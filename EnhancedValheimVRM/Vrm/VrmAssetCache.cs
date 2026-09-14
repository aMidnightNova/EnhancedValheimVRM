using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HarmonyLib;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using VRM;
using VRMShaders;
using Object = UnityEngine.Object;

namespace EnhancedValheimVRM
{
    // Main-thread dictionary; source inspection/parsing happens on workers. Entries live
    // for the game process, including across scenes and Player/corpse destruction.
    internal static class VrmAssetCache
    {
        internal sealed class Source
        {
            public string Path, Key, OutfitPath, OutfitText, Name;
            public byte[] Bytes;
            public VrmSettings Settings;
            public OutfitConfig Outfits;
        }

        internal sealed class Entry
        {
            public GameObject Root;
            public string Path;
            public int LiveClones;
            public bool Completed;
            public bool Imported;
            public Exception Error;
            public string ImportTiming;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

        private static readonly Dictionary<string, string> Latest = new Dictionary<string, string>();

        // Thread-safe view of imported keys, so a receive worker can skip fetching a model that
        // is already imported and only the small settings blob has changed.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ImportedKeys =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

        internal static bool IsImportedKey(string key)
        {
            return key != null && ImportedKeys.ContainsKey(key);
        }

        internal static string SharedKey(long characterId, string version, VrmSettings settings)
        {
            return "shared-version:" + characterId + ":" + version + BakedSuffix(settings);
        }

        // Only baked material options invalidate imports. Socket, sizing, outfit
        // and spring changes reapply to fresh clones of the existing import.
        private static string BakedSuffix(VrmSettings settings)
        {
            return ":" + settings.ModelBrightness.ToString("R", CultureInfo.InvariantCulture) + ":" +
                settings.UseMToonShader + ":" + settings.AttemptTextureFix + ":" +
                (settings.UsesCreatureShader ? "creature" : "player") + ":" +
                settings.TextureFixEmission.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static void Retain(Entry entry)
        {
            entry.LiveClones++;
        }

        internal static void Release(Entry entry)
        {
            entry.LiveClones--;
            RetireSuperseded(entry.Path);
        }

        private static void RetireSuperseded(string path)
        {
            if (!Latest.TryGetValue(path, out var latest) || !Entries.TryGetValue(latest, out var replacement) ||
                !replacement.Imported)
                return;
            var retired = new List<string>();
            foreach (var pair in Entries)
            {
                if (pair.Key != latest && pair.Value.Path == path && pair.Value.Completed && pair.Value.LiveClones == 0)
                {
                    if (pair.Value.Root != null) Object.Destroy(pair.Value.Root);
                    retired.Add(pair.Key);
                }
            }

            foreach (var key in retired)
            {
                Entries.Remove(key);
                ImportedKeys.TryRemove(key, out _);
            }
        }

        private sealed class PhaseScope : IDisposable
        {
            private readonly Action _done;

            internal PhaseScope(Action done)
            {
                _done = done;
            }

            public void Dispose()
            {
                _done();
            }
        }

        private static void Finish(Entry entry, RuntimeGltfInstance loaded)
        {
            entry.Root = loaded.Root;
            var animator = entry.Root != null ? entry.Root.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidDataException("VRM requires a valid humanoid avatar.");
            Object.DontDestroyOnLoad(entry.Root);
            entry.Root.SetActive(false);
            var owner = entry.Root.AddComponent<SharedVrmLifetime>();
            loaded.TransferOwnership((key, resource) => owner.ExtraResources.Add(resource));
            foreach (var renderer in loaded.VisibleRenderers) renderer.enabled = true;
            entry.Imported = true;
        }

        // Must only be called on a worker, including file metadata and outfit parsing.
        internal static Source ReadSource(string name, bool useDefault, Sharing.AvatarBundle bundle)
        {
            var source = new Source { Name = name };
            try
            {
                if (bundle != null)
                {
                    source.Bytes = bundle.Vrm; // Null when the model is already imported; the key finds it.
                    source.Path = "shared-" + bundle.CharacterId + ".vrm";
                    source.Settings = new VrmSettings(name, bundle.Settings);
                    if (!string.IsNullOrEmpty(bundle.VerifiedVersion))
                        source.Key = SharedKey(bundle.CharacterId, bundle.VerifiedVersion, source.Settings);
                    else if (source.Bytes == null)
                        throw new InvalidDataException("A shared model without a verified version needs its bytes.");
                    else
                    {
                        using (var hash = SHA256.Create())
                        {
                            source.Key = "shared:" + bundle.CharacterId + ":" +
                                Convert.ToBase64String(hash.ComputeHash(source.Bytes)) + BakedSuffix(source.Settings);
                        }
                    }

                    source.OutfitText = bundle.Outfits;
                }
                else
                {
                    source.Path = Constants.Vrm.Find(name + ".vrm");
                    if (!File.Exists(source.Path))
                    {
                        if (!useDefault || !File.Exists(Constants.Vrm.DefaultPath))
                            throw new FileNotFoundException("No avatar for " + name);
                        source.Path = Constants.Vrm.DefaultPath;
                        name = Path.GetFileNameWithoutExtension(Constants.Vrm.DefaultName);
                    }

                    source.Settings = new VrmSettings(name);
                    var file = new FileInfo(source.Path);
                    source.Key = source.Path + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks;
                    source.OutfitPath = Constants.Vrm.Find(
                        "outfits_" + Path.GetFileNameWithoutExtension(source.Path) + ".txt");
                    if (File.Exists(source.OutfitPath))
                    {
                        if (new FileInfo(source.OutfitPath).Length > Sharing.SharingWire.MaxSettingsBytes)
                            throw new InvalidDataException("Outfit file exceeds 128 KiB.");
                        source.OutfitText = File.ReadAllText(source.OutfitPath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(source.OutfitText))
                {
                    try
                    {
                        source.Outfits = OutfitConfig.Parse(source.OutfitText);
                    }
                    catch (Exception error) when (error is InvalidDataException || error is ArgumentException)
                    {
                        Logger.LogWarning("Outfit file: " + error.Message);
                        source.OutfitText = null;
                    }
                }

                if (bundle == null) source.Key += BakedSuffix(source.Settings);
                return source;
            }
            catch
            {
                if (source.Bytes != null) Array.Clear(source.Bytes, 0, source.Bytes.Length);
                throw;
            }
        }

        internal static bool IsImported(string key)
        {
            return Entries.TryGetValue(key, out var entry) && entry.Imported && entry.Root != null;
        }

        internal static Entry GetForMenu(Source source)
        {
            Latest[source.Path] = source.Key;
            if (Entries.TryGetValue(source.Key, out var cached) && cached.Imported && cached.Root != null)
                return cached;

            // Match the previous commit's local/menu loader, retaining the result
            // so character switches and world entry do not import it again.
            var entry = new Entry { Path = source.Path };
            ImporterContext importer = null;
            try
            {
                var timer = Settings.LogLoadTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
                PatchShaderFind.EnsureLoadedForMenu();
                var shadersMs = timer?.Elapsed.TotalMilliseconds ?? 0;
                var bytes = File.ReadAllBytes(source.Path);
                var readMs = timer?.Elapsed.TotalMilliseconds ?? 0;
                var data = new GlbBinaryParser(bytes, source.Path).Parse();
                try
                {
                    importer = new VRMImporterContext(new VRMData(data));
                }
                catch (NotVrm0Exception)
                {
                    importer = new Vrm10Importer(Vrm10Data.Parse(data));
                }

                var parseMs = timer?.Elapsed.TotalMilliseconds ?? 0;
                var loaded = importer.Load();
                if (timer != null)
                {
                    entry.ImportTiming = string.Format(CultureInfo.InvariantCulture,
                        "cold shaders={0:F0}ms, read={1:F0}ms, parse/context={2:F0}ms, native={3:F0}ms",
                        shadersMs,
                        readMs - shadersMs,
                        parseMs - readMs,
                        (timer?.Elapsed.TotalMilliseconds ?? 0) - parseMs);
                }

                Finish(entry, loaded);
                Entries[source.Key] = entry;
                ImportedKeys[source.Key] = 0;
            }
            catch
            {
                var root = entry.Root ?? (importer == null ? null : PersistentImportAwaitCaller.GetRoot(importer));
                if (root != null) Object.Destroy(root);
                throw;
            }
            finally
            {
                importer?.Dispose();
            }

            CoroutineHelper.Instance.StartCoroutine(ProcessMaterials(entry, source));
            return entry;
        }

        internal static Entry Get(Source source)
        {
            Latest[source.Path] = source.Key;
            if (Entries.TryGetValue(source.Key, out var entry))
            {
                // A second received bundle must not retain a plaintext copy on a hit.
                var unused = source.Bytes;
                source.Bytes = null;
                if (unused != null) Task.Run(() => Array.Clear(unused, 0, unused.Length));
                return entry;
            }

            entry = new Entry { Path = source.Path };
            Entries.Add(source.Key, entry);
            CoroutineHelper.Instance.StartCoroutine(Track(entry, source));
            return entry;
        }

        private static IEnumerator Track(Entry entry, Source source)
        {
            var work = Import(entry, source);
            try
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        more = work.MoveNext();
                    }
                    catch (Exception error)
                    {
                        entry.Error = error;
                        break;
                    }

                    if (!more) break;
                    yield return work.Current;
                }
            }
            finally
            {
                (work as IDisposable)?.Dispose();
                var bytes = source.Bytes;
                source.Bytes = null;
                if (bytes != null) Task.Run(() => Array.Clear(bytes, 0, bytes.Length));
                if (entry.Error != null)
                {
                    if (entry.Root != null) Object.Destroy(entry.Root);
                    // Do not remove a newer synchronous menu import of this source.
                    if (Entries.TryGetValue(source.Key, out var current) && ReferenceEquals(current, entry))
                    {
                        Entries.Remove(source.Key);
                        ImportedKeys.TryRemove(source.Key, out _);
                    }
                }

                entry.Completed = true;
            }
        }

        private static IEnumerator Import(Entry entry, Source source)
        {
            // Parse concurrently; native imports share one main-thread frame budget.
            var timer = Settings.LogLoadTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
            if (timer != null) Logger.Log("Avatar import for " + source.Name + ": read/parse started");
            var parsing = Task.Run<object>(() =>
            {
                if (source.Bytes == null && !File.Exists(source.Path))
                {
                    throw new InvalidOperationException(
                        "The shared model is no longer imported; it will be fetched again.");
                }

                var bytes = source.Bytes ?? File.ReadAllBytes(source.Path);
                var data = new GlbBinaryParser(bytes, source.Path).Parse();
                try
                {
                    return new VRMData(data);
                }
                catch (NotVrm0Exception)
                {
                    return Vrm10Data.Parse(data);
                }
            });
            while (!parsing.IsCompleted) yield return null;
            var parsed = parsing.GetAwaiter().GetResult();
            var parseMs = timer?.Elapsed.TotalMilliseconds ?? 0;
            if (timer != null)
            {
                Logger.Log("Avatar import for " + source.Name + ": read/parse completed in " + parseMs.ToString("F0") +
                    "ms; shader preparation started");
            }

            var queueMs = timer?.Elapsed.TotalMilliseconds ?? 0;
            ImporterContext importer = null;
            try
            {
                yield return PatchShaderFind.EnsureLoaded();
                var shadersMs = timer?.Elapsed.TotalMilliseconds ?? 0;
                importer = parsed is VRMData vrm0
                    ? (ImporterContext)new VRMImporterContext(vrm0, null, new TextureDeserializerAsync())
                    : new Vrm10Importer((Vrm10Data)parsed, null, new TextureDeserializerAsync());
                var caller = new PersistentImportAwaitCaller(importer);
                if (timer != null) Logger.Log("Avatar import for " + source.Name + ": native import started");
                // univrm reports each phase through this hook. keep the longest single call per phase so a
                // spike can be named. only with the timing log on.
                var phases = timer != null ? new Dictionary<string, double>() : null;
                if (phases != null) FrameClock.ResetWorst();
                var loading = importer.LoadAsync(caller,
                    phases == null
                        ? null
                        : (Func<string, IDisposable>)(name =>
                        {
                            var clock = System.Diagnostics.Stopwatch.StartNew();
                            caller.Phase = name;
                            return new PhaseScope(() =>
                            {
                                phases.TryGetValue(name, out var worst);
                                phases[name] = Math.Max(worst, clock.Elapsed.TotalMilliseconds);
                            });
                        }));
                while (!loading.IsCompleted)
                {
                    caller.Protect();
                    yield return null;
                }

                var loaded = loading.GetAwaiter().GetResult();
                if (timer != null)
                {
                    entry.ImportTiming = string.Format(CultureInfo.InvariantCulture,
                        "cold read/parse={0:F0}ms, queue={1:F0}ms, shaders={2:F0}ms, native/frames={3:F0}ms ({4})",
                        parseMs,
                        queueMs - parseMs,
                        shadersMs - queueMs,
                        (timer?.Elapsed.TotalMilliseconds ?? 0) - shadersMs,
                        caller.SliceStats);
                    entry.ImportTiming += string.Format(CultureInfo.InvariantCulture,
                        "; worst frame={0:F1}ms; longest main thread stretch {2}; longest phase calls: {1}",
                        FrameClock.WorstFrameMs,
                        string.Join(", ",
                            phases.OrderByDescending(p => p.Value)
                                .Take(4)
                                .Select(p =>
                                    p.Key + "=" + p.Value.ToString("F1", CultureInfo.InvariantCulture) + "ms")),
                        caller.LongestStep);
                }

                if (timer != null) Logger.Log("Avatar import for " + source.Name + ": " + entry.ImportTiming);
                Finish(entry, loaded);
                ImportedKeys[source.Key] = 0;
                yield return ProcessMaterials(entry, source);
            }
            finally
            {
                if (entry.Root == null && importer != null)
                {
                    var root = PersistentImportAwaitCaller.GetRoot(importer);
                    if (root != null) Object.Destroy(root);
                    foreach (var node in importer.Nodes)
                    {
                        if (node != null && node.parent == null && node.gameObject != root)
                            Object.Destroy(node.gameObject);
                    }
                }

                importer?.Dispose();
            }
        }

        private static IEnumerator ProcessMaterials(Entry entry, Source source)
        {
            var materials = VrmInstance.PrepareCachedMaterials(entry.Root, source.Settings);
            try
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        more = materials.MoveNext();
                    }
                    catch (Exception error)
                    {
                        Logger.LogWarning(
                            "VRM material conversion failed; keeping imported materials: " + error.Message);
                        break;
                    }

                    if (!more) break;
                    yield return materials.Current;
                }
            }
            finally
            {
                (materials as IDisposable)?.Dispose();
                entry.Completed = true;
                entry.Root?.GetComponent<SharedVrmLifetime>()?.NotifyMaterial(null);
                RetireSuperseded(entry.Path);
            }
        }
    }
}
