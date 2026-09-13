// Exercises the production cache coroutine with a controlled importer/native-object
// boundary. These checks do not replace an in-game Unity rendering/performance pass.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static void Until(Func<bool> done)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            CoroutineHelper.Instance.Tick();
            if (DateTime.UtcNow > timeout) throw new Exception("Cache coroutine did not finish");
            Thread.Sleep(1); // Test pump only; never used by production loading.
        }
    }

    private static VrmAssetCache.Source Source(string key, byte first = 1)
    {
        return new VrmAssetCache.Source
        {
            Key = key, Path = key + ".vrm", Bytes = new byte[] { first, 2, 3 }, Settings = new VrmSettings("test")
        };
    }

    private static void Main()
    {
        UniGLTF.GlbBinaryParser.CallerThread = Environment.CurrentManagedThreadId;
        var first = VrmAssetCache.Get(Source("menu"));
        var duplicateBytes = Source("menu");
        Check(ReferenceEquals(first, VrmAssetCache.Get(duplicateBytes)),
            "Concurrent requests must share the same pending import");
        Until(() => UniGLTF.ImporterContext.Pending.Count == 1);
        Check(!first.Completed, "An unfinished native import must yield back to the caller");
        Check(UniGLTF.GlbBinaryParser.AllOffThread, "GLB parsing must never run on the caller/main thread");
        var second = VrmAssetCache.Get(Source("remote"));
        for (var i = 0; i < 20; i++)
        {
            CoroutineHelper.Instance.Tick();
            Thread.Sleep(1);
        }

        Check(UniGLTF.ImporterContext.Pending.Count == 2, "Independent imports must be allowed to interleave");
        UniGLTF.ImporterContext.FinishOne();
        Until(() => first.Completed);
        Check(first.Error == null && first.Root != null, "First import must publish its template");
        Check(!first.Root.active && first.Root.persistent, "Cached template must stay hidden across scenes");
        Until(() => UniGLTF.ImporterContext.Pending.Count == 1);
        UniGLTF.ImporterContext.FinishOne();
        Until(() => second.Completed);
        Check(!ReferenceEquals(first.Root, second.Root), "Different avatar sources must have independent templates");
        var respawn = VrmAssetCache.Get(Source("menu"));
        Check(ReferenceEquals(respawn.Root, first.Root) && respawn.Completed,
            "Menu/world/respawn requests must reuse the completed import");
        Check(UniGLTF.ImporterContext.Starts == 2, "Warm character switches must not invoke the importer again");
        Check(first.Root.GetComponent<SharedVrmLifetime>().ExtraResources.Count == 1,
            "Persistent cache must own native resources");
        Check(!first.Root.destroyed, "Cache hits must not dispose the retained template");
        var failure = VrmAssetCache.Get(Source("broken", 0));
        Until(() => failure.Completed);
        Check(failure.Error != null, "Failed worker parsing must complete the request with an error");
        var retry = VrmAssetCache.Get(Source("broken"));
        Check(!ReferenceEquals(retry, failure), "Failures must be evicted so corrected avatars can retry");
        Until(() => UniGLTF.ImporterContext.Pending.Count == 1);
        UniGLTF.ImporterContext.FinishOne();
        Until(() => retry.Completed);
        Check(retry.Error == null, "Import queue must remain usable after a failure");
        var nativeFailure = VrmAssetCache.Get(Source("native-failure"));
        Until(() => UniGLTF.ImporterContext.Pending.Count == 1);
        UniGLTF.ImporterContext.FailOne();
        Until(() => nativeFailure.Completed);
        Check(nativeFailure.Error != null, "Native async failure must release the queue and complete the request");
        var afterFailure = VrmAssetCache.Get(Source("after-failure"));
        Until(() => UniGLTF.ImporterContext.Pending.Count == 1);
        UniGLTF.ImporterContext.FinishOne();
        Until(() => afterFailure.Completed);
        Check(afterFailure.Error == null, "A failed native import must not block unrelated players");
        Check(VrmInstance.Prepared == 4,
            "Materials must be processed once per successful cold import, never per cache hit");
        var directory = Path.Combine(Path.GetTempPath(), "evrm-source-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            Constants.Vrm.Dir = directory;
            Constants.Vrm.DefaultPath = Path.Combine(directory, Constants.Vrm.DefaultName);
            File.WriteAllBytes(Constants.Vrm.DefaultPath, new byte[] { 1, 2, 3 });
            var fallback = Task.Run(() => VrmAssetCache.ReadSource("missing", true, null)).GetAwaiter().GetResult();
            Check(fallback.Settings.Name == "___Default", "Default settings name must not include the .vrm suffix");
            var unchanged = Task.Run(() => VrmAssetCache.ReadSource("missing", true, null)).GetAwaiter().GetResult();
            Check(unchanged.Key == fallback.Key, "Re-entering the same character must preserve cache identity");
            File.WriteAllText(Path.Combine(directory, "outfits____default.txt"), "[New look]\nDefault=True");
            var outfitChange = Task.Run(() => VrmAssetCache.ReadSource("missing", true, null)).GetAwaiter().GetResult();
            Check(outfitChange.Key == fallback.Key && outfitChange.OutfitText.Contains("New look"),
                "Outfit changes must reload definitions without reimporting the model");
            File.WriteAllBytes(Constants.Vrm.DefaultPath, new byte[] { 1, 2, 3, 4 });
            var changed = Task.Run(() => VrmAssetCache.ReadSource("missing", true, null)).GetAwaiter().GetResult();
            Check(changed.Key != fallback.Key, "Replacing a VRM must invalidate its previous source version");
            // Exercise the actual synchronous menu entry point, then retain a live
            // clone/corpse lease while replacing the same source's material revision.
            var menu = VrmAssetCache.GetForMenu(changed);
            Until(() => menu.Completed);
            Check(menu.Imported && menu.Root.persistent && !menu.Root.active,
                "Synchronous menu import did not finalize a hidden persistent template");
            Check(ReferenceEquals(menu, VrmAssetCache.GetForMenu(changed)), "Menu cache hit reimported the avatar");
            VrmAssetCache.Retain(menu);
            var revision = VrmAssetCache.ReadSource("missing", true, null);
            revision.Key += ":brightness-edit";
            var next = VrmAssetCache.GetForMenu(revision);
            Until(() => next.Completed);
            Check(!menu.Root.destroyed, "A live player/corpse lost its superseded template");
            VrmAssetCache.Release(menu);
            Check(menu.Root.destroyed && !next.Root.destroyed,
                "Superseded unused template was retained or latest cache was evicted");
            var otherPath = Source("other-character");
            var other = VrmAssetCache.Get(otherPath);
            Until(() => UniGLTF.ImporterContext.Pending.Count == 1);
            UniGLTF.ImporterContext.FinishOne();
            Until(() => other.Completed);
            Check(!next.Root.destroyed, "Another character's load evicted the latest cached avatar");
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Console.WriteLine(checks + " cache scheduling/lifetime checks passed.");
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool destroyed, persistent;

        public static void Destroy(Object obj)
        {
            obj.destroyed = true;
        }

        public static void DontDestroyOnLoad(Object obj)
        {
            obj.persistent = true;
        }
    }

    public class Transform : Object
    {
        public Transform parent;
        public GameObject gameObject;
    }

    public class GameObject : Object
    {
        private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
        public bool active = true;

        public GameObject()
        {
            components[typeof(Animator)] = new Animator();
        }

        public T AddComponent<T>() where T : new()
        {
            var result = new T();
            components[typeof(T)] = result;
            return result;
        }

        public T GetComponent<T>() where T : class
        {
            return components.TryGetValue(typeof(T), out var value) ? value as T : null;
        }

        public void SetActive(bool value)
        {
            active = value;
        }
    }

    public class Animator
    {
        public Avatar avatar = new Avatar();
    }

    public class Avatar
    {
        public bool isValid = true, isHuman = true;
    }

    public class Renderer
    {
        public bool enabled;
    }
}

namespace UniGLTF
{
    public class GlbBinaryParser
    {
        private readonly byte[] bytes;
        public static int CallerThread;
        public static bool AllOffThread = true;

        public GlbBinaryParser(byte[] value, string path)
        {
            bytes = value;
        }

        public object Parse()
        {
            AllOffThread &= Environment.CurrentManagedThreadId != CallerThread;
            if (bytes[0] == 0) throw new InvalidDataException("Test parse failure");
            return new object();
        }
    }

    public class RuntimeGltfInstance
    {
        public UnityEngine.GameObject Root;
        public List<UnityEngine.Renderer> VisibleRenderers = new List<Renderer>();

        public void TransferOwnership(Action<object, UnityEngine.Object> take)
        {
            take(null, new UnityEngine.Object());
        }
    }

    public class ImporterContext : IDisposable
    {
        protected UnityEngine.GameObject Root;
        public readonly List<UnityEngine.Transform> Nodes = new List<Transform>();

        public static readonly Queue<TaskCompletionSource<RuntimeGltfInstance>> Pending =
            new Queue<TaskCompletionSource<RuntimeGltfInstance>>();

        public static int Starts;

        public RuntimeGltfInstance Load()
        {
            return new RuntimeGltfInstance { Root = new UnityEngine.GameObject() };
        }

        public Task<RuntimeGltfInstance> LoadAsync(object caller)
        {
            Starts++;
            var task = new TaskCompletionSource<RuntimeGltfInstance>();
            Pending.Enqueue(task);
            return task.Task;
        }

        public static void FinishOne()
        {
            Pending.Dequeue().SetResult(new RuntimeGltfInstance { Root = new UnityEngine.GameObject() });
        }

        public static void FailOne()
        {
            Pending.Dequeue().SetException(new InvalidOperationException("Test native failure"));
        }

        public void Dispose() { }
    }
}

namespace VRM
{
    public class NotVrm0Exception : Exception { }

    public class VRMData
    {
        public VRMData(object data) { }
    }

    public class VRMImporterContext : UniGLTF.ImporterContext
    {
        public VRMImporterContext(VRMData data, object map = null, object texture = null) { }
    }
}

namespace UniVRM10
{
    public class Vrm10Data
    {
        public static Vrm10Data Parse(object data)
        {
            return new Vrm10Data();
        }
    }

    public class Vrm10Importer : UniGLTF.ImporterContext
    {
        public Vrm10Importer(Vrm10Data data, object map = null, object texture = null) { }
    }
}

namespace VRMShaders { }

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static System.Reflection.FieldInfo Field(Type type, string name)
        {
            return type.GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        }
    }
}

namespace EnhancedValheimVRM
{
    public class VrmSettings
    {
        public float ModelBrightness = .8f, TextureFixEmission = 0.15f;
        public bool UseMToonShader, AttemptTextureFix;
        public bool UsesCreatureShader => false;
        public string Name;

        public VrmSettings(string name)
        {
            Name = name;
        }

        public VrmSettings(string name, string settings) { }
    }

    public class OutfitConfig
    {
        public static OutfitConfig Parse(string text)
        {
            return new OutfitConfig();
        }
    }

    public static class Constants
    {
        public static class Vrm
        {
            public static string Dir = "/tmp", DefaultPath = "/tmp/___Default.vrm", DefaultName = "___Default.vrm";
        }
    }

    public static class Logger
    {
        public enum LogLevel
        {
            Debug
        }

        public static void Log(string value, LogLevel level = LogLevel.Debug) { }

        public static void LogWarning(string value) { }
    }

    public class SharedVrmLifetime
    {
        public void NotifyMaterial(object material) { }

        public readonly List<UnityEngine.Object> ExtraResources = new List<Object>();
    }

    public static class Settings
    {
        public static bool LogLoadTiming = false;
    }

    public class TextureDeserializerAsync { }

    public class PersistentImportAwaitCaller
    {
        public PersistentImportAwaitCaller(object importer) { }

        public void Protect() { }

        public static UnityEngine.GameObject GetRoot(UniGLTF.ImporterContext importer)
        {
            return importer == null
                ? null
                : typeof(UniGLTF.ImporterContext).GetField("Root",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(importer) as UnityEngine.GameObject;
        }
    }

    public static class PatchShaderFind
    {
        public static void EnsureLoadedForMenu() { }

        public static IEnumerator EnsureLoaded()
        {
            yield return null;
        }
    }

    public static class VrmInstance
    {
        public static int Prepared;

        public static IEnumerator PrepareCachedMaterials(object root, object settings)
        {
            Prepared++;
            yield return null;
        }
    }

    public class CoroutineHelper
    {
        public static readonly CoroutineHelper Instance = new CoroutineHelper();
        private readonly List<Stack<IEnumerator>> routines = new List<Stack<IEnumerator>>();

        public void StartCoroutine(IEnumerator work)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(work);
            routines.Add(stack);
        }

        public void Tick()
        {
            foreach (var stack in routines.ToArray())
            {
                while (stack.Count > 0)
                {
                    var work = stack.Peek();
                    if (!work.MoveNext())
                    {
                        stack.Pop();
                        (work as IDisposable)?.Dispose();
                        continue;
                    }

                    if (work.Current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }

                    break;
                }

                if (stack.Count == 0) routines.Remove(stack);
            }
        }
    }
}

namespace EnhancedValheimVRM.Sharing
{
    public class AvatarBundle
    {
        internal string VerifiedVersion;
        public long CharacterId;
        public byte[] Vrm;
        public string Settings, Outfits;
    }

    public static class SharingWire
    {
        public const int MaxSettingsBytes = 128 * 1024;
    }
}
