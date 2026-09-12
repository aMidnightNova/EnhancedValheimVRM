using System;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Debug = UnityEngine.Debug;

namespace EnhancedValheimVRM
{
    public static class PatchAllUpdateMethods
    {
        private static Dictionary<string, List<long>> methodCallTimestamps = new Dictionary<string, List<long>>();
        
        public static void ApplyPatches(Harmony harmony)
        {
            CoroutineHelper.Instance.StartCoroutine(ApplyPatchesAsync(harmony));
        }

        private static IEnumerator ApplyPatchesAsync(Harmony harmony)
        {
            // Optional diagnostic discovery must not recursively read the install tree
            // while an avatar is loading. Harmony itself is applied one type per frame.
            var discovery = Task.Run(() =>
            {
                var files = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll", SearchOption.AllDirectories);
                var types = new List<Type>();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.IsDynamic || new[] { "UnityEngine", "System", "mscorlib", "netstandard", "Microsoft", "Editor", "LuxParticles", "DemoScript" }.Any(assembly.FullName.StartsWith)) continue;
                    try { if (IsAssemblyInDirectory(assembly, files)) types.AddRange(assembly.GetTypes()); }
                    catch (ReflectionTypeLoadException error) { types.AddRange(error.Types.Where(type => type != null)); }
                }
                return types;
            });
            while (!discovery.IsCompleted) yield return null;
            if (discovery.IsFaulted) { Logger.LogWarning("Profiler discovery failed: " + discovery.Exception.GetBaseException().Message); yield break; }
            foreach (var type in discovery.Result)
            {
                yield return null;
                try
                {
                    PatchMethod(harmony, type, "Update");
                    PatchMethod(harmony, type, "FixedUpdate");
                    PatchMethod(harmony, type, "LateUpdate");
                }
                catch (Exception error) { Logger.LogWarning("Cannot profile " + type.FullName + ": " + error.Message); }
            }
        }

        private static bool IsAssemblyInDirectory(Assembly assembly, string[] assemblyFiles)
        {
            string assemblyLocation = assembly.Location;
            return assemblyFiles.Any(file => file.Equals(assemblyLocation, StringComparison.OrdinalIgnoreCase));
        }

        private static void PatchMethod(Harmony harmony, Type type, string methodName)
        {
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                try
                {
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(typeof(PatchAllUpdateMethods), nameof(GenericPrefix)),
                        postfix: new HarmonyMethod(typeof(PatchAllUpdateMethods), nameof(GenericPostfix)));
                    //Logger.Log($"Patched {methodName} in {type.FullName}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to patch method {methodName} in {type.FullName}: {ex.Message}");
                }
            }
        }

        public class GenericPState
        {
            public Stopwatch Stopwatch { get; set; }
            public MethodBase CallingMethod { get; set; }
        }

        public static void GenericPrefix(out GenericPState __state)
        {
            var stackTrace = new StackTrace();
            var frame = stackTrace.GetFrame(1); 
            var method = frame.GetMethod();
            __state = new GenericPState
            {
                Stopwatch = new Stopwatch(),
                CallingMethod = method
            };
            __state.Stopwatch.Start();
            // Logger.Log($"Before {method.DeclaringType.FullName}.{method.Name}");
        }

        public static void GenericPostfix(GenericPState __state)
        {
            __state.Stopwatch.Stop();

            int elapsedMilliseconds = (int)__state.Stopwatch.Elapsed.TotalMilliseconds;
            string methodName = $"{__state.CallingMethod.DeclaringType.FullName}.{__state.CallingMethod.Name}";

            if (!methodCallTimestamps.ContainsKey(methodName))
            {
                methodCallTimestamps[methodName] = new List<long>();
            }

            long currentTimestamp = Stopwatch.GetTimestamp();
            methodCallTimestamps[methodName].Add(currentTimestamp);

            // Remove timestamps that are outside the time window
            methodCallTimestamps[methodName].RemoveAll(timestamp => (currentTimestamp - timestamp) / (Stopwatch.Frequency / 1000) > Settings.TimeWindowMs);

            if (methodCallTimestamps[methodName].Count > Settings.CallThreshold)
            {
                Logger.LogOnce("profiler-frequency:" + methodName, $"{methodName} called {methodCallTimestamps[methodName].Count} times in the last {Settings.TimeWindowMs} ms");
            }

            if (elapsedMilliseconds > Settings.ProfileLogThresholdMs)
            {
                Logger.LogOnce("profiler-duration:" + methodName, $"{methodName} | Runtime -> {elapsedMilliseconds} ms | Call Count -> {methodCallTimestamps[methodName].Count}");
            }
        }
    }
}
