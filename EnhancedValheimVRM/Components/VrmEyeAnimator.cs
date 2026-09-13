using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmEyeAnimator : MonoBehaviour
    {
        private Transform _vrmEyes;
        private Transform _playerEyes;

        private Animator _playerAnimator;

        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance)
        {
            _playerAnimator = vrmInstance.GetVrmGoAnimator();

            _vrmEyes = BoneLookup.Get(_playerAnimator, HumanBodyBones.LeftEye);

            if (_vrmEyes == null) _vrmEyes = BoneLookup.Get(_playerAnimator, HumanBodyBones.Head);

            if (_vrmEyes == null) _vrmEyes = BoneLookup.Get(_playerAnimator, HumanBodyBones.Neck);


            if (player != null)
                _playerEyes = player.m_eye;
            else
                Logger.LogError("Player component or m_eye is null. Ensure the component exists.");
        }

        private void LateUpdate()
        {
            if (_playerEyes && _vrmEyes)
            {
                var pos = _playerEyes.position;
                pos.y = _vrmEyes.position.y;

                _playerEyes.position = pos;
            }
        }
    }
}
