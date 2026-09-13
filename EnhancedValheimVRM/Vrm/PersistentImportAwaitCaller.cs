using System;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;
using VRMShaders;

namespace EnhancedValheimVRM
{
    // All imports share one frame window. A second avatar can make progress next
    // frame without waiting for another whole model, or multiplying the frame budget.
    internal sealed class PersistentImportAwaitCaller : IAwaitCaller
    {
        private static readonly System.Reflection.FieldInfo RootField =
            HarmonyLib.AccessTools.Field(typeof(ImporterContext), "Root");

        internal static GameObject GetRoot(ImporterContext importer) =>
            importer == null ? null : RootField.GetValue(importer) as GameObject;

        private static int _frame = -1;
        private static float _start, _budget;
        private readonly ImporterContext _importer;
        private readonly RuntimeOnlyAwaitCaller _inner = new RuntimeOnlyAwaitCaller(float.MaxValue);
        private int _protectedNodes;
        private GameObject _protectedRoot;

        internal PersistentImportAwaitCaller(ImporterContext importer)
        {
            _importer = importer;
            BeginFrame();
        }

        private static void BeginFrame()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _start = Time.realtimeSinceStartup;
            _budget = VrmController.InitialLocalAvatarPending
                ? 0.05f
                : Mathf.Clamp(0.0167f - Time.deltaTime, 0.002f, 0.008f);
        }

        internal void Protect()
        {
            for (; _protectedNodes < _importer.Nodes.Count; _protectedNodes++)
            {
                var node = _importer.Nodes[_protectedNodes];
                if (node != null && node.parent == null) UnityEngine.Object.DontDestroyOnLoad(node.gameObject);
            }

            var root = GetRoot(_importer);
            if (root == null || root == _protectedRoot) return;
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.SetActive(false);
            _protectedRoot = root;
        }

        public Task NextFrame()
        {
            Protect();
            return _inner.NextFrame();
        }

        public Task NextFrameIfTimedOut()
        {
            BeginFrame();
            return Time.realtimeSinceStartup - _start >= _budget ? NextFrame() : Task.CompletedTask;
        }

        public Task Run(Action action)
        {
            Protect();
            return _inner.Run(action);
        }

        public Task<T> Run<T>(Func<T> action)
        {
            Protect();
            return _inner.Run(action);
        }
    }
}
