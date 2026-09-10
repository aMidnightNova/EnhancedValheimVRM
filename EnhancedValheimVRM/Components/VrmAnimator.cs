using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmAnimator : MonoBehaviour, IMonoUpdater
    {
        // private const int FirstTime = -161139084;
        // private const int StandingIdle = 229373857; // standing idle 
        // private const int FirstRise = -1536343465; // stand up upon login
        // private const int RiseUp = -805461806;
        // private const int StartToSitDown = 890925016;
        // private const int SittingIdle = -1544306596;
        // private const int StandingUpFromSit = -805461806; // same as RiseUp
        // private const int SittingChair = -1829310159;
        // private const int SittingThrone = 1271596;
        // private const int SittingShip = -675369009;
        // private const int StartSleeping = 337039637;
        // private const int Sleeping = -1603096;
        // private const int GetUpFromBed = -496559199;
        // private const int Crouch = -2015693266;
        // private const int HoldingMast = -2110678410;
        // private const int HoldingDragon = -2076823180; // that thing in a front of longship
        // private const int RollingLeft = 21017266;
        // private const int RollingRight = 1353639306;


        // states probably no longer matter with this method.
        // private readonly List<int> _adjustHipHashes = new List<int>()
        // {
        //     SittingChair,
        //     SittingThrone,
        //     SittingShip,
        //     Sleeping,
        //     //
        //     FirstRise,
        //     StartToSitDown,
        //     SittingIdle,
        //     StartSleeping,
        //     GetUpFromBed
        // };

        // private Vector3 StateHashToOffset(int stateHash)
        // {
        //     switch (stateHash)
        //     {
        //         case StartToSitDown:
        //         case SittingIdle:
        //             return _vrmSettings.SittingIdleOffset;
        //         case SittingChair:
        //             return _vrmSettings.SittingOnChairOffset;
        //
        //         case SittingThrone:
        //             return _vrmSettings.SittingOnThroneOffset;
        //
        //         case SittingShip:
        //             return _vrmSettings.SittingOnShipOffset;
        //
        //         case HoldingMast:
        //             return _vrmSettings.HoldingMastOffset;
        //
        //         case HoldingDragon:
        //             return _vrmSettings.HoldingDragonOffset;
        //
        //         case Sleeping:
        //             return _vrmSettings.SleepingOffset;
        //
        //         default:
        //             return Vector3.zero;
        //     }
        // }


        // private class AttachmentPoint
        // {
        //     public Transform Player;
        //     public Transform Vrm;
        //     public Vector3 PlayerOriginalLocalScale;
        // }
        //
        // private List<AttachmentPoint> _attachmentPoints;

        //
        // private void SetAttachPoint(Transform playerPoint, Transform vrmPoint)
        // {
        //     if (playerPoint == null)
        //     {
        //         Logger.LogError("SetAttachPoint: playerPoint is null.");
        //     }
        //
        //     if (vrmPoint == null)
        //     {
        //         Logger.LogError("SetAttachPoint: vrmPoint is null.");
        //     }
        //
        //     _attachmentPoints.Add(new AttachmentPoint
        //     {
        //         Player = playerPoint,
        //         Vrm = vrmPoint,
        //         PlayerOriginalLocalScale = playerPoint.localScale
        //     });
        // }
        //
        // private bool _runningSetupAttach2 = false;
        //
        // private void SetupAttachPoints2()
        // {
        //     _runningSetupAttach2 = true;
        //     _attachmentPoints = new List<AttachmentPoint>();
        //
        //     if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
        //     {
        //         Logger.Log("_______________ m_visEquipment");
        //         SetAttachPoint(visEquipment.m_leftHand, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand));
        //         SetAttachPoint(visEquipment.m_rightHand, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand));
        //
        //         SetAttachPoint(visEquipment.m_helmet, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head));
        //         SetAttachPoint(visEquipment.m_backShield, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest));
        //         SetAttachPoint(visEquipment.m_backMelee, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest));
        //
        //         SetAttachPoint(visEquipment.m_backTwohandedMelee, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest));
        //         SetAttachPoint(visEquipment.m_backBow, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest));
        //         SetAttachPoint(visEquipment.m_backTool, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest));
        //         SetAttachPoint(visEquipment.m_backAtgeir, _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Spine));
        //     }
        // }
        //
        // private void UpdateBones()
        // {
        //     if (_isRagdoll) return;
        //
        //     foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        //     {
        //         if (bone == HumanBodyBones.LastBone) continue;
        //
        //         var playerBone = _playerAnimator.GetBoneTransform(bone);
        //         var vrmBone = _vrmGoAnimator.GetBoneTransform(bone);
        //         //_playerAnimator.rootPosition = _vrmGoAnimator.rootPosition;
        //         if (playerBone != null && vrmBone != null)
        //         {
        //             playerBone.position = vrmBone.position;
        //         }
        //     }
        // }


        // private void Update()
        // {
        //
        // }


        // private void LateUpdate()
        // {
        //      
        //
        //
        //     _vrmGoAnimator.transform.localPosition = Vector3.zero;
        //     _playerAnimator.transform.localPosition = Vector3.zero;
        //     
        //     _playerPoseHandler.GetHumanPose(ref _humanPose);
        //     _vrmPoseHandler.SetHumanPose(ref _humanPose);
        //
        //     var stateHash = _playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        //
        //     if (_runningSetupAttach2)
        //     {
        //         foreach (var attachmentPoint in _attachmentPoints)
        //         {
        //             attachmentPoint.Player.position = attachmentPoint.Vrm.position;
        //             attachmentPoint.Player.rotation = attachmentPoint.Vrm.rotation;
        //             attachmentPoint.Player.localScale = Vector3.Scale(attachmentPoint.Player.localScale, attachmentPoint.Vrm.localScale);
        //         }
        //     }
        //
        //
        //
        // }


        // end old stuff, will clean eventuly


        private static readonly string LeftHandBoneName = BoneTransformer.MapHumanBodyBoneToPlayerBoneName(HumanBodyBones.LeftHand);
        private static readonly string RightHandBoneName = BoneTransformer.MapHumanBodyBoneToPlayerBoneName(HumanBodyBones.RightHand);

        private static readonly string LeftMiddleFinger = BoneTransformer.MapHumanBodyBoneToPlayerBoneName(HumanBodyBones.LeftMiddleProximal);
        private static readonly string RightMiddleFinger = BoneTransformer.MapHumanBodyBoneToPlayerBoneName(HumanBodyBones.RightMiddleProximal);

        public static List<IMonoUpdater> Instances { get; } = new List<IMonoUpdater>();

        private Vector3 _leftHandItemInstanceTransformOffset;
        private Vector3 _rightHandItemInstanceTransformOffset;


        private GameObject _leftHandItemInstance;
        private Transform _leftHandItemInstanceTransform;


        public GameObject LeftHandItemInstance
        {
            get => _leftHandItemInstance;
            set
            {
                _leftHandItemInstance = value;
                if (_leftHandItemInstance != null)
                {
                    SetupHands();
                }
            }
        }

        private GameObject _rightHandItemInstance;
        private Transform _rightHandItemInstanceTransform;

        public GameObject RightHandItemInstance
        {
            get => _rightHandItemInstance;
            set
            {
                _rightHandItemInstance = value;
                if (_rightHandItemInstance != null)
                {
                    SetupHands();
                }
            }
        }


        private void SetupHands()
        {
            Transform rootBone = null;
            if (!GameItem.IsSpecialCase(LeftHandItemInstanceName) || !GameItem.IsSpecialCase(RightHandItemInstanceName))
            {
                var playerSmrBody = _player.GetSmrBody();

                if (playerSmrBody != null)
                {
                    rootBone = playerSmrBody.rootBone;
                    if (rootBone == null)
                    {
                        Logger.LogWarning("Root bone is null for player SMR body.");
                    }
                }
                else
                {
                    Logger.LogWarning("Player SMR body not found.");
                }
            }

            Logger.LogWarning($"-______ Items -> {LeftHandItemInstanceName}, {RightHandItemInstanceName}");


            // probably only need to change right hand items against special case stuff.

            var isSpecialCaseRight = GameItem.IsSpecialCase(RightHandItemInstanceName);

            if (isSpecialCaseRight)
            {
                _rightHandItemInstanceTransform = BoneTransformer.FindBoneInHierarchy(_rightHandItemInstance.transform, RightHandBoneName);
            }
            else
            {
                _rightHandItemInstanceTransform = _visEquipment.m_rightHand;
                //_rightHandItemInstanceTransform = BoneTransformer.FindBoneInHierarchy(rootBone, "RightHand_Attach");
            }


            if (isSpecialCaseRight || GameItem.IsSpecialCase(LeftHandItemInstanceName))
            {
                _leftHandItemInstanceTransform = BoneTransformer.FindBoneInHierarchy(_rightHandItemInstance.transform, LeftHandBoneName);
            }
            else
            {
                _leftHandItemInstanceTransform = _visEquipment.m_leftHand;
                //_leftHandItemInstanceTransform = BoneTransformer.FindBoneInHierarchy(rootBone, "LeftHand_Attach");
            }


            var leftHand = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand);

            var leftMiddleFinger = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            var rightMiddleFinger = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);

            var leftHandMid = (leftMiddleFinger.position + leftHand.position) / 2f;
            var rightHandMid = (rightMiddleFinger.position + rightHand.position) / 2f;


            if (isSpecialCaseRight) // this is the duel wield weapons.
            {
                var scale = 1f - _vrmSettings.PlayerVrmScale;
                var scaledBoneOffset = new Vector3(0, -(0.06f * scale), 0.04f * scale);

                var leftHandOffset = (leftHandMid - leftHand.position) / 10;
                _leftHandItemInstanceTransformOffset = leftHandOffset + scaledBoneOffset;

                var rightHandOffset = -((rightHandMid - rightHand.position) / 10);
                _rightHandItemInstanceTransformOffset = rightHandOffset + scaledBoneOffset;
            }
            else
            {
                var leftHandOffset = _leftHandItemInstanceTransform.position - _leftHandItemInstanceTransform.parent.position;
                
                
                _leftHandItemInstanceTransformOffset = Vector3.zero;

                var rightHandOffset = _rightHandItemInstanceTransform.position - _rightHandItemInstanceTransform.parent.position;
                _rightHandItemInstanceTransformOffset = Vector3.zero;
            }

            Logger.Log($"Hand Offsets -> Left:{_leftHandItemInstanceTransformOffset.ToString("F6")}, Right:{_rightHandItemInstanceTransformOffset.ToString("F6")}",
                Logger.LogLevel.All);
        }

        public GameObject LeftHandBackItemInstance { get; set; }
        public GameObject RightHandBackItemInstance { get; set; }

        // State variables for item names
        public string LeftHandItemInstanceName { get; set; }
        public string RightHandItemInstanceName { get; set; }
        public string LeftHandBackItemInstanceName { get; set; }
        public string RightHandBackItemInstanceName { get; set; }

        private Transform _vrmLeftHandTransform;
        private Transform _vrmRightHandTransform;
        private Transform _playerLeftHandTransform;
        private Transform _playerRightHandTransform;


        private Player _player;
        private VrmInstance _vrmInstance;
        private Animator _playerAnimator;
        private Animator _vrmGoAnimator;
        private GameObject _vrmGo;
        private VrmSettings _vrmSettings;


        private HumanPose _humanPose = new HumanPose();
        private HumanPoseHandler _playerPoseHandler, _vrmPoseHandler;

        private readonly Dictionary<HumanBodyBones, float> _boneLengthRatios = new Dictionary<HumanBodyBones, float>();


        private bool _isRagdoll = false;


        private VisEquipment _visEquipment;


        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance, bool isRagdoll = false)
        {
            _player = player;
            _playerAnimator = playerAnimator;
            _vrmInstance = vrmInstance;
            _isRagdoll = isRagdoll;
            _vrmSettings = vrmInstance.GetSettings();

            _vrmGo = vrmInstance.GetGameObject();
            _vrmGoAnimator = _vrmGo.GetComponent<Animator>();
            // this is attached to vrmGo, this the below is the same as above, but the above is more clear.
            //_vrmGoAnimator = GetComponent<Animator>();
            _vrmGoAnimator.applyRootMotion = true;
            _vrmGoAnimator.updateMode = _playerAnimator.updateMode;
            _vrmGoAnimator.feetPivotActive = _playerAnimator.feetPivotActive;
            _vrmGoAnimator.layersAffectMassCenter = _playerAnimator.layersAffectMassCenter;
            _vrmGoAnimator.stabilizeFeet = _playerAnimator.stabilizeFeet;

            _vrmLeftHandTransform = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            _vrmRightHandTransform = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand);

            _playerLeftHandTransform = _playerAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            _playerRightHandTransform = _playerAnimator.GetBoneTransform(HumanBodyBones.RightHand);


            if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
            {
                _visEquipment = visEquipment;
                StartupGetItems();
            }


            //_player.gameObject.AddComponent<VrmController>();
            CreatePoseHandlers();


            //CreateBoneRatios();
            //SetupAttachPoints();


            SetupAttachPoints();
            Instances.Add(this);
        }

        void StartupGetItems()
        {
            if (_visEquipment.GetFieldValue<FieldInfo>("m_leftItem")?.GetValue(_visEquipment) is string leftItemName)
            {
                LeftHandItemInstanceName = leftItemName;
                if (_visEquipment.TryGetField<VisEquipment, GameObject>("m_leftItemInstance", out var leftItemInstance))
                {
                    LeftHandItemInstance = leftItemInstance;
                }
            }

            if (_visEquipment.GetFieldValue<FieldInfo>("m_rightItem")?.GetValue(_visEquipment) is string rightItemName)
            {
                RightHandItemInstanceName = rightItemName;
                if (_visEquipment.TryGetField<VisEquipment, GameObject>("m_rightItemInstance", out var rightItemInstance))
                {
                    RightHandItemInstance = rightItemInstance;
                }
            }

            if (_visEquipment.GetFieldValue<FieldInfo>("m_leftBackItem")?.GetValue(_visEquipment) is string leftBackItemName)
            {
                LeftHandBackItemInstanceName = leftBackItemName;
                if (_visEquipment.TryGetField<VisEquipment, GameObject>("m_leftBackItemInstance", out var leftBackItemInstance))
                {
                    LeftHandBackItemInstance = leftBackItemInstance;
                }
            }

            if (_visEquipment.GetFieldValue<FieldInfo>("m_rightBackItem")?.GetValue(_visEquipment) is string rightBackItemName)
            {
                RightHandBackItemInstanceName = rightBackItemName;
                if (_visEquipment.TryGetField<VisEquipment, GameObject>("m_rightBackItemInstance", out var rightBackItemInstance))
                {
                    RightHandBackItemInstance = rightBackItemInstance;
                }
            }
        }


        private void ReParentAttachPoint(Transform newParent, Transform child, Vector3? newScale, Vector3 rotation)
        {
            // this is up here, becase we want the offset in certain instances but it needs to be before its parent is changed.
            
            var offset = child.position - child.parent.position;
            var localOffset = child.parent.InverseTransformPoint(child.position) * (100f * _vrmSettings.PlayerVrmScale);
            var localOffsetO = child.parent.InverseTransformPoint(child.position) * 100f;
            if (newScale.HasValue)
            {
                child.localScale = newScale.Value;
            }

            // the localPosition is most likley the bones offset from the parent bone.
            // and i need to recheck all the calculations that deal with offsets becase
            // i was not accounting for this fact when making them.
            var originalLocalPosition = child.localPosition;
            
            child.localPosition = Vector3.zero;

            child.SetParent(newParent, false);
 
            var newWorldPosition = newParent.position;
            var newLocalPosition = Vector3.zero;
             
 



            if (child.name == "RightHand_Attach")
            {
                
                
                //child.Translate(localOffset);

                //var scale = 1f - _vrmSettings.PlayerVrmScale;
                newLocalPosition = new Vector3(localOffset.y * 0.8f,-localOffset.z,localOffset.x);

                if (_player.IsInStartMenu())
                {
                    //child.localPosition = new Vector3(-localOffset.y * (_vrmSettings.PlayerVrmScale * 0.8f), -localOffset.x, 0);
                }
                else
                {
                    //child.localPosition = new Vector3(-offset.x * _vrmSettings.PlayerVrmScale , -offset.y, 0); // x * (_vrmSettings.PlayerVrmScale * 0.8f)
                    //child.localPosition = new Vector3(localOffset.x, localOffset.y, localOffset.z); // x * (_vrmSettings.PlayerVrmScale * 0.8f)
                }


                //
                // var leftHand = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                // var rightHand = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                //
                // var leftMiddleFinger = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                // var rightMiddleFinger = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                //
                // var leftHandMid = (leftHand.position * (1f / 3f)) + (leftMiddleFinger.position * (2f / 3f));
                // var rightHandMid = (rightHand.position * (1f / 3f)) + (rightMiddleFinger.position * (2f / 3f));
                //
                // Logger.Log($"-------------", Logger.LogLevel.All);
                // Logger.Log($"rightHandMid -> {rightHandMid.ToString("F6")}, newWorldPosition -> {newWorldPosition.ToString("F6")}", Logger.LogLevel.All);
                // Logger.Log($"newParent.position -> {newParent.position.ToString("F6")}, offset -> {offset.ToString("F6")} ", Logger.LogLevel.All);
                // Logger.Log($"-------------", Logger.LogLevel.All);
                // //Logger.Log($"child {child.name} -> leftHandMid {leftHandMid.ToString("F6")}, rightHandMid {rightHandMid.ToString("F6")}, newWorldPosition {newWorldPosition.ToString("F6")}, offset {offset.ToString("F6")}", Logger.LogLevel.All);
                //
                // //if (child.name == "LeftHand_Attach") newWorldPosition = leftHandMid;
                // //if (child.name == "RightHand_Attach") newWorldPosition = rightHandMid;
            }

            if (child.name == "LeftHand_Attach")
            {   // this will help place the hand onto the shield handle.
                var scaledDepth = -0.035f * (1f - _vrmSettings.PlayerVrmScale);
                //newLocalPosition = new Vector3(-localOffset.y * 0.8f,scaledDepth,localOffset.z);
                newLocalPosition = new Vector3(-localOffset.y  * 0.8f,scaledDepth,localOffset.z);


                //child.Translate(localOffset);
                
                if (_player.IsInStartMenu())
                {
                    //child.localPosition = new Vector3(localOffset.y, localOffset.z, localOffset.x) * (_vrmSettings.PlayerVrmScale * 0.8f);
                }
                else
                {
                    //child.localPosition = new Vector3(localOffset.x, localOffset.y, localOffset.z);;
                }
                
                
                
            }
            
            child.position = newWorldPosition;
            
            //child.localPosition = newLocalPosition;
            child.SetLocalPositionAndRotation(newLocalPosition, Quaternion.Euler(rotation));
            //child.localRotation = Quaternion.Euler(rotation);

   
            if (child.name == "LeftHand_Attach" || child.name == "RightHand_Attach")
            {
                Logger.Log($"-----------", Logger.LogLevel.All);
                Logger.Log($"child {child.name} -> localOffsetO {localOffsetO.ToString("F6")}, localOffset {localOffset.ToString("F6")}", Logger.LogLevel.All);
                Logger.Log($"child {child.name} -> newWorldPosition {newWorldPosition.ToString("F6")}", Logger.LogLevel.All);
 
                Logger.Log($"-----------", Logger.LogLevel.All);

            }

        }


        private void SetupAttachPoints()
        {
            // no idea why but when putting items in the back slots, whiles having the armature of the player scaled causes the items to be 100 times bigger.

            try
            {
                var playerVisual = _player.GetField<Player, GameObject>("m_visual");

                if (playerVisual == null)
                {
                    throw new Exception("Player visual object (m_visual) not found.");
                }

                var armatureTransform = playerVisual.transform.Find("Armature");

                if (armatureTransform == null)
                {
                    throw new Exception("Armature transform is not found.");
                }

                var newScale = new Vector3(0.01f, 0.01f, 0.01f);

                // var attachPointNames = new HashSet<string>
                // {
                //     "BackShield_attach",
                //     "BackOneHanded_attach",
                //     "BackTwohanded_attach",
                //     "BackBow_attach",
                //     "BackTool_attach",
                //     "BackAtgeir_attach"
                // };

                // stuff should be 0.01 i think.
                var smallScale = Vector3.one * 0.01f;
 
                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand), _visEquipment.m_leftHand, smallScale, new Vector3(0, 0, -180));
                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand), _visEquipment.m_rightHand, smallScale, new Vector3(0, 0, 0));

                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head), _visEquipment.m_helmet, smallScale, new Vector3(-22.286f, -90, 0));

                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), _visEquipment.m_backShield, smallScale, new Vector3(260.842f, -110.573f, -95.08301f));

                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), _visEquipment.m_backMelee, smallScale, new Vector3(123.57f, 82.526f, 86.67f));

                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), _visEquipment.m_backTwohandedMelee, smallScale, new Vector3(126.719f, 84.227f, 89.261f));

                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), _visEquipment.m_backBow, smallScale, new Vector3(-111.234f, -56.51501f, 148.21f));

                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), _visEquipment.m_backAtgeir, smallScale, new Vector3(-74.321f, 30.416f, -213.174f));

                ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Hips), _visEquipment.m_backTool, smallScale, new Vector3(101.664f, -90.17902f, -179.256f));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in SetupAttachPoints: {ex.Message}");
            }
        }


        private void SetupAttachPoints2()
        {
            if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
            {
                visEquipment.m_leftHand = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                visEquipment.m_rightHand = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand);

                visEquipment.m_helmet = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head);
                visEquipment.m_backShield = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backMelee = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest);

                visEquipment.m_backTwohandedMelee = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backBow = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backTool = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backAtgeir = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest);
            }
        }


        public Animator GetPlayerAnimator()
        {
            return _playerAnimator;
        }

        private void CreatePoseHandlers()
        {
            Logger.LogWarning("CreatePoseHandlers");
            OnDestroy();
            _playerPoseHandler = new HumanPoseHandler(_playerAnimator.avatar, _playerAnimator.transform);
            _vrmPoseHandler = new HumanPoseHandler(_vrmGoAnimator.avatar, _vrmGoAnimator.transform);
        }


        // not sure why i need to include these next to methods when there not in other files of the game and it should be inherited from IMonoUpdater
        public void CustomFixedUpdate(float deltaTime)
        {
        }

        public void CustomUpdate(float deltaTime, float time)
        {
        }


        //Valheim has a Loop called CustomLateUpdate that is fired after LateUpdate but in such a way that you cant really create your own CustomLateUpdate
        //instead have to use a HarmonyPatch on MonoUpdaters LateUpdate instead.

        // private void LateUpdate()
        // {
        //     //CustomLateUpdate();
        // }
        // private void CustomLateUpdate()
        // {
        //     CustomLateUpdate(Time.deltaTime);
        // }

        public void CustomLateUpdate(float deltaTime)
        {
            _playerPoseHandler.GetHumanPose(ref _humanPose);
            _vrmPoseHandler.SetHumanPose(ref _humanPose);

            if (_isRagdoll) return;

            if (RightHandItemInstance == null) return;

            if (_vrmGoAnimator == null) return;

            if (GameItem.IsSpecialCase(RightHandItemInstanceName))
            {
                _leftHandItemInstanceTransform.rotation = _playerLeftHandTransform.rotation;
                _leftHandItemInstanceTransform.position = _vrmLeftHandTransform.position;
                //_leftHandItemInstanceTransform.localPosition = _leftHandItemInstanceTransformOffset;
                _leftHandItemInstanceTransform.Translate(_leftHandItemInstanceTransformOffset, Space.Self);

                _rightHandItemInstanceTransform.rotation = _playerRightHandTransform.rotation;
                _rightHandItemInstanceTransform.position = _vrmRightHandTransform.position;
                //_rightHandItemInstanceTransform.localPosition = _rightHandItemInstanceTransformOffset;

                _rightHandItemInstanceTransform.Translate(_rightHandItemInstanceTransformOffset, Space.Self);
            }
        }


        void OnDestroy()
        {
            Instances.Remove(this);

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