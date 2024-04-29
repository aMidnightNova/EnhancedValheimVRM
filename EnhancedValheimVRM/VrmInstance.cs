using System;
using System.IO;
using System.Collections;
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

        private RuntimeGltfInstance _instance;
        private GameObject _go;
        private readonly string _playerName;
        private VrmSettings _settings;
        private readonly string _vrmPath;

        public VrmInstance(string playerName, bool sync = true)
        {
            _playerName = playerName;

            _vrmPath = Path.Combine(Constants.VrmDir, $"{_playerName}.vrm");

            _settings = new VrmSettings(_playerName);

            CalculateSettingsHash();

            Debug.Log("[EnhancedValheimVRM] loading vrm");

            if (sync)
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
            if (_go != null)
            {
                Object.Destroy(_go);
            }
        }

        public RuntimeGltfInstance GetGltfInstance()
        {
            return _instance;
        }

        public GameObject GetGameObject()
        {
            return _go;
        }

        public void ReloadSettings()
        {
            _settings.Reload();
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

                    Debug.Log("[EnhancedValheimVRM] VRM read successful");

                    _instance = loaded;
                    _go = _instance.Root;
                }
                else
                {
                    Debug.LogError("[EnhancedValheimVRM] loading vrm Failed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }

            CalculateSourceBytesHash();
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
                Debug.LogError($"[EnhancedValheimVRM] Error loading VRM: {bytesTask.Exception.Flatten().InnerException}");
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
                _go = _instance.Root;
                Debug.Log("[EnhancedValheimVRM] VRM read successful");
                CalculateSourceBytesHash();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error during VRM loading: " + ex);
            }
        }

        private void CalculateSourceBytesHash()
        {
            Task.Run(() =>
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    var hash = md5.ComputeHash(_sourceHash);
                    lock (this)
                    {
                        _sourceHash = hash;
                    }
                }
            });
        }


        private void CalculateSettingsHash()
        {
            Task.Run(() =>
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] bytes = System.Text.Encoding.ASCII.GetBytes(_settings.ToString());

                    lock (this)
                    {
                        _settingsHash = md5.ComputeHash(bytes);
                    }
                }
            });
        }
    }
}