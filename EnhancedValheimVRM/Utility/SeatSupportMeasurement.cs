using System.Collections.Generic;
using Unity.Mathematics;

namespace EnhancedValheimVRM
{
    // once on load. the bends point each thigh and shin bone forward so thickness and length dont mix
    internal sealed class SeatSupportMeasurement
    {
        internal const int Hips = 1, LeftThigh = 2, RightThigh = 3, Foot = 4, LeftShin = 5, RightShin = 6;

        private readonly float3 _hips, _leftThigh, _rightThigh, _leftKnee, _rightKnee;
        private readonly quaternion _leftBend, _rightBend, _leftShinBend, _rightShinBend;
        private readonly float _leftShinLength, _rightShinLength;
        private readonly List<float> _leftThickness = new List<float>();
        private readonly List<float> _rightThickness = new List<float>();
        private readonly List<float> _leftCalf = new List<float>();
        private readonly List<float> _rightCalf = new List<float>();
        private readonly List<float> _ground = new List<float>();

        internal SeatSupportMeasurement(float3 hips,
            float3 leftThigh,
            float3 rightThigh,
            quaternion leftBend,
            quaternion rightBend,
            float3 leftKnee,
            float3 rightKnee,
            quaternion leftShinBend,
            quaternion rightShinBend,
            float leftShinLength,
            float rightShinLength)
        {
            _hips = hips;
            _leftThigh = leftThigh;
            _rightThigh = rightThigh;
            _leftBend = leftBend;
            _rightBend = rightBend;
            _leftKnee = leftKnee;
            _rightKnee = rightKnee;
            _leftShinBend = leftShinBend;
            _rightShinBend = rightShinBend;
            _leftShinLength = leftShinLength;
            _rightShinLength = rightShinLength;
        }

        // group 0 = any other bone
        internal void Add(float3 vertex, int4 groups, float4 weights)
        {
            if (!math.all(math.isfinite(vertex)) || !math.all(math.isfinite(weights))) return;
            var dominant = 0;
            for (var i = 1; i < 4; i++)
            {
                if (weights[i] > weights[dominant]) dominant = i;
            }

            if (weights[dominant] < 0.5f || groups[dominant] == 0) return;

            var group = groups[dominant];
            if (group == Foot)
                _ground.Add(vertex.y);
            else if (group == LeftThigh)
                _leftThickness.Add(-math.rotate(_leftBend, vertex - _leftThigh).y);
            else if (group == RightThigh)
                _rightThickness.Add(-math.rotate(_rightBend, vertex - _rightThigh).y);
            else if (group == LeftShin)
                AddCalf(_leftCalf, math.rotate(_leftShinBend, vertex - _leftKnee), _leftShinLength);
            else if (group == RightShin)
                AddCalf(_rightCalf, math.rotate(_rightShinBend, vertex - _rightKnee), _rightShinLength);
        }

        // like the thigh but only the top half of the shin
        private static void AddCalf(List<float> calf, float3 bent, float length)
        {
            if (bent.z >= 0 && bent.z <= length * 0.5f) calf.Add(-bent.y);
        }

        internal bool TryMeasure(out float3 support)
        {
            support = float3.zero;
            if (_leftThickness.Count < 8 || _rightThickness.Count < 8 || _ground.Count < 8 ||
                _leftCalf.Count < 8 || _rightCalf.Count < 8)
                return false;
            _leftThickness.Sort();
            _rightThickness.Sort();
            _leftCalf.Sort();
            _rightCalf.Sort();
            _ground.Sort();
            // skip the last 2% so stray verts dont count
            var left = math.max(0, _leftThickness[(int)(_leftThickness.Count * 0.98f)]);
            var right = math.max(0, _rightThickness[(int)(_rightThickness.Count * 0.98f)]);
            var leftCalf = math.max(0, _leftCalf[(int)(_leftCalf.Count * 0.98f)]);
            var rightCalf = math.max(0, _rightCalf[(int)(_rightCalf.Count * 0.98f)]);
            var groundToHip = _hips.y - _ground[(int)(_ground.Count * 0.02f)];
            // hip bone height above the thigh joints differs per rig, add that gap
            var hipDrop = _hips.y - (_leftThigh.y + _rightThigh.y) * 0.5f;
            // x ground to hip, y hip bone to bottom of thigh mesh, z shin bone to back of calf mesh
            support = new float3(groundToHip, hipDrop + (left + right) * 0.5f, (leftCalf + rightCalf) * 0.5f);
            return math.all(math.isfinite(support)) && groundToHip > 0 && support.y > 0 && support.z > 0;
        }
    }
}
