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

        internal static GameObject GetRoot(ImporterContext importer)
        {
            return importer == null ? null : RootField.GetValue(importer) as GameObject;
        }

        private static int _frame = -1;
        private static float _start, _budgetMs;
        private readonly ImporterContext _importer;
        private readonly RuntimeOnlyAwaitCaller _inner = new RuntimeOnlyAwaitCaller(float.MaxValue);
        private int _protectedNodes, _frames, _inline, _deferred, _stepsInPhase;
        private double _lastCall, _longestStepMs;
        private string _longestStepWhere = "";

        // set by the import while a univrm phase is running, for naming the longest stretch
        internal string Phase
        {
            set
            {
                _phase = value;
                _stepsInPhase = 0;
            }
        }

        private string _phase = "";

        // every call into the caller ends a stretch of main thread work. the longest one is the spike.
        private void MarkStep()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (_lastCall > 0 && _lastFrame == Time.frameCount)
            {
                var stretch = (now - _lastCall) * 1000.0;
                if (stretch > _longestStepMs)
                {
                    _longestStepMs = stretch;
                    _longestStepWhere = _phase + " step " + _stepsInPhase;
                }
            }

            _stepsInPhase++;
            _lastCall = now;
            _lastFrame = Time.frameCount;
        }

        private int _lastFrame = -1;

        internal string LongestStep => _longestStepMs.ToString("F1") + "ms at " + _longestStepWhere;
        private float _budgetSumMs;
        private GameObject _protectedRoot;

        internal PersistentImportAwaitCaller(ImporterContext importer)
        {
            _importer = importer;
            BeginFrame();
        }

        // whats left of this frame for use.
        // uncapped has no idle time to find, so that keeps the old small slice.
        internal static float SpareFrameMs()
        {
            double frameMs;
            if (QualitySettings.vSyncCount > 0 && Screen.currentResolution.refreshRateRatio.value > 0)
                frameMs = 1000.0 * QualitySettings.vSyncCount / Screen.currentResolution.refreshRateRatio.value;
            else if (Application.targetFrameRate > 0)
                frameMs = 1000.0 / Application.targetFrameRate;
            else
                return Mathf.Clamp(16.7f - Time.deltaTime * 1000f, 2f, 8f);
            var spare = FrameClock.SpareMs(frameMs);
            return spare < 0 ? Mathf.Clamp(16.7f - Time.deltaTime * 1000f, 2f, 8f) : spare;
        }

        private static void BeginFrame()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _start = Time.realtimeSinceStartup;
            _budgetMs = VrmController.InitialLocalAvatarPending ? 50f : SpareFrameMs();
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

        // univrm calls NextFrame between nearly every step we only yield when the slice for this frame is spent.
        public Task NextFrame()
        {
            MarkStep();
            Protect();
            BeginFrame();
            if ((Time.realtimeSinceStartup - _start) * 1000f < _budgetMs) return Task.CompletedTask;
            _frames++;
            _budgetSumMs += _budgetMs;
            return _inner.NextFrame();
        }

        public Task NextFrameIfTimedOut()
        {
            return NextFrame();
        }

        // for the timing log: how many frames this import yielded and the average slice it was given
        internal string SliceStats =>
            _frames + " frames, avg slice " + (_frames > 0 ? _budgetSumMs / _frames : 0f).ToString("F1") + "ms (" +
            (FrameClock.Installed ? "frame clock" : "fixed") + "), worker jobs " + _inline + " inline / " + _deferred +
            " deferred";

        // a worker continuation only resumes once per frame, so every Run used to cost a frame.
        // now we wait for it inside the slice, only work that outlives the slice still costs a frame.
        public Task Run(Action action)
        {
            MarkStep();
            Protect();
            var task = Task.Run(action);
            return WaitWithinSlice(task) ? Task.CompletedTask : task;
        }

        public Task<T> Run<T>(Func<T> action)
        {
            MarkStep();
            Protect();
            var task = Task.Run(action);
            return WaitWithinSlice(task) ? Task.FromResult(task.Result) : task;
        }

        private bool WaitWithinSlice(Task task)
        {
            BeginFrame();
            // the importer uploads the result on the main thread right after, so leave room for that
            var remainingMs = (_budgetMs - (Time.realtimeSinceStartup - _start) * 1000f) * 0.6f;
            if (remainingMs <= 0) return task.IsCompleted;
            try
            {
                task.Wait((int)remainingMs);
            }
            catch (AggregateException)
            {
                // the awaiting code sees the fault through the task itself
            }

            if (task.IsCompleted)
                _inline++;
            else
                _deferred++;
            return task.IsCompleted && !task.IsFaulted;
        }
    }
}
