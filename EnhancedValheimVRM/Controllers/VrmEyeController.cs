using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmEyeController : MonoBehaviour
    {
        private Transform _vrmEyes;
        private Transform _playerEyes;

        private Animator _playerAnimator;

        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance)
        {
            _playerAnimator = playerAnimator;
            
            _vrmEyes = _playerAnimator.GetBoneTransform(HumanBodyBones.LeftEye);

            if (_vrmEyes == null)
            {
                _vrmEyes = _playerAnimator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (_vrmEyes == null)
            {
                _vrmEyes = _playerAnimator.GetBoneTransform(HumanBodyBones.Neck);
            }


            if (player != null)
            {
                _playerEyes = player.m_eye;
            }
            else
            {
                Logger.LogError("Player component or m_eye is null. Ensure the component exists.");
            }
        }

        void LateUpdate()
        {
            if (_playerEyes && _vrmEyes)
            {
                var pos = _playerEyes.position;
                pos.y = _vrmEyes.position.y;

                //TODO: figure out if the player eye should be set to the vrm eye pos.
                _playerEyes.position = pos;
            }
        }
    }
}