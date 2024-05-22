using System;
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


        private readonly List<int> _adjustHipHashes = new List<int>()
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

        private float _playerScaleFactor;

        private HumanPose _humanPose = new HumanPose();
        private HumanPoseHandler _playerPoseHandler, _vrmPoseHandler;

        private readonly Dictionary<HumanBodyBones, float> _boneLengthRatios = new Dictionary<HumanBodyBones, float>();

        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance)
        {
            _player = player;
            _playerAnimator = playerAnimator;
            _vrmInstance = vrmInstance;


            var vrmGo = _vrmInstance.GetGameObject();

            _vrmAnimator = vrmGo.GetComponentInChildren<Animator>();
            _vrmAnimator.applyRootMotion = true;
            _vrmAnimator.updateMode = _playerAnimator.updateMode;
            _vrmAnimator.feetPivotActive = _playerAnimator.feetPivotActive;
            _vrmAnimator.layersAffectMassCenter = _playerAnimator.layersAffectMassCenter;
            _vrmAnimator.stabilizeFeet = _playerAnimator.stabilizeFeet;

            //_player.gameObject.AddComponent<VrmController>();
            CreatePoseHandlers();

            CreatePlayerScaleFactor();

            CreateBoneRatios();
        }

        private void CreatePoseHandlers()
        {
            OnDestroy();
            _playerPoseHandler = new HumanPoseHandler(_playerAnimator.avatar, _playerAnimator.transform);
            _vrmPoseHandler = new HumanPoseHandler(_vrmAnimator.avatar, _vrmAnimator.transform);
        }

        void CreatePlayerScaleFactor()
        {
            float playerHeight = Vector3.Distance(_playerAnimator.GetBoneTransform(HumanBodyBones.Head).position,
                _playerAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
            float vrmHeight = Vector3.Distance(_vrmAnimator.GetBoneTransform(HumanBodyBones.Head).position, _vrmAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position);

            _playerScaleFactor = vrmHeight / playerHeight;
        }

        void CreateBoneRatios()
        {
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                Transform playerBone = _playerAnimator.GetBoneTransform(bone);
                Transform vrmBone = _vrmAnimator.GetBoneTransform(bone);

                if (playerBone != null && vrmBone != null)
                {
                    float playerBoneLength = Vector3.Distance(playerBone.position, playerBone.parent.position);
                    float vrmBoneLength = Vector3.Distance(vrmBone.position, vrmBone.parent.position);
                    float boneLengthRatio = vrmBoneLength / playerBoneLength;

                    _boneLengthRatios[bone] = boneLengthRatio;
                }
            }
        }


        private Vector3 StateHashToOffset(int stateHash)
        {
            var settings = _vrmInstance.GetSettings();

            switch (stateHash)
            {
                case StartToSitDown:
                case SittingIdle:
                    return settings.SittingIdleOffset;
                case SittingChair:
                    return settings.SittingOnChairOffset;

                case SittingThrone:
                    return settings.SittingOnThroneOffset;

                case SittingShip:
                    return settings.SittingOnShipOffset;

                case HoldingMast:
                    return settings.HoldingMastOffset;

                case HoldingDragon:
                    return settings.HoldingDragonOffset;

                case Sleeping:
                    return settings.SleepingOffset;

                default:
                    return Vector3.zero;
            }
        }

        private void LateUpdate()
        {
            if (_player.IsDead()) return;

            _vrmAnimator.transform.localPosition = Vector3.zero;

            _playerPoseHandler.GetHumanPose(ref _humanPose);
            _vrmPoseHandler.SetHumanPose(ref _humanPose);

            Transform playerHips = _playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform vrmHips = _vrmAnimator.GetBoneTransform(HumanBodyBones.Hips);

            if (playerHips && vrmHips)
            {
                int currentStateHash = _playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;

                Vector3 currentAdjustedHipPosition;

                if (!_adjustHipHashes.Contains(currentStateHash))
                {
                    var curOrgHipPos = playerHips.position - playerHips.parent.position;
                    var curVrmHipPos = curOrgHipPos * _playerScaleFactor;

                    currentAdjustedHipPosition = curVrmHipPos - curOrgHipPos;
                }
                else
                {
                    currentAdjustedHipPosition = (playerHips.position * _playerScaleFactor) - playerHips.position;
                }
                
                var currentStateOffset = StateHashToOffset(currentStateHash);

                if (currentStateOffset != Vector3.zero) currentAdjustedHipPosition += playerHips.transform.rotation * currentStateOffset;

                vrmHips.position += currentAdjustedHipPosition;
                
                foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (bone == HumanBodyBones.LastBone) continue;

                    var playerBone = _playerAnimator.GetBoneTransform(bone);
                    var vrmBone = _vrmAnimator.GetBoneTransform(bone);

                    if (playerBone != null && vrmBone != null)
                    {
                        if (_boneLengthRatios.TryGetValue(bone, out var boneLengthRatio))
                        {
                            var relativePosition = playerBone.position - playerBone.parent.position;
                            var adjustedPosition = vrmBone.parent.position + (relativePosition * boneLengthRatio);

                            playerBone.position = adjustedPosition;
                            playerBone.rotation = vrmBone.rotation;
                        }
                    }
                }
            }
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