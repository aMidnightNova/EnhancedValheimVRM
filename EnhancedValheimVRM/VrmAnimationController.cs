using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmAnimationController : MonoBehaviour
    {
        const int FirstTime = -161139084;
        const int Usually = 229373857; // standing idle
        const int FirstRise = -1536343465; // stand up upon login
        const int RiseUp = -805461806;
        const int StartToSitDown = 890925016;
        const int SittingIdle = -1544306596;
        const int StandingUpFromSit = -805461806;
        const int SittingChair = -1829310159;
        const int SittingThrone = 1271596;
        const int SittingShip = -675369009;
        const int StartSleeping = 337039637;
        const int Sleeping = -1603096;
        const int GetUpFromBed = -496559199;
        const int Crouch = -2015693266;
        const int HoldingMast = -2110678410;
        const int HoldingDragon = -2076823180; // that thing in a front of longship


        private List<int> adjustHipHashes = new List<int>()
        {
            SittingChair,
            SittingThrone,
            SittingShip,
            Sleeping
        };


        private Player _player;
        private VrmInstance _vrmInstance;
        private Animator _playerAnimator;
        private Animator _vrmAnimator;

        private HumanPose _humanPose = new HumanPose();
        private HumanPoseHandler _playerPoseHandler, _vrmPoseHandler;

        public void Setup(Player player, VrmInstance vrmInstance)
        {
            _player = player;
            _vrmInstance = vrmInstance;

            _playerAnimator = _player.GetComponentInChildren<Animator>();

            var vrmGo = _vrmInstance.GetGameObject();

            _vrmAnimator = vrmGo.GetComponentInChildren<Animator>();

            vrmGo.transform.SetParent(_playerAnimator.transform.parent, false);
            //_player.gameObject.AddComponent<VrmController>();
            CreatePoseHandlers();
        }
        private void CreatePoseHandlers()
        {
            OnDestroy();
            _playerPoseHandler = new HumanPoseHandler(_playerAnimator.avatar, _playerAnimator.transform);
            _vrmPoseHandler = new HumanPoseHandler(_vrmAnimator.avatar, _vrmAnimator.transform);
        }

        private void LateUpdate()
        {
            // TODO: figure out why this is being set to zero each frame.
            _vrmAnimator.transform.localPosition = Vector3.zero;
            
            _vrmPoseHandler.SetHumanPose(ref _humanPose);
            _playerPoseHandler.GetHumanPose(ref _humanPose);
            
            
            
        }


        void OnDestroy()
        {
            if (_playerPoseHandler != null)
            {
                _playerPoseHandler.Dispose();
            }

            if (_vrmPoseHandler != null)
            {
                _vrmPoseHandler.Dispose();
            }
        }
    }
}