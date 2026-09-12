using Unity.Mathematics;

namespace EnhancedValheimVRM
{
    // Render between completed physics samples, with at most one fixed tick of
    // visual delay. Never writes to the authoritative player/physics transform.
    public sealed class FixedRotationSmoother
    {
        private bool _ready;
        private float _sampleTime;
        private float3 _samplePosition;
        private quaternion _previous, _current;

        public void Reset() => _ready = false;

        public quaternion Sample(quaternion rotation, float3 position, float fixedTime, float renderTime, float fixedDelta)
        {
            bool discontinuity = !_ready || fixedDelta <= 0 || !math.isfinite(fixedDelta) ||
                fixedTime < _sampleTime || fixedTime - _sampleTime > fixedDelta * 2.5f ||
                math.distancesq(position, _samplePosition) > 25f ||
                // A turn over 90 degrees in one sample is a snap/teleport, not locomotion.
                (_ready && math.abs(math.dot(rotation.value, _current.value)) < 0.7071067f);
            if (discontinuity)
            {
                _previous = _current = rotation;
                _sampleTime = fixedTime;
                _samplePosition = position;
                _ready = true;
                return rotation;
            }
            if (fixedTime > _sampleTime)
            {
                _previous = _current;
                _current = rotation;
                _sampleTime = fixedTime;
                _samplePosition = position;
            }
            float alpha = math.saturate((renderTime - fixedTime) / fixedDelta);
            return math.slerp(_previous, _current, alpha);
        }
    }
}
