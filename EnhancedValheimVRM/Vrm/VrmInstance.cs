using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VRM;
using VRMShaders;
using Object = UnityEngine.Object;

namespace EnhancedValheimVRM
{
    public class VrmInstance
    {
        private byte[] _sourceBytes;
        private byte[] _sourceHash;
        private byte[] _settingsHash;

        public bool SettingsHashIsDirty = false;
        public bool SourceHashIsDirty = false;

        private GameObject _instance;
        private GameObject _vrmGo;
        private readonly string _playerName;
        private readonly string _playerSettingsName;
        private readonly VrmSettings _settings;
        private readonly string _vrmPath;

        private Player _player;
        private  BoneTransformer _boneTransformer;
        
        
 
 
        
        public VrmInstance(Player player)
        {
            _player = player;

            _playerName = player.GetPlayerDisplayName();
            _playerSettingsName = player.GetPlayerDisplayName();

            _vrmPath = Path.Combine(Constants.Vrm.Dir, $"{_playerName}.vrm");

            if (!File.Exists(_vrmPath))
            {
                if (Settings.UseDefaultVrm && File.Exists(Constants.Vrm.DefaultPath))
                {
                    _vrmPath = Constants.Vrm.DefaultPath;
                    _playerSettingsName = Constants.Vrm.DefaultName;
                }
                else
                {
                    var exMsg = $"No Vrm file found for {_playerName}.vrm";

                    if (Settings.UseDefaultVrm && !File.Exists(Constants.Vrm.DefaultPath))
                    {
                        exMsg += $" and no Vrm file found for {Constants.Vrm.DefaultName}";
                    }

                    exMsg += ".";

                    throw new FileNotFoundException(exMsg);
                }
            }


            _settings = new VrmSettings(_playerName);

            CalculateSettingsHash();

            Logger.Log("loading vrm");


            if (IsLocalPlayer(player))
            {
                LoadVrm();
            }
            else
            {
                CoroutineHelper.Instance.StartCoroutine(LoadVrmAsync());
            }
        }

        public string GetSettingsFilePath()
        {
            return _settings.GetSettingsFilePath();
        }
        public string GetVrmFilePath()
        {
            return _vrmPath;
        }
        private static bool IsLocalPlayer(Player player)
        {
            if (Game.instance != null)
            {
                var localPlayerName = Game.instance.GetPlayerProfile().GetName();
                var playerName = player.GetPlayerDisplayName();
                return string.IsNullOrEmpty(playerName) || playerName == "..." || playerName == localPlayerName;
            }

            var index = FejdStartup.instance.GetField<FejdStartup, int>("m_profileIndex");
            var profiles = FejdStartup.instance.GetField<FejdStartup, List<PlayerProfile>>("m_profiles");
            return index >= 0 && index < profiles.Count;
        }

        public void SetPlayer(Player player)
        {
            _player = player;
            CreateVrmGo();
        }

        ~VrmInstance()
        {
            if (_vrmGo != null)
            {
                Object.Destroy(_vrmGo);
            }
        }


        public GameObject GetGameObject()
        {
            return _vrmGo;
        }

        public Animator GetAnimator()
        {
            return _vrmGo.GetComponentInChildren<Animator>();
        }

        public void ReloadSettings()
        {
            _settings.Reload();
        }

        public VrmSettings GetSettings()
        {
            return _settings;
        }

