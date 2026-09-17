using System;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [DefaultExecutionOrder(10000)]
    public sealed class OutfitController : MonoBehaviour
    {
        private Player _player;
        private string _path;
        private OutfitConfig _config;
        private bool _staging;

        internal void SetStaging(bool staging)
        {
            _staging = staging;
            foreach (var entry in _originalVisibility)
            {
                if (entry.Key != null)
                {
                    entry.Key.forceRenderingOff = entry.Value;
                    if (!entry.Value)
                    {
                        entry.Key.enabled = true;
                        // Activate the mesh ancestry only within this imported avatar.
                        for (var node = entry.Key.transform; node != null && node != transform; node = node.parent)
                        {
                            if (!node.gameObject.activeSelf)
                            {
                                _activatedNodes.Add(node.gameObject);
                                node.gameObject.SetActive(true);
                            }
                        }
                    }
                }
            }

            ApplyValues();
        }

        private readonly Dictionary<Renderer, bool> _originalVisibility = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Renderer, bool> _originalEnabled = new Dictionary<Renderer, bool>();
        private readonly HashSet<GameObject> _activatedNodes = new HashSet<GameObject>();

        private readonly Dictionary<SkinnedMeshRenderer, float[]> _originalWeights =
            new Dictionary<SkinnedMeshRenderer, float[]>();

        private readonly Dictionary<Renderer, bool> _visibility = new Dictionary<Renderer, bool>();

        private readonly Dictionary<SkinnedMeshRenderer, Dictionary<int, float>> _weights =
            new Dictionary<SkinnedMeshRenderer, Dictionary<int, float>>();

        public string CurrentName { get; private set; } = "";

        public string SourceText { get; private set; } = "";

        public IEnumerable<string> Names =>
            _config?.Outfits.Select(outfit => outfit.Name) ?? Enumerable.Empty<string>();

        internal void SetupPrepared(Player player, string path, string text, bool isShared, OutfitConfig prepared)
        {
            _player = player;
            _path = isShared ? null : path;
            // Snapshot only imported meshes, before equipment sockets enter the hierarchy.
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                _originalVisibility[renderer] = renderer.forceRenderingOff;
                _originalEnabled[renderer] = renderer.enabled;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    var weights = new float[skin.sharedMesh.blendShapeCount];
                    for (var index = 0; index < weights.Length; index++)
                        weights[index] = skin.GetBlendShapeWeight(index);
                    _originalWeights[skin] = weights;
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                // No definitions: show every imported mesh, preserving authored shape weights.
                text = DefaultTemplate();
                prepared = null;
            }

            if (prepared == null)
                LoadText(text);
            else
                ApplyConfig(text, prepared);
        }

        private bool LoadText(string text)
        {
            try
            {
                var config = OutfitConfig.Parse(text);
                ApplyConfig(text, config);
                return true;
            }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException)
            {
                Logger.LogWarning("Outfit file: " + error.Message);
                return false;
            }
        }

        private void ApplyConfig(string text, OutfitConfig config)
        {
            Restore();
            _config = config;
            SourceText = text;
            var outfit = config.Find(CurrentName) ?? config.Default;
            if (outfit != null) Apply(outfit.Name);
        }

        private bool _reloading;

        public string Reload()
        {
            if (_path == null) return "Shared outfit definitions come from the avatar owner.";
            if (_reloading) return "Outfit reload is already in progress.";
            StartCoroutine(ReloadAsync());
            return "Reading outfit file in the background.";
        }

        private IEnumerator ReloadAsync()
        {
            _reloading = true;
            var path = _path;
            var read = Task.Run(() =>
            {
                if (new FileInfo(path).Length > Sharing.SharingWire.MaxSettingsBytes)
                    throw new InvalidDataException("Outfit file exceeds 128 KiB.");
                var text = File.ReadAllText(path);
                return Tuple.Create(text, OutfitConfig.Parse(text));
            });
            try
            {
                while (!read.IsCompleted) yield return null;
                if (read.IsFaulted)
                {
                    Logger.LogWarning("Outfit reload failed; previous outfits retained: " +
                        read.Exception.GetBaseException().Message);
                    yield break;
                }

                var result = read.GetAwaiter().GetResult();
                ApplyConfig(result.Item1, result.Item2);
                FileTransferController.RefreshUpload();
                Logger.Log("Reloaded outfits from " + path);
            }
            finally
            {
                _reloading = false;
            }
        }

        public bool Apply(string name)
        {
            var outfit = _config?.Find(name);
            if (outfit == null) return false;
            Restore();
            foreach (var entry in outfit.Meshes)
            {
                var renderers = _originalVisibility.Keys
                    .Where(renderer => renderer != null && renderer.name == entry.Key)
                    .ToArray();
                if (renderers.Length == 0)
                    Logger.LogOnce("outfit-mesh:" + entry.Key, "Outfit mesh not found: " + entry.Key);
                foreach (var renderer in renderers) _visibility[renderer] = !entry.Value;
            }

            foreach (var entry in outfit.Blendshapes)
            {
                var separator = entry.Key.IndexOf(':');
                string meshName = entry.Key.Substring(0, separator), shapeName = entry.Key.Substring(separator + 1);
                var found = false;
                foreach (var skin in _originalWeights.Keys.Where(skin => skin != null && skin.name == meshName))
                {
                    var index = skin.sharedMesh.GetBlendShapeIndex(shapeName);
                    if (index < 0) continue;
                    found = true;
                    if (!_weights.TryGetValue(skin, out var values))
                        _weights.Add(skin, values = new Dictionary<int, float>());
                    values[index] = entry.Value;
                }

                if (!found) Logger.LogOnce("outfit-shape:" + entry.Key, "Outfit blendshape not found: " + entry.Key);
            }

            CurrentName = outfit.Name;
            ApplyValues();
            OutfitRpc.LocalChanged(_player, this);
            return true;
        }

        private void ApplyValues()
        {
            foreach (var entry in _visibility)
            {
                if (entry.Key != null)
                {
                    entry.Key.forceRenderingOff = entry.Value;
                    if (!entry.Value)
                    {
                        entry.Key.enabled = true;
                        // Activate the mesh ancestry only within this imported avatar.
                        for (var node = entry.Key.transform; node != null && node != transform; node = node.parent)
                        {
                            if (!node.gameObject.activeSelf)
                            {
                                _activatedNodes.Add(node.gameObject);
                                node.gameObject.SetActive(true);
                            }
                        }
                    }
                }
            }

            foreach (var mesh in _weights)
            {
                if (mesh.Key != null)
                {
                    foreach (var entry in mesh.Value) mesh.Key.SetBlendShapeWeight(entry.Key, entry.Value);
                }
            }

            if (_staging)
            {
                foreach (var renderer in _originalVisibility.Keys)
                {
                    if (renderer != null) renderer.forceRenderingOff = true;
                }
            }
        }

        private void Restore()
        {
            foreach (var entry in _visibility)
            {
                if (entry.Key != null)
                {
                    entry.Key.forceRenderingOff = _originalVisibility[entry.Key];
                    entry.Key.enabled = _originalEnabled[entry.Key];
                }
            }

            foreach (var mesh in _weights)
            {
                if (mesh.Key != null)
                {
                    foreach (var entry in mesh.Value)
                        mesh.Key.SetBlendShapeWeight(entry.Key, _originalWeights[mesh.Key][entry.Key]);
                }
            }

            foreach (var node in _activatedNodes)
            {
                if (node != null) node.SetActive(false);
            }

            _activatedNodes.Clear();
            _visibility.Clear();
            _weights.Clear();
        }

        private string DefaultTemplate()
        {
            var text = new StringBuilder(
                "# Named outfits; exactly one section has Default=True.\n[default]\nDefault=True\n");
            foreach (var name in _originalVisibility.Keys.Where(r => r != null).Select(r => r.name).Distinct())
                text.Append("mesh:").Append(name).Append("=True\n");
            return text.ToString();
        }

        public string Generate()
        {
            if (_path == null) return "Generate outfits on the avatar owner's client.";
            string path = _path, text = DefaultTemplate();
            Task.Run(() =>
            {
                try
                {
                    // CreateNew is atomic: never overwrite even if a file appears concurrently.
                    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(file))
                        writer.Write(text);
                    Logger.Log("Created default outfit: " + path + ". Use /vrm outfit reload to load it.");
                }
                catch (IOException error)
                {
                    Logger.LogWarning(File.Exists(path)
                        ? "Outfit file already exists; left unchanged."
                        : "Cannot generate outfit: " + error.Message);
                }
            });
            return "Creating default outfit file if none exists.";
        }

        // writes a [Blendshapes] section with every blendshape the avatar has. (commented out)
        // appended to the outfit file, or the file is created with just that section
        public string GenerateBlendShapeList()
        {
            if (_path == null) return "Generate the blendshape list on the avatar owner's client.";
            var names = new List<string>();
            foreach (var skin in _originalWeights.Keys)
            {
                if (skin == null || skin.sharedMesh == null) continue;
                for (var i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                {
                    var name = skin.sharedMesh.GetBlendShapeName(i);
                    if (!names.Contains(name)) names.Add(name);
                }
            }

            if (names.Count == 0) return "This avatar has no blendshapes.";
            var text = new StringBuilder("\n[" + OutfitConfig.KeepSection + "]\n");
            text.Append(
                "# every blendshape this avatar has. only the ones the vrm expressions or this file use get loaded,\n");
            text.Append("# remove the # in front of a name to keep it loaded\n");
            foreach (var name in names) text.Append('#').Append(name).Append('\n');
            string path = _path, section = text.ToString();
            Task.Run(() =>
            {
                try
                {
                    if (File.Exists(path) && File.ReadAllText(path)
                            .IndexOf("[" + OutfitConfig.KeepSection + "]",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Logger.LogWarning("The outfit file already has a [" + OutfitConfig.KeepSection +
                            "] section; left unchanged.");
                        return;
                    }

                    File.AppendAllText(path, section);
                    Logger.Log("Blendshape list written to " + path + ". Use /vrm outfit reload after editing it.");
                }
                catch (IOException error)
                {
                    Logger.LogWarning("Cannot write the blendshape list: " + error.Message);
                }
            });
            return "Writing " + names.Count + " blendshape names to the outfit file.";
        }

        // the weight a shape had when the avatar was imported, what a face hands back when it goes idle
        internal float OriginalWeight(SkinnedMeshRenderer skin, int index)
        {
            return _originalWeights.TryGetValue(skin, out var weights) && index >= 0 && index < weights.Length
                ? weights[index]
                : 0f;
        }

        public bool Toggle(string name)
        {
            var mesh = _originalVisibility.Keys.FirstOrDefault(r => r != null && r.name == name);
            if (mesh == null) return false;
            var hidden = _visibility.TryGetValue(mesh, out var value)
                ? value
                : mesh.forceRenderingOff || !mesh.enabled || !mesh.gameObject.activeInHierarchy;
            return Override(name, false, hidden ? 1 : 0, true);
        }

        public bool Override(string name, bool blend, float value, bool publish = false)
        {
            if (!Sharing.SharingWire.IsValidOverride(name, blend, value)) return false;
            var found = false;
            if (!blend)
            {
                foreach (var mesh in _originalVisibility.Keys.Where(r => r != null && r.name == name))
                {
                    _visibility[mesh] = value == 0;
                    found = true;
                }
            }
            else
            {
                foreach (var skin in _originalWeights.Keys.Where(r => r != null))
                {
                    var index = skin.sharedMesh.GetBlendShapeIndex(name);
                    if (index < 0) continue;
                    if (!_weights.TryGetValue(skin, out var values))
                        _weights[skin] = values = new Dictionary<int, float>();
                    values[index] = value;
                    found = true;
                }
            }

            if (!found) return false;
            ApplyValues();
            if (publish && _player == Player.m_localPlayer)
                SharingRpc.SetOverride(_player.GetPlayerID(), name, blend, value);
            return true;
        }

        private void LateUpdate()
        {
            // Blendshape animation may overwrite these; renderer visibility only changes
            // when selecting/staging an outfit, so it does not need per-frame writes.
            foreach (var mesh in _weights)
            {
                if (mesh.Key != null)
                {
                    foreach (var entry in mesh.Value) mesh.Key.SetBlendShapeWeight(entry.Key, entry.Value);
                }
            }
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
