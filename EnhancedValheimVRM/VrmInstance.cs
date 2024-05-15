using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;
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

        private RuntimeGltfInstance _instance;
        private GameObject _vrmGo;
        private readonly string _playerName;
        private readonly string _playerSettingsName;
        private readonly VrmSettings _settings;
        private readonly string _vrmPath;
        private readonly Player _player;

        public VrmInstance(Player player)
        {
            _player = player;

            _playerName = player.name;
            _playerSettingsName = player.name;

            _vrmPath = Path.Combine(Settings.Constants.VrmDir, $"{_playerName}.vrm");

            if (!File.Exists(_vrmPath))
            {
                if (Settings.UseDefaultVrm && File.Exists(Settings.Constants.DefaultVrmPath))
                {
                    _vrmPath = Settings.Constants.DefaultVrmPath;
                    _playerSettingsName = Settings.Constants.DefaultVrmName;
                }
                else
                {
                    var exMsg = $"No Vrm file found for {_playerName}.vrm";

                    if (Settings.UseDefaultVrm && !File.Exists(Settings.Constants.DefaultVrmPath))
                    {
                        exMsg += $" and no Vrm file found for {Settings.Constants.DefaultVrmName}";
                    }

                    exMsg += ".";

                    throw new FileNotFoundException(exMsg);
                }
            }


            _settings = new VrmSettings(_playerName);

            CalculateSettingsHash();

            Debug.Log("loading vrm");

            // this will determine if the player is local, basically loading at the start screen needs to be sync.
            if (Player.m_localPlayer.name == player.name)
            {
                LoadVrm();
            }
            else
            {
                CoroutineHelper.Instance.StartCoroutine(LoadVrmAsync());
            }
        }

        ~VrmInstance()
        {
            if (_vrmGo != null)
            {
                Object.Destroy(_vrmGo);
            }
        }

        public RuntimeGltfInstance GetGltfInstance()
        {
            return _instance;
        }

        public GameObject GetGameObject()
        {
            return _vrmGo;
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

                var vrm = new VRMData(data);
                var context = new VRMImporterContext(vrm);
                var loaded = default(RuntimeGltfInstance);

                try
                {
                    loaded = context.Load();
                }
                catch (TypeLoadException ex)
                {
                    Debug.LogError("Failed to load type: " + ex.TypeName);
                    Debug.LogError(ex);
                }

                if (loaded != null)
                {
                    loaded.ShowMeshes();
                    loaded.Root.transform.localScale = Vector3.one * _settings.ModelScale;

                    Debug.Log("VRM read successful");

                    _instance = loaded;
                }
                else
                {
                    Debug.LogError("loading vrm Failed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
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
                Debug.LogError($"Error loading VRM: {bytesTask.Exception.Flatten().InnerException}");
                yield break;
            }

            var compressTask = Compression.CompressBytesAsync(bytesTask.Result);
            while (!compressTask.IsCompleted)
            {
                yield return new WaitUntil(() => compressTask.IsCompleted);
            }

            _sourceBytes = compressTask.Result;


            var data = new GlbBinaryParser(bytesTask.Result, _vrmPath).Parse();
            bytesTask = null;


            yield return null;

            var vrm = new VRMData(data);

            yield return null;


            var context = new VRMImporterContext(vrm, null, new TextureDeserializerAsync());

            //var loader = context.LoadAsync(new RuntimeOnlyAwaitCaller(0.001f), name => new Timer(name));					
            var loader = context.LoadAsync(new RuntimeOnlyAwaitCaller(0.001f));
            while (!loader.IsCompleted)
            {
                yield return new WaitUntil(() => loader.IsCompleted);
            }

            try
            {
                var loaded = loader.Result;

                loaded.ShowMeshes();

                loaded.Root.transform.localScale = Vector3.one * _settings.ModelScale;
                _instance = loaded;

                SetupVrm();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error during VRM loading: " + ex);
            }
        }

        private void SetupVrm()
        {
            CalculateSourceBytesHash();

            _vrmGo = Object.Instantiate(_instance.Root);

            Object.DontDestroyOnLoad(_vrmGo);

            _vrmGo.name = Settings.Constants.VrmGoName;
            
            
            
           var lodGroupPlayer = _player.GetComponentInChildren<LODGroup>();
            
            var lodGroup = _vrmGo.AddComponent<LODGroup>();
            if (_settings.EnablePlayerFade)
            {
                lodGroup.SetLODs(new LOD[]
                {
                    new LOD(0.1f, _vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>())
                });
            }
            lodGroup.RecalculateBounds();

            lodGroup.fadeMode = lodGroupPlayer.fadeMode;
            lodGroup.animateCrossFading = lodGroupPlayer.animateCrossFading;

            _vrmGo.SetActive(false);


            CoroutineHelper.Instance.StartCoroutine(ProcessMaterialsCoroutine());

        }

        private IEnumerator ProcessMaterialsCoroutine()
        {
            
            
            var materials = new List<Material>();

            foreach (var smr in _vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                foreach (var mat in smr.materials)
                {
                    if (!materials.Contains(mat)) materials.Add(mat);
                }
            }

            foreach (var mr in _vrmGo.GetComponentsInChildren<MeshRenderer>())
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

            Debug.Log("[ValheimVRM] Material processing completed.");
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