using System.Collections.Generic;
using EnhancedValheimVRM.Sharing;
using System;
using UnityEngine;
using UniVRM10;
using VRM;

namespace EnhancedValheimVRM
{
    // puts a face on one avatar. the values move from the last frame to the newest over the time
    // between frames, the trackers for your own and the senders rate for a remote one, so a face
    // sits one frame behind and never steps. runs after the outfit controller so a live face wins
    // on shared shapes, and hands them back when the stream goes idle
    [DefaultExecutionOrder(10001)]
    public sealed class FaceApplier : MonoBehaviour
    {
        private sealed class Target
        {
            internal ExpressionKey? Expression;
            internal BlendShapeKey? Clip;
            internal SkinnedMeshRenderer Skin;
            internal int Index = -1;
        }

        private readonly Target[] _targets = new Target[FaceWire.ValueCount];
        private readonly float[] _wanted = new float[FaceWire.ValueCount];
        private readonly float[] _frameValues = new float[FaceWire.ValueCount];
        private readonly float[] _current = new float[FaceWire.ValueCount];
        private readonly byte[] _frame = new byte[FaceWire.ValueCount];
        private Vrm10Instance _vrm1;
        private VRMBlendShapeProxy _vrm0;
        private OutfitController _outfits;
        private long _id;
        private Player _player;
        private bool _local, _live, _mapped;
        private double _lastFrame = double.NegativeInfinity, _lastArrival;
        private float _packetSeconds = 1f / 30f;

        internal void Setup(Player player, VrmInstance instance)
        {
            _player = player;
            player.TryGetPlayerId(out _id);
            _local = player != null && player == Player.m_localPlayer;
            _vrm1 = GetComponent<Vrm10Instance>();
            _vrm0 = GetComponent<VRMBlendShapeProxy>();
            _outfits = GetComponent<OutfitController>();
            Map();
        }

        // catalogue index to something on this model: an expression or clip named after the
        // arkit shape or viseme, else the raw mesh blendshape by name, visemes also by their vrc.v_
        // name. presets are left alone on purpose
        private void Map()
        {
            var mappedCount = 0;
            // the model's spelling wins, trackers and files disagree on case (mouthPucker, MouthPucker)
            var expressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_vrm1 != null && _vrm1.Runtime != null)
            {
                foreach (var key in _vrm1.Runtime.Expression.ExpressionKeys)
                    if (key.Preset == ExpressionPreset.custom)
                        expressions[key.Name] = key.Name;
            }

            var clips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_vrm0 != null && _vrm0.BlendShapeAvatar != null)
            {
                foreach (var clip in _vrm0.BlendShapeAvatar.Clips)
                {
                    if (clip != null && clip.Preset == BlendShapePreset.Unknown)
                        clips[clip.BlendShapeName] = clip.BlendShapeName;
                }
            }

            var skins = new List<Tuple<SkinnedMeshRenderer, Dictionary<string, int>>>();
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var shapes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                    shapes[skin.sharedMesh.GetBlendShapeName(i)] = i;
                skins.Add(Tuple.Create(skin, shapes));
            }

            for (var i = 0; i < FaceWire.ValueCount; i++)
            {
                var alias = FaceWire.MeshAlias(i);
                Target target = null;
                foreach (var name in alias == null
                             ? new[] { FaceWire.Catalogue[i] }
                             : new[] { FaceWire.Catalogue[i], alias })
                {
                    if (_vrm1 != null)
                    {
                        if (expressions.TryGetValue(name, out var expression))
                            target = new Target { Expression = ExpressionKey.CreateCustom(expression) };
                    }
                    else if (_vrm0 != null)
                    {
                        if (clips.TryGetValue(name, out var clip))
                            target = new Target { Clip = BlendShapeKey.CreateUnknown(clip) };
                    }

                    if (target == null)
                    {
                        foreach (var skin in skins)
                        {
                            if (!skin.Item2.TryGetValue(name, out var index)) continue;
                            target = new Target { Skin = skin.Item1, Index = index };
                            break;
                        }
                    }

                    if (target != null) break;
                }

                _targets[i] = target;
                if (target != null) mappedCount++;
            }

            _mapped = mappedCount > 0;
            if (Settings.LogLoadTiming)
                Logger.Log(name + ": face stream maps " + mappedCount + " of " + FaceWire.ValueCount + " blendshapes");
        }

        private long RemoteId()
        {
            if (_id == 0) _player.TryGetPlayerId(out _id);
            return _id;
        }

        private void LateUpdate()
        {
            if (!_mapped) return;
            var now = Time.realtimeSinceStartupAsDouble;
            var fresh = false;
            if (_local)
            {
                if (Settings.FaceEnabled && VmcReceiver.Live &&
                    VmcReceiver.TryCopy(_frameValues, out var arrivedFrame, out var frameSeconds))
                {
                    fresh = true;
                    if (arrivedFrame != _lastArrival)
                    {
                        _lastArrival = arrivedFrame;
                        _packetSeconds = frameSeconds;
                        for (var i = 0; i < _wanted.Length; i++) _wanted[i] = _frameValues[i];
                    }

                    Smooth(_packetSeconds);
                }
            }
            else if (Settings.ReceiveFaceStreams && RemoteId() != 0 &&
                     FaceStreamClient.TryGetFrame(_id, _frame, out var arrived, out var rate))
            {
                fresh = true;
                Logger.LogOnce("face-showing-" + _id,
                    name + ": showing their face from the stream.",
                    Logger.LogLevel.Debug);
                if (arrived != _lastArrival)
                {
                    // the sender says how often it sends, that is how long the move to the new values gets
                    _packetSeconds = 1f / Mathf.Clamp(rate, 15, 60);
                    _lastArrival = arrived;
                    for (var i = 0; i < _wanted.Length; i++) _wanted[i] = _frame[i] / 255f;
                }

                Smooth(_packetSeconds);
            }

            if (fresh)
            {
                _lastFrame = now;
                _live = true;
                Apply();
                return;
            }

            if (!_live) return;
            // nothing for two seconds: relax to nothing over half a second, then let go
            if (now - _lastFrame < 2)
            {
                Smooth(_packetSeconds);
                Apply();
                return;
            }

            for (var i = 0; i < _wanted.Length; i++) _wanted[i] = 0;
            Smooth(0.15f);
            Apply();
            var settled = true;
            for (var i = 0; i < _current.Length; i++) settled &= _current[i] < 0.002f;
            if (!settled) return;
            for (var i = 0; i < _current.Length; i++) _current[i] = 0;
            Apply();
            Release();
            _live = false;
        }

        private static readonly int BlinkLeft = FaceWire.IndexOf("eyeBlinkLeft"),
            BlinkRight = FaceWire.IndexOf("eyeBlinkRight"),
            TongueOut = FaceWire.IndexOf("tongueOut");

        // straight line to the target, arriving after `seconds` whatever the frame rate. blinks and
        // the tongue take their target at once, the sender already timed those and a bridge would
        // only blur a blink
        private void Smooth(float seconds)
        {
            var step = seconds <= 0 ? 1f : Mathf.Min(1f, Time.deltaTime / seconds);
            for (var i = 0; i < _current.Length; i++)
            {
                var snap = i == BlinkLeft || i == BlinkRight || i == TongueOut;
                _current[i] += (_wanted[i] - _current[i]) * (snap ? 1f : step);
            }
        }

        private readonly Dictionary<ExpressionKey, float> _expressionWeights = new Dictionary<ExpressionKey, float>();
        private readonly Dictionary<BlendShapeKey, float> _clipWeights = new Dictionary<BlendShapeKey, float>();

        private void Apply()
        {
            _expressionWeights.Clear();
            _clipWeights.Clear();
            for (var i = 0; i < _targets.Length; i++)
            {
                var target = _targets[i];
                if (target == null) continue;
                var weight = _current[i];
                if (target.Expression.HasValue)
                    _expressionWeights[target.Expression.Value] = weight;
                else if (target.Clip.HasValue)
                    _clipWeights[target.Clip.Value] = weight;
                else if (target.Skin != null) target.Skin.SetBlendShapeWeight(target.Index, weight * 100f);
            }

            if (_vrm1 != null)
            {
                foreach (var pair in _expressionWeights) _vrm1.Runtime.Expression.SetWeight(pair.Key, pair.Value);
                _vrm1.Runtime.Process();
            }
            else if (_vrm0 != null)
            {
                foreach (var pair in _clipWeights) _vrm0.ImmediatelySetValue(pair.Key, pair.Value);
                _vrm0.Apply();
            }
        }

        // Restore imported mesh weights; the outfit controller reapplies its overrides.
        private void Release()
        {
            foreach (var target in _targets)
            {
                if (target?.Skin == null) continue;
                target.Skin.SetBlendShapeWeight(target.Index,
                    _outfits != null ? _outfits.OriginalWeight(target.Skin, target.Index) : 0f);
            }
        }
    }
}