        private void LoadVrm()
        {
            byte[] bytes = File.ReadAllBytes(_vrmPath);
            _sourceBytes = Compression.CompressBytes(bytes);

            try
            {
                var data = new GlbBinaryParser(bytes, _vrmPath).Parse();
                bytes = null;

                var loaded = default(RuntimeGltfInstance);

                try
                {
                    var vrm = new VRMData(data);
                    var context = new VRMImporterContext(vrm);
                    try
                    {
                        loaded = context.Load();
                    }
                    catch (TypeLoadException ex)
                    {
                        Logger.LogError("Failed to load type: " + ex.TypeName);
                        Logger.LogError(ex);
                    }
                }
                catch (NotVrm0Exception)
                {
                    Logger.Log("Not Vrm0, Trying VRM10");
                    var vrm = Vrm10Data.Parse(data);
                    var context = new Vrm10Importer(vrm);
                    try
                    {
                        loaded = context.Load();
                    }
                    catch (TypeLoadException ex)
                    {
                        Logger.LogError("Failed to load type: " + ex.TypeName);
                        Logger.LogError(ex);
                    }
                }


                if (loaded != null)
                {
                    loaded.ShowMeshes();
                    loaded.Root.transform.localScale = Vector3.one * _settings.ModelScale;

                    Logger.Log("VRM read successful");

                    _instance = loaded.Root;
                }
                else
                {
                    Logger.LogError("loading vrm Failed.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }

            SetupVrm();
        }

        private IEnumerator LoadVrmAsync()
        {
            Task<byte[]> bytesTask = Task.Run(() => File.ReadAllBytes(_vrmPath));

            while (!bytesTask.IsCompleted)
            {
                yield return new WaitUntil(() => bytesTask.IsCompleted);
            }

            if (bytesTask.IsFaulted)
            {
                Logger.LogError($"Error loading VRM: {bytesTask.Exception.Flatten().InnerException}");
                yield break;
            }

            var compressTask = Compression.CompressBytesAsync(bytesTask.Result);
            while (!compressTask.IsCompleted)
            {
                yield return new WaitUntil(() => compressTask.IsCompleted);
            }

            _sourceBytes = compressTask.Result;


            var dataTask = Task.Run(() => new GlbBinaryParser(bytesTask.Result, _vrmPath).Parse());

            while (!dataTask.IsCompleted)
            {
                yield return new WaitUntil(() => dataTask.IsCompleted);
            }

            if (dataTask.IsFaulted)
            {
                Logger.LogError($"Error parsing GLB: {dataTask.Exception.Flatten().InnerException}");
                yield break;
            }


            bytesTask = null;

            yield return null;

            Task<RuntimeGltfInstance> loader = null;
            bool maybeVrm10 = false;

            Task<VRMData> vrm0Task = Task.Run(() => new VRMData(dataTask.Result));
            while (!vrm0Task.IsCompleted)
            {
                yield return new WaitUntil(() => vrm0Task.IsCompleted);
            }

            if (vrm0Task.IsFaulted)
            {
                if (vrm0Task.Exception.InnerException is NotVrm0Exception)
                {
                    maybeVrm10 = true;
                }
            }
            else
            {
                var context = new VRMImporterContext(vrm0Task.Result, null, new TextureDeserializerAsync());
                loader = context.LoadAsync(new RuntimeOnlyAwaitCaller(0.001f));
            }

            if (maybeVrm10)
            {
                Logger.Log("Not Vrm0, Trying VRM10");
                var vrmTask = Task.Run(() => Vrm10Data.Parse(dataTask.Result));
                while (!vrmTask.IsCompleted)
                {
                    yield return new WaitUntil(() => vrmTask.IsCompleted);
                }

                var context = new Vrm10Importer(vrmTask.Result, null, new TextureDeserializerAsync());
                loader = context.LoadAsync(new RuntimeOnlyAwaitCaller(0.001f));
            }

            if (loader == null)
            {
                Logger.LogError("Loader was not initialized.");
                yield break;
            }

            while (!loader.IsCompleted)
            {
                yield return new WaitUntil(() => loader.IsCompleted);
            }

            if (loader.IsFaulted)
            {
                Logger.LogError("Error during VRM loading: " + loader.Exception.Flatten());
                yield break;
            }

            var loaded = loader.Result;

            //this is what .LoadMeshes() does
            // we are just yielding between each mesh.
            foreach (Renderer visibleRenderer in loaded.VisibleRenderers)
            {
                visibleRenderer.enabled = true;
                yield return null;
            }

            loaded.Root.transform.localScale = Vector3.one * _settings.ModelScale;
            _instance = loaded.Root;

            SetupVrm();
        }


        private void CreateVrmGo()
        {
            _vrmGo = Object.Instantiate(_instance);
            _vrmGo.name = Constants.Vrm.GoName;

            var lodGroupPlayer = _player.GetComponentInChildren<LODGroup>();

            var lodGroup = _vrmGo.AddComponent<LODGroup>();
            if (_settings.EnablePlayerFade)
            {
                //TODO: determine if regular Renders need to be put into the lod group. and then any armors added 
                lodGroup.SetLODs(new LOD[]
                {
                    new LOD(0.1f, _vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>())
                });
            }

            lodGroup.RecalculateBounds();

            lodGroup.fadeMode = lodGroupPlayer.fadeMode;
            lodGroup.animateCrossFading = lodGroupPlayer.animateCrossFading;

            _vrmGo.SetActive(false);

            if (_player.TryGetField<Player, Animator>("m_animator", out var playerAnimator))
            {
                //BoneTransforms.CopyBoneTransforms(playerAnimator);
                //BoneTransforms.SetupBones(_player,_vrmGo);
            }
            
            _boneTransformer = new BoneTransformer(_player, _vrmGo);
            
            // var vrmBodySmr = _vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(smr => smr.name == "Body");
            //
            // if (vrmBodySmr != null)
            // {
            //     _boneTransformer.RenameVrmBones(vrmBodySmr.bones);
            // }

            //_boneTransformer.CopyBoneTransforms(_player, this);
            _boneTransformer.ResizePlayerAvatarToVrmSize(_player);
        }

        public BoneTransformer GetBoneTransformer()
        {
            return _boneTransformer;
        }
        

 


        private void SetupVrm()
        {
            CalculateSourceBytesHash();
            // Object.DontDestroyOnLoad(_instance.Root);
            Object.DontDestroyOnLoad(_instance);

            CreateVrmGo();

            


            if (_player.TryGetField<Player, GameObject>("m_visual", out var playerVisual))
            {
                var vrmAnimator = GetAnimator();
                float playerHeight = Utils.GetModelHeight(playerVisual);
                
                float vrmHeight =  Utils.GetModelHeight(_vrmGo);

                _settings.HeightOffsetY = playerHeight - vrmHeight;
                
                _settings.PlayerVrmScale = vrmHeight / playerHeight;
                Logger.Log($"vrmHeight {vrmHeight} -- playerHeight {playerHeight} -- _settings.PlayerVrmScale {_settings.PlayerVrmScale}");

            }


            Logger.Log($"_settings.HeightOffsetY -> {_settings.HeightOffsetY} _settings.PlayerVrmScale -> {_settings.PlayerVrmScale}");
            CoroutineHelper.Instance.StartCoroutine(ProcessMaterialsCoroutine());
        }


        private IEnumerator ProcessMaterialsCoroutine()
        {
            var materials = new List<Material>();

            foreach (var smr in _instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                foreach (var mat in smr.materials)
                {
                    if (!materials.Contains(mat)) materials.Add(mat);
                }
            }

            foreach (var mr in _instance.GetComponentsInChildren<MeshRenderer>())
            {
                foreach (var mat in mr.materials)
                {
                    if (!materials.Contains(mat)) materials.Add(mat);
                }
            }

            
            
            
            
            Shader foundShader = Shader.Find("Custom/Player");

            foreach (var mat in materials)
            {
                if (_settings.UseMToonShader && !_settings.AttemptTextureFix && mat.HasProperty("_Color"))
                {
                    var color = mat.GetColor("_Color");
                    color.r *= _settings.ModelBrightness;
                    color.g *= _settings.ModelBrightness;
                    color.b *= _settings.ModelBrightness;
                    mat.SetColor("_Color", color);
                }
                else if (_settings.AttemptTextureFix)
                {
                    if (mat.shader != foundShader)
                    {
                        var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

                        var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") as Texture2D : null;
                        Texture2D tex = mainTex;

                        if (mainTex != null)
                        {
                            tex = new Texture2D(mainTex.width, mainTex.height);
                            var pixels = mainTex.GetPixels();

                            var pixelsTask = Task.Run(() =>
                            {
                                for (int i = 0; i < pixels.Length; i++)
                                {
                                    var col = pixels[i] * color;
                                    Color.RGBToHSV(col, out float h, out float s, out float v);
                                    v *= _settings.ModelBrightness;
                                    pixels[i] = Color.HSVToRGB(h, s, v, true);
                                    pixels[i].a = col.a;
                                }
                            });

                            while (!pixelsTask.IsCompleted)
                            {
                                yield return new WaitUntil(() => pixelsTask.IsCompleted);
                            }

                            pixelsTask.Wait();


                            tex.SetPixels(pixels);
                            tex.Apply();
                        }

                        var bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                        mat.shader = foundShader;

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

                yield return null;
            }

            Logger.Log("Material processing completed.");
        }

        private void CalculateSourceBytesHash()
        {
            SourceHashIsDirty = true;
            Task.Run(() =>
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    var hash = md5.ComputeHash(_sourceHash);
                    lock (this)
                    {
                        _sourceHash = hash;
                        SourceHashIsDirty = false;
                    }
                }
            });
        }


        private void CalculateSettingsHash()
        {
            Task.Run(() =>
            {
                SettingsHashIsDirty = true;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] bytes = System.Text.Encoding.ASCII.GetBytes(_settings.ToString());
                    byte[] hash = md5.ComputeHash(bytes);

                    lock (this)
                    {
                        _settingsHash = hash;
                        SettingsHashIsDirty = false;
                    }
                }
            });
        }
    }
}