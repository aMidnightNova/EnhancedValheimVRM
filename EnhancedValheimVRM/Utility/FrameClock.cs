using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace EnhancedValheimVRM
{
    // stamps three points of every frame from inside unitys player loop so we can tell how much of
    // the frame the game itself used. the fps limit wait sits at the top of the frame before
    // EarlyUpdate, and the vsync wait is in PresentAfterDraw, so neither is counted as work.
    internal static class FrameClock
    {
        private static bool _installed;
        private static int _startFrame = -1;
        private static double _start, _updateEnd, _tailMs, _deadlineMs, _overshootMs;

        internal static bool Installed => _installed;

        // longest frame since ResetWorst, for the timing log
        internal static double WorstFrameMs { get; private set; }

        // what the import was doing this frame, named in the long frame report
        internal static string Label
        {
            set
            {
                if (_notes.Count == 6) _notes.RemoveAt(0);
                _notes.Add("+" + ((Time.realtimeSinceStartupAsDouble - _start) * 1000.0).ToString("F0") + "ms " +
                    value);
            }
        }

        private static readonly List<string> _notes = new List<string>();
        private static double _importMs;

        // main thread time the import itself spent this frame.
        internal static void Charge(double ms)
        {
            _importMs += ms;
        }

        // while set, every frame over this many ms is logged with the notes. 0 is off
        internal static double ReportAboveMs;

        internal static bool Reporting => ReportAboveMs > 0;

        internal static void ResetWorst()
        {
            WorstFrameMs = 0;
        }

        internal static void Install()
        {
            if (_installed) return;
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                if (!AddFirst(ref loop, typeof(EarlyUpdate), MarkStart) ||
                    !AddFirst(ref loop, typeof(PreLateUpdate), MarkUpdateEnd) ||
                    !AddBefore(ref loop, typeof(PostLateUpdate), typeof(PostLateUpdate.PresentAfterDraw), MarkEnd))
                {
                    Logger.LogWarning("Could not hook the player loop, avatar loads use the fixed slice.");
                    return;
                }

                PlayerLoop.SetPlayerLoop(loop);
                _installed = true;
                if (Settings.LogLoadTiming) Logger.Log("Frame clock hooked into the player loop.");
            }
            catch (Exception error)
            {
                Logger.LogWarning("Could not hook the player loop, avatar loads use the fixed slice: " + error.Message);
            }
        }

        // ms still free in this frame once the game has had its turn. frameMs is the frame the
        // game is aiming for. tail is what LateUpdate and rendering took last frame.
        internal static float SpareMs(double frameMs)
        {
            if (!_installed || _startFrame != Time.frameCount) return -1;
            _deadlineMs = frameMs;
            var usedMs = (Time.realtimeSinceStartupAsDouble - _start) * 1000.0;
            return Mathf.Max(2f, (float)(frameMs - usedMs - _tailMs - _overshootMs - 1.0));
        }

        private static void MarkStart()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            // the fps wait sits before this stamp, so start to start is the real frame period.
            // whatever ran past the deadline last frame comes off the next slice, and fades
            // once frames land inside it again.
            if (_startFrame == Time.frameCount - 1)
            {
                var periodMs = (now - _start) * 1000.0;
                WorstFrameMs = Math.Max(WorstFrameMs, periodMs);
                if (ReportAboveMs > 0 && periodMs > ReportAboveMs)
                {
                    Logger.Log("Long frame " + periodMs.ToString("F0") + "ms: update part " +
                        ((_updateEnd - _start) * 1000.0).ToString("F0") + "ms, render part " +
                        ((now - _updateEnd) * 1000.0).ToString("F0") + "ms, import used " + _importMs.ToString("F1") +
                        "ms; " + string.Join(", ", _notes));
                }
            }

            _notes.Clear();
            _importMs = 0;

            if (_startFrame == Time.frameCount - 1 && _deadlineMs > 0)
            {
                var over = (now - _start) * 1000.0 - _deadlineMs;
                _overshootMs = over > 0 ? Math.Max(over, _overshootMs) : _overshootMs * 0.5;
            }

            _startFrame = Time.frameCount;
            _start = now;
        }

        private static void MarkUpdateEnd()
        {
            _updateEnd = Time.realtimeSinceStartupAsDouble;
        }

        private static void MarkEnd()
        {
            var tail = (Time.realtimeSinceStartupAsDouble - _updateEnd) * 1000.0;
            // lean on the slow side so one light frame doesnt hand out a slice the next one cant afford
            _tailMs = Math.Max(tail, _tailMs * 0.9);
        }

        private static bool AddFirst(ref PlayerLoopSystem loop, Type phase, PlayerLoopSystem.UpdateFunction fn)
        {
            return Edit(ref loop, phase, list => list.Insert(0, Stamp(fn)));
        }

        internal static bool AddBefore(ref PlayerLoopSystem loop,
            Type phase,
            Type before,
            PlayerLoopSystem.UpdateFunction fn,
            Type owner = null)
        {
            return Edit(ref loop,
                phase,
                list =>
                {
                    var at = list.FindIndex(s => s.type == before);
                    list.Insert(at < 0 ? list.Count : at, Stamp(fn, owner));
                });
        }

        private static PlayerLoopSystem Stamp(PlayerLoopSystem.UpdateFunction fn, Type owner = null)
        {
            return new PlayerLoopSystem { type = owner ?? typeof(FrameClock), updateDelegate = fn };
        }

        private static bool Edit(ref PlayerLoopSystem loop, Type phase, Action<List<PlayerLoopSystem>> edit)
        {
            if (loop.subSystemList == null) return false;
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != phase) continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList ??
                    Array.Empty<PlayerLoopSystem>());
                edit(list);
                loop.subSystemList[i].subSystemList = list.ToArray();
                return true;
            }

            return false;
        }
    }
}
