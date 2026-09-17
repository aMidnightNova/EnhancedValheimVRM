using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EnhancedValheimVRM
{
    public class VrmInstance
    {
        private enum State
        {
            Loading,
            Staged,
            Displayed,
            Corpse,
            Disposed
        }

        private State _state;
        public bool IsLoading => _state == State.Loading;

        public bool IsShared { get; private set; }

        // Set by the animator when the game marks this player dead; viewers lose the ZDO before
        // the player object is destroyed, so IsDead() alone is not reliable at that moment.
        internal bool DeathSeen;

        public bool IsCorpse
        {
            get => _state == State.Corpse;
            internal set
            {
                if (value) _state = State.Corpse;
            }
        }

        private bool _disposed => _state == State.Disposed;

        internal Vector3 LodReferencePoint { get; private set; }

        public bool IsReady => _state == State.Staged || _state == State.Displayed || _state == State.Corpse;
        internal bool IsDisplayed => _state == State.Displayed || _state == State.Corpse;

        // measured once on load. x 1 = measured (0 = hips only), y thigh depth diff, z calf thickness diff
        internal Vector3 SeatProportions { get; private set; }

        internal void ShowModel()
        {
            _state = State.Displayed;
            TraceLoad("visible commit");
            if (_vrmGo != null) _vrmGo.GetComponent<OutfitController>()?.SetStaging(false);
            if (_loadClock == null) return;
            _loadClock.Stop();
            var path = IsShared ? "shared" : _synchronousMenuLoad ? "menu" : "world";
            Logger.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Avatar ready: {0} ({1}, cache {2}); total={3:F0}ms, source={4:F0}ms, import/wait={5:F0}ms, clone={6:F0}ms, material copies={7:F0}ms, outfits/sizing={8:F0}ms, game setup={9:F0}ms{10}",
                _playerName,
                path,
                _cacheHit ? "hit" : "miss",
                LoadMilliseconds,
                _sourceReadyMs,
                _importReadyMs - _sourceReadyMs,
                _cloneReadyMs - _importReadyMs,
                _materialsReadyMs - _cloneReadyMs,
                _modelReadyMs - _materialsReadyMs,
                LoadMilliseconds - _modelReadyMs,
                (_cacheHit ? "" : "; " + _coldImportTiming) + (_setupTiming == null ? "" : "; " + _setupTiming) +
                (IsShared
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "; player-to-avatar={0:F0}ms, visible wait={3:F0}ms, before importer={1:F0}ms; {2}",
                        _beforeImportMilliseconds + LoadMilliseconds,
                        _beforeImportMilliseconds,
                        _receiveTiming,
                        VisibleWaitMilliseconds)
                    : "")));
        }

        private GameObject _vrmGo;
        private readonly string _playerName;
        private VrmSettings _settings;
        private string _vrmPath;
        private Player _player;
        private readonly Task<VrmAssetCache.Source> _source;
        private readonly bool _synchronousMenuLoad;

        private readonly System.Diagnostics.Stopwatch _loadClock =
            Settings.LogLoadTiming ? System.Diagnostics.Stopwatch.StartNew() : null;

        private double _sourceReadyMs, _importReadyMs, _cloneReadyMs, _materialsReadyMs, _modelReadyMs;
        private bool _cacheHit;
        private string _coldImportTiming;
        private double _beforeImportMilliseconds;
        private string _receiveTiming, _setupTiming;

        internal void SetSetupTiming(string text)
        {
            _setupTiming = text;
        }

        internal void SetReceiveTiming(string text, double milliseconds)
        {
            _receiveTiming = text;
            _beforeImportMilliseconds = milliseconds;
        }

        private void TraceLoad(string stage)
        {
            if (_loadClock != null && IsShared)
            {
                Logger.Log("Avatar load " + _player.GetPlayerID() + " import+" + _loadClock.ElapsedMilliseconds +
                    "ms: " + stage);
            }
        }

        private double LoadMilliseconds => _loadClock?.Elapsed.TotalMilliseconds ?? 0;

        // Time this avatar was missing while the local player could see the world: measured from
        // the later of the local spawn and the server's offer. Comparable between machines.
        private double VisibleWaitMilliseconds
        {
            get
            {
                var sinceSpawn = FileTransferController.MillisecondsSinceLocalSpawn;
                if (sinceSpawn < 0) return 0;
                var sinceAvailable =
                    _player != null ? SharingRpc.MillisecondsSinceAvailable(_player.GetPlayerID()) : -1;
                return sinceAvailable < 0 ? sinceSpawn : Math.Min(sinceSpawn, sinceAvailable);
            }
        }

        public VrmInstance(Player player) : this(player, null) { }

        public VrmInstance(Player player, Sharing.AvatarBundle bundle)
        {
            _player = player;
            _playerName = player.GetPlayerDisplayName();
            IsShared = bundle != null;
            TraceLoad("source preparation scheduled");
            // Snapshot Unity/config state before leaving the main thread.
            var name = _playerName;
            var useDefault = Settings.UseDefaultVrm;
            _synchronousMenuLoad = bundle == null && player.IsInStartMenu();
            _source = _synchronousMenuLoad
                ? Task.FromResult(VrmAssetCache.ReadSource(name, useDefault, null))
                : Task.Run(() => VrmAssetCache.ReadSource(name, useDefault, bundle));
            if (_synchronousMenuLoad)
            {
                // The original menu path loaded inside Player.Awake. Do not put
                // a coroutine, async clone or menu-reveal gate in that path.
                var loading = LoadTracked();
                try
                {
                    while (loading.MoveNext())
                        throw new InvalidOperationException("The synchronous menu load unexpectedly yielded.");
                }
                finally
                {
                    (loading as IDisposable)?.Dispose();
                }
            }
            else
                CoroutineHelper.Instance.StartCoroutine(LoadTracked());
        }

        private IEnumerator LoadTracked()
        {
            var loading = LoadVrmAsync();
            try
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        more = loading.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        // A remote player without an installed local file is normal;
                        // the TCP client may supply that avatar later.
                        if (IsShared || !(ex is System.IO.FileNotFoundException))
                            Logger.LogError(_playerName + ": model failed to load: " + ex);
                        yield break;
                    }

                    if (!more) break;
                    yield return loading.Current;
                }
            }
            finally
            {
                (loading as IDisposable)?.Dispose();
                if (!IsReady) Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _state = State.Disposed;
            var corpseOwnsResources = _vrmGo != null && _vrmGo.GetComponentInParent<Ragdoll>() != null;
            if (!corpseOwnsResources)
            {
                if (_vrmGo != null)
                {
                    _vrmGo.GetComponent<SharedVrmLifetime>()?.Release();
                    Object.Destroy(_vrmGo);
                }
                // Imported assets belong to the session cache, not this player incarnation.
            }

            _vrmGo = null;
        }

        public string GetVrmFilePath()
        {
            return _vrmPath;
        }

        public GameObject GetGameObject()
        {
            return _vrmGo;
        }

        public Animator GetVrmGoAnimator()
        {
            if (_vrmGo == null) return null;

            return _vrmGo.GetComponentInChildren<Animator>();
        }

        public void ReloadSettings()
        {
            if (IsShared || IsCorpse || _player == null) return;
            VrmController.ReloadPlayer(_player);
        }

        public VrmSettings GetSettings()
        {
            return _settings;
        }

        private IEnumerator LoadVrmAsync()
        {
            while (!_source.IsCompleted) yield return null;
            var source = _source.GetAwaiter().GetResult(); // Completion checked; never waits.
            _settings = source.Settings;
            _vrmPath = source.Path;
            _sourceReadyMs = LoadMilliseconds;
            TraceLoad("source prepared; cached import lookup");
            _cacheHit = VrmAssetCache.IsImported(source.Key);
            // A cache import outlives a cancelled menu preview or departing player.
            var entry = _synchronousMenuLoad ? VrmAssetCache.GetForMenu(source) : VrmAssetCache.Get(source);
            while (!entry.Imported && !entry.Completed) yield return null;
            if (entry.Error != null) throw new InvalidOperationException("VRM cache import failed.", entry.Error);
            if (_disposed || _player == null) yield break;
            _importReadyMs = LoadMilliseconds;
            _coldImportTiming = entry.ImportTiming;
            TraceLoad("import ready (cache " + (_cacheHit ? "hit" : "miss") + "); " + entry.ImportTiming +
                "; clone started");
            VrmAssetCache.Retain(entry);
            var leaseTransferred = false;
            try
            {
                var animator = _player.GetField<Player, Animator>("m_animator");
                if (_synchronousMenuLoad)
                    _vrmGo = Object.Instantiate(entry.Root, animator.transform.parent, false);
                else
                {
                    var cloning = Object.InstantiateAsync(entry.Root,
                        new InstantiateParameters { parent = animator.transform.parent, worldSpace = false });
                    while (!cloning.isDone) yield return null;
                    _vrmGo = cloning.Result[0];
                }

                if (_disposed || _player == null)
                {
                    Object.Destroy(_vrmGo);
                    _vrmGo = null;
                    yield break;
                }

                _cloneReadyMs = LoadMilliseconds;
                TraceLoad("clone ready; material copies and outfit/size setup started");
                _vrmGo.transform.localScale = Vector3.one * _settings.ModelScale;
                _vrmGo.transform.localPosition = animator.transform.localPosition;
                _vrmGo.name = Constants.Vrm.GoName;
                var lifetime = _vrmGo.GetComponent<SharedVrmLifetime>();
                lifetime.CacheReleased = () => VrmAssetCache.Release(entry);
                leaseTransferred = true;
                // Textures/meshes stay cached. Each live avatar owns mutable material copies.
                var copies = new Dictionary<Material, Material>();
                var copyBudget = System.Diagnostics.Stopwatch.StartNew();
                var copySliceMs = PersistentImportAwaitCaller.SpareFrameMs();
                foreach (var renderer in _vrmGo.GetComponentsInChildren<Renderer>(true))
                {
                    if (_disposed || _player == null) yield break;
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        var original = materials[i];
                        if (original == null) continue;
                        if (!copies.TryGetValue(original, out var copy))
                        {
                            copy = new Material(original);
                            copies.Add(original, copy);
                            lifetime.ExtraResources.Add(copy);
                        }

                        materials[i] = copy;
                    }

                    renderer.sharedMaterials = materials;
                    if (!_synchronousMenuLoad && copyBudget.Elapsed.TotalMilliseconds >= copySliceMs)
                    {
                        yield return null;
                        copyBudget.Restart();
                        copySliceMs = PersistentImportAwaitCaller.SpareFrameMs();
                    }
                }

                _materialsReadyMs = LoadMilliseconds;
                if (_disposed || _player == null) yield break;
                var outfits = _vrmGo.AddComponent<OutfitController>();
                outfits.SetupPrepared(_player, source.OutfitPath, source.OutfitText, IsShared, source.Outfits);
                outfits.SetStaging(true);
                CreateVrmGo();
                SetupVrm();
                _modelReadyMs = LoadMilliseconds;
                CoroutineHelper.Instance.StartCoroutine(RefreshCachedMaterials(entry, copies));
            }
            finally
            {
                if (!leaseTransferred) VrmAssetCache.Release(entry);
            }
        }

        private IEnumerator RefreshCachedMaterials(VrmAssetCache.Entry entry, Dictionary<Material, Material> copies)
        {
            var model = _vrmGo;
            var template = entry.Root.GetComponent<SharedVrmLifetime>();
            var lifetime = model.GetComponent<SharedVrmLifetime>();
            Action<Material> apply = null;
            apply = material =>
            {
                if (model == null) return;
                foreach (var pair in copies)
                {
                    if (pair.Key == null || pair.Value == null || (material != null && material != pair.Key)) continue;
                    pair.Value.shader = pair.Key.shader;
                    pair.Value.CopyPropertiesFromMaterial(pair.Key);
                    model.GetComponent<VrmMToonFix>()?.RefreshMaterialBaseline(pair.Value);
                }

                if (material == null && template != null) template.MaterialReady -= apply;
            };
            if (entry.Completed)
                apply(null);
            else
            {
                foreach (var material in template.ConvertedMaterials) apply(material);
                template.MaterialReady += apply;
                lifetime.CacheReleased += () =>
                {
                    if (template != null) template.MaterialReady -= apply;
                };
            }

            yield break;
        }

        private void CreateVrmGo()
        {
            var lodGroupPlayer = _player.GetComponentInChildren<LODGroup>();

            var lodGroup = _vrmGo.AddComponent<LODGroup>();
            if (_settings.EnablePlayerFade)
            {
                //TODO: determine if regular Renderers need to be put into the lod group. and then any armors added
                lodGroup.SetLODs(new LOD[]
                {
                    new LOD(0.1f,
                        _vrmGo.GetComponentsInChildren<Renderer>(true)
                            .Where(renderer =>
                                renderer is SkinnedMeshRenderer || renderer is MeshRenderer)
                            .ToArray())
                });
            }

            lodGroup.RecalculateBounds();
            LodReferencePoint = lodGroup.localReferencePoint;

            if (lodGroupPlayer != null)
            {
                lodGroup.fadeMode = lodGroupPlayer.fadeMode;
                lodGroup.animateCrossFading = lodGroupPlayer.animateCrossFading;
            }
        }

        //

        private void SetupVrm()
        {
            if (_player.TryGetField<Player, GameObject>("m_visual", out var playerVisual))
            {
                var playerModel = _player.GetField<Player, Animator>("m_animator").gameObject;
                var playerHeight = Utils.GetModelHeight(playerModel);
                var playerWidth = Utils.GetModelWidth(playerModel);

                var vrmHeight = Utils.GetModelHeight(_vrmGo);
                var vrmWidth = Utils.GetModelWidth(_vrmGo);


                if (vrmWidth <= 0 || float.IsNaN(vrmWidth))
                    throw new InvalidOperationException("Cannot measure avatar shoulder width.");
                _settings.VrmHeight = vrmHeight;

                _settings.VrmRadius = vrmWidth * 0.55f; // Half shoulder width plus 10% clearance.

                _settings.PlayerVrmScale = vrmHeight / playerHeight;
                _settings.PlayerVrmWidthScale = vrmWidth / playerWidth;
                Logger.Log(_playerName + ": avatar is " + vrmHeight.ToString("F2") + " m tall, " +
                    vrmWidth.ToString("F2") + " m across the shoulders, " +
                    _settings.PlayerVrmScale.ToString("F2") + "x the player");
                if (_settings.PlayerVrmScale > 3f || _settings.PlayerVrmScale < 0.33f)
                    Logger.LogWarning(_playerName +
                        ": avatar size is way off, a node in the model is probably scaled. " +
                        "weapons and the collider follow this number");

                // ModelScale is already in these
                SeatProportions = Vector3.zero;
                var vanillaModel = _player.GetField<Player, Animator>("m_animator").gameObject;
                var hasVanillaSupport = Utils.TryGetSeatSupport(vanillaModel, out var vanillaSupport);
                var hasAvatarSupport = Utils.TryGetSeatSupport(_vrmGo, out var avatarSupport);
                if (hasVanillaSupport && hasAvatarSupport)
                {
                    SeatProportions = AttachmentTransforms.Vector(AttachmentMath.GetSeatProportions(
                        AttachmentTransforms.Vector(vanillaSupport),
                        AttachmentTransforms.Vector(avatarSupport)));
                }

                if (Settings.LogLoadTiming)
                {
                    Logger.Log("Seat measurements (ground-to-hip, hip-to-thigh underside, lower leg thickness): game=" +
                        (hasVanillaSupport ? vanillaSupport.ToString("F3") : "unavailable") + "; VRM=" +
                        (hasAvatarSupport ? avatarSupport.ToString("F3") : "unavailable"));
                }
            }

            _state = State.Staged;
        }

        internal static IEnumerator PrepareCachedMaterials(GameObject root, VrmSettings settings)
        {
            var lifetime = root.GetComponent<SharedVrmLifetime>();
            var materials = new List<Material>();

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var mat in smr.sharedMaterials)
                    if (!materials.Contains(mat))
                        materials.Add(mat);
            }

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (var mat in mr.sharedMaterials)
                    if (!materials.Contains(mat))
                        materials.Add(mat);
            }

            // Custom/Creature is the game's lit skinned shader with an emission input; it renders
            // in the deferred path like the player shader, so SSAO gets real normals. The player
            // shader stays as the fallback (no glow) if the creature shader is not loaded yet.
            var creatureShader = settings.UsesCreatureShader ? Shader.Find("Custom/Creature") : null;
            var foundShader = creatureShader != null ? creatureShader : Shader.Find("Custom/Player");

            foreach (var mat in materials)
            {
                if (lifetime == null) yield break;
                if (mat == null) continue;
                if (settings.UseMToonShader && !settings.AttemptTextureFix && mat.HasProperty("_Color"))
                {
                    var color = mat.GetColor("_Color");
                    color.r *= settings.ModelBrightness;
                    color.g *= settings.ModelBrightness;
                    color.b *= settings.ModelBrightness;
                    mat.SetColor("_Color", color);
                }
                else if (settings.AttemptTextureFix)
                {
                    if (mat.shader != foundShader)
                    {
                        var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

                        var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") as Texture2D : null;
                        var tex = mainTex;

                        if (mainTex != null)
                        {
                            tex = new Texture2D(mainTex.width, mainTex.height);
                            lifetime.ExtraResources.Add(tex);
                            // Bound native copies, but only yield when the frame's work
                            // budget expires; one frame per strip adds seconds per texture.
                            int width = mainTex.width, height = mainTex.height;
                            var rows = Math.Max(1, 65536 / width);
                            var stripes = new List<Color[]>();
                            var slice = System.Diagnostics.Stopwatch.StartNew();
                            var sliceBudgetMs = PersistentImportAwaitCaller.SpareFrameMs();
                            for (var y = 0; y < height; y += rows)
                            {
                                stripes.Add(mainTex.GetPixels(0, y, width, Math.Min(rows, height - y)));
                                if (slice.Elapsed.TotalMilliseconds >= sliceBudgetMs)
                                {
                                    yield return null;
                                    slice.Restart();
                                    sliceBudgetMs = PersistentImportAwaitCaller.SpareFrameMs();
                                }
                            }

                            var pixelsTask = Task.Run(() =>
                            {
                                foreach (var pixels in stripes)
                                {
                                    for (var i = 0; i < pixels.Length; i++)
                                    {
                                        var col = pixels[i] * color;
                                        Color.RGBToHSV(col, out var h, out var s, out var v);
                                        v *= settings.ModelBrightness;
                                        pixels[i] = Color.HSVToRGB(h, s, v, true);
                                        pixels[i].a = col.a;
                                    }
                                }
                            });

                            while (!pixelsTask.IsCompleted) yield return new WaitUntil(() => pixelsTask.IsCompleted);

                            pixelsTask.GetAwaiter().GetResult(); // Already completed above.

                            if (lifetime == null || tex == null || mat == null) yield break;
                            slice.Restart();
                            sliceBudgetMs = PersistentImportAwaitCaller.SpareFrameMs();
                            for (int y = 0, stripe = 0; y < height; y += rows, stripe++)
                            {
                                tex.SetPixels(0, y, width, Math.Min(rows, height - y), stripes[stripe]);
                                if (slice.Elapsed.TotalMilliseconds >= sliceBudgetMs)
                                {
                                    yield return null;
                                    slice.Restart();
                                    sliceBudgetMs = PersistentImportAwaitCaller.SpareFrameMs();
                                }
                            }

                            tex.Apply();
                        }

                        var bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                        mat.shader = foundShader;

                        if (foundShader == creatureShader)
                        {
                            mat.SetTexture("_MainTex", tex);
                            mat.SetColor("_Color", color);
                            mat.SetTexture("_BumpMap", bumpMap);
                            mat.SetFloat("_Glossiness", 0.2f);
                            mat.SetFloat("_Metallic", 0f);
                            mat.SetFloat("_MetalGloss", 0f);
                            // The avatar's own colours glow at the configured strength.
                            mat.SetTexture("_EmissionMap", tex);
                            mat.SetColor("_EmissionColor", Color.white * settings.TextureFixEmission);
                        }
                        else
                        {
                            mat.SetTexture("_MainTex", tex);
                            mat.SetTexture("_SkinBumpMap", bumpMap);
                            mat.SetColor("_SkinColor", color);
                            mat.SetTexture("_ChestTex", tex);
                            mat.SetTexture("_ChestBumpMap", bumpMap);
                            mat.SetTexture("_LegsTex", tex);
                            mat.SetTexture("_LegsBumpMap", bumpMap);
                            mat.SetFloat("_Glossiness", 0.2f);
                            mat.SetFloat("_MetalGlossiness", 0.0f);
                        }
                    }
                }

                lifetime.NotifyMaterial(mat);
                yield return null;
            }
        }
    }
}
