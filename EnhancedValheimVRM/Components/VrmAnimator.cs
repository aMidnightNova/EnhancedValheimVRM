using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmAnimator : MonoBehaviour
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


        private HumanPose _humanPose = new HumanPose();
        private HumanPoseHandler _playerPoseHandler, _vrmPoseHandler;

        private readonly Dictionary<HumanBodyBones, float> _boneLengthRatios = new Dictionary<HumanBodyBones, float>();
        private float offset;

        private class AttachmentPoint
        {
            public Transform Player;
            public Transform Vrm;
            public Vector3 PlayerOriginalLocalScale;
        }

        private List<AttachmentPoint> _attachmentPoints;

        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance)
        {
            _player = player;
            _playerAnimator = playerAnimator;
            _vrmInstance = vrmInstance;


            Logger.Log("__________VRM Animation Controller SETUP");

            var vrmGo = _vrmInstance.GetGameObject();
            _vrmAnimator = vrmGo.GetComponent<Animator>();
            // this is attached to vrmGo, this the below is the same as above, but the above is more clear.
            //_vrmAnimator = GetComponent<Animator>();
            _vrmAnimator.applyRootMotion = true;
            _vrmAnimator.updateMode = _playerAnimator.updateMode;
            _vrmAnimator.feetPivotActive = _playerAnimator.feetPivotActive;
            _vrmAnimator.layersAffectMassCenter = _playerAnimator.layersAffectMassCenter;
            _vrmAnimator.stabilizeFeet = _playerAnimator.stabilizeFeet;


            Transform vrmLeftFoot = _vrmAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform vrmRightFoot = _vrmAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform vrmHips = _vrmAnimator.GetBoneTransform(HumanBodyBones.Hips);


            offset = ((vrmLeftFoot.position.y - _vrmAnimator.transform.position.y) +
                      (vrmRightFoot.position.y - _vrmAnimator.transform.position.y)) / 2.0f;
            

            
            //_player.gameObject.AddComponent<VrmController>();
            CreatePoseHandlers();

            //CreateBoneRatios();
            //SetupAttachPoints();
        }


        private void SetupAttachPoints()
        {
            if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
            {
                visEquipment.m_leftHand = _vrmAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                visEquipment.m_rightHand = _vrmAnimator.GetBoneTransform(HumanBodyBones.RightHand);

                visEquipment.m_helmet = _vrmAnimator.GetBoneTransform(HumanBodyBones.Head);
                visEquipment.m_backShield = _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backMelee = _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);

                visEquipment.m_backTwohandedMelee = _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backBow = _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backTool = _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backAtgeir = _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
            }
        }


        private void SetAttachPoint(Transform playerPoint, Transform vrmPoint)
        {
            if (playerPoint == null)
            {
                Logger.LogError("SetAttachPoint: playerPoint is null.");
            }

            if (vrmPoint == null)
            {
                Logger.LogError("SetAttachPoint: vrmPoint is null.");
            }

            _attachmentPoints.Add(new AttachmentPoint
            {
                Player = playerPoint,
                Vrm = vrmPoint,
                PlayerOriginalLocalScale = playerPoint.localScale
            });
        }

        private bool _runningSetupAttach2 = false;

        private void SetupAttachPoints2()
        {
            _runningSetupAttach2 = true;
            _attachmentPoints = new List<AttachmentPoint>();

            if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
            {
                Logger.Log("_______________ m_visEquipment");
                SetAttachPoint(visEquipment.m_leftHand, _vrmAnimator.GetBoneTransform(HumanBodyBones.LeftHand));
                SetAttachPoint(visEquipment.m_rightHand, _vrmAnimator.GetBoneTransform(HumanBodyBones.RightHand));

                SetAttachPoint(visEquipment.m_helmet, _vrmAnimator.GetBoneTransform(HumanBodyBones.Head));
                SetAttachPoint(visEquipment.m_backShield, _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest));
                SetAttachPoint(visEquipment.m_backMelee, _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest));

                SetAttachPoint(visEquipment.m_backTwohandedMelee, _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest));
                SetAttachPoint(visEquipment.m_backBow, _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest));
                SetAttachPoint(visEquipment.m_backTool, _vrmAnimator.GetBoneTransform(HumanBodyBones.Chest));
                SetAttachPoint(visEquipment.m_backAtgeir, _vrmAnimator.GetBoneTransform(HumanBodyBones.Spine));
            }
        }

        public Animator GetPlayerAnimator()
        {
            return _playerAnimator;
        }

        private void CreatePoseHandlers()
        {
            Logger.LogWarning("_______ CreatePoseHandlers");
            OnDestroy();
            _playerPoseHandler = new HumanPoseHandler(_playerAnimator.avatar, _playerAnimator.transform);
            _vrmPoseHandler = new HumanPoseHandler(_vrmAnimator.avatar, _vrmAnimator.transform);
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


        private void UpdateBones()
        {
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var playerBone = _playerAnimator.GetBoneTransform(bone);
                var vrmBone = _vrmAnimator.GetBoneTransform(bone);
                _playerAnimator.rootPosition = _vrmAnimator.rootPosition;
                if (playerBone != null && vrmBone != null)
                {
                    playerBone.position = vrmBone.position;
                }
            }
        }
        

        private void Update()
        {
            UpdateBones();
        }


        private void LateUpdate()
        {
            var settings = _vrmInstance.GetSettings();


            _vrmAnimator.transform.localPosition = Vector3.zero;
            _playerAnimator.transform.localPosition = Vector3.zero;
            
            _playerPoseHandler.GetHumanPose(ref _humanPose);
            _vrmPoseHandler.SetHumanPose(ref _humanPose);

            var stateHash = _playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;

            if (_runningSetupAttach2)
            {
                foreach (var attachmentPoint in _attachmentPoints)
                {
                    attachmentPoint.Player.position = attachmentPoint.Vrm.position;
                    attachmentPoint.Player.rotation = attachmentPoint.Vrm.rotation;
                    attachmentPoint.Player.localScale = Vector3.Scale(attachmentPoint.Player.localScale, attachmentPoint.Vrm.localScale);
                }
            }
 
 
 
        }


        private void Working()
        {
            var settings = _vrmInstance.GetSettings();


            _vrmAnimator.transform.localPosition = Vector3.zero;
            _playerAnimator.transform.localPosition = Vector3.zero;
            
            _playerPoseHandler.GetHumanPose(ref _humanPose);
            _vrmPoseHandler.SetHumanPose(ref _humanPose);

            var stateHash = _playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;




            if (_runningSetupAttach2)
            {
                foreach (var attachmentPoint in _attachmentPoints)
                {
                    attachmentPoint.Player.position = attachmentPoint.Vrm.position;
                    attachmentPoint.Player.rotation = attachmentPoint.Vrm.rotation;
                    attachmentPoint.Player.localScale = Vector3.Scale(attachmentPoint.Player.localScale, attachmentPoint.Vrm.localScale);
                }
            }
            Transform playerHips = _playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform vrmHips = _vrmAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform vrmLeftFoot = _vrmAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform vrmRightFoot = _vrmAnimator.GetBoneTransform(HumanBodyBones.RightFoot);




            UpdateBones();

 
            var ground = Mathf.Min(vrmLeftFoot.position.y - _vrmAnimator.transform.position.y, 
                vrmRightFoot.position.y - _vrmAnimator.transform.position.y, 0);
            
            vrmHips.Translate(0, -ground + offset, 0, Space.World);
            playerHips.Translate(0, -ground + offset, 0, Space.World);
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