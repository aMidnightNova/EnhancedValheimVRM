using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmAnimator : MonoBehaviour, IMonoUpdater
    {
        public static List<IMonoUpdater> Instances { get; } = new List<IMonoUpdater>();

        const int FirstTime = -161139084;
        const int StandingIdle = 229373857; // standing idle 
        const int FirstRise = -1536343465; // stand up upon login
        const int RiseUp = -805461806;
        const int StartToSitDown = 890925016;
        const int SittingIdle = -1544306596;
        const int StandingUpFromSit = -805461806; // same as RiseUp
        const int SittingChair = -1829310159;
        const int SittingThrone = 1271596;
        const int SittingShip = -675369009;
        const int StartSleeping = 337039637;
        const int Sleeping = -1603096;
        const int GetUpFromBed = -496559199;
        const int Crouch = -2015693266;
        const int HoldingMast = -2110678410;
        const int HoldingDragon = -2076823180; // that thing in a front of longship
        const int RollingLeft = 21017266; 
        const int RollingRight = 1353639306; 


        private readonly List<int> _adjustHipHashes = new List<int>()
        {
            SittingChair,
            SittingThrone,
            SittingShip,
            Sleeping,
            //
            FirstRise,
            StartToSitDown,
            SittingIdle,
            StartSleeping,
            GetUpFromBed
        };


        private Player _player;
        private VrmInstance _vrmInstance;
        private Animator _playerAnimator;
        private Animator _vrmAnimator;
        private VrmSettings _vrmSettings;


        private HumanPose _humanPose = new HumanPose();
        private HumanPoseHandler _playerPoseHandler, _vrmPoseHandler;

        private readonly Dictionary<HumanBodyBones, float> _boneLengthRatios = new Dictionary<HumanBodyBones, float>();
        private float offset;
        private float offsetH;

        private class AttachmentPoint
        {
            public Transform Player;
            public Transform Vrm;
            public Vector3 PlayerOriginalLocalScale;
        }

        private List<AttachmentPoint> _attachmentPoints;


        private bool _isRagdoll = false;

        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance, bool isRagdoll = false)
        {
            _player = player;
            _playerAnimator = playerAnimator;
            _vrmInstance = vrmInstance;
            _isRagdoll = isRagdoll;
            _vrmSettings = vrmInstance.GetSettings();
            _playerCollider = _player.GetComponent<CapsuleCollider>();

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

            Transform playerLeftFoot = _playerAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform playerRightFoot = _playerAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform vrmHips = _vrmAnimator.GetBoneTransform(HumanBodyBones.Hips);


            offset = ((vrmLeftFoot.position.y - _vrmAnimator.transform.position.y) +
                      (vrmRightFoot.position.y - _vrmAnimator.transform.position.y)) * 0.5f;
            offsetH = ((playerLeftFoot.position + playerRightFoot.position) -
                      (vrmLeftFoot.position + vrmRightFoot.position)).y * 0.5f;

            //_player.gameObject.AddComponent<VrmController>();
            CreatePoseHandlers();


            //CreateBoneRatios();
            //SetupAttachPoints();
            ScaleAttachPoints();
            Instances.Add(this);
        }

        //private void OnEnable() =>  Instances.Add( this);


        private void ScaleAttachPoints()
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

                var attachPointNames = new HashSet<string>
                {
                    "BackShield_attach",
                    "BackOneHanded_attach",
                    "BackTwohanded_attach",
                    "BackBow_attach",
                    "BackTool_attach",
                    "BackAtgeir_attach"
                };

                var allTransforms = armatureTransform.GetComponentsInChildren<Transform>();

                foreach (var t in allTransforms)
                {
                    if (attachPointNames.Contains(t.name))
                    {
                        t.localScale = newScale;
                        Debug.Log($"Scaled {t.name} to {newScale}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in ScaleAttachPoints: {ex.Message}");
            }
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
            Logger.LogWarning("CreatePoseHandlers");
            OnDestroy();
            _playerPoseHandler = new HumanPoseHandler(_playerAnimator.avatar, _playerAnimator.transform);
            _vrmPoseHandler = new HumanPoseHandler(_vrmAnimator.avatar, _vrmAnimator.transform);
        }


        private Vector3 StateHashToOffset(int stateHash)
        {
            switch (stateHash)
            {
                case StartToSitDown:
                case SittingIdle:
                    return _vrmSettings.SittingIdleOffset;
                case SittingChair:
                    return _vrmSettings.SittingOnChairOffset;

                case SittingThrone:
                    return _vrmSettings.SittingOnThroneOffset;

                case SittingShip:
                    return _vrmSettings.SittingOnShipOffset;

                case HoldingMast:
                    return _vrmSettings.HoldingMastOffset;

                case HoldingDragon:
                    return _vrmSettings.HoldingDragonOffset;

                case Sleeping:
                    return _vrmSettings.SleepingOffset;

                default:
                    return Vector3.zero;
            }
        }

        private void UpdateBones()
        {
            if (_isRagdoll) return;

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var playerBone = _playerAnimator.GetBoneTransform(bone);
                var vrmBone = _vrmAnimator.GetBoneTransform(bone);
                //_playerAnimator.rootPosition = _vrmAnimator.rootPosition;
                if (playerBone != null && vrmBone != null)
                {
                    playerBone.position = vrmBone.position;
                }
            }
            
        }
 

        private static Material _playerSizeGizmoMaterial;
        private CapsuleCollider _playerCollider;
        private GameObject _playerSizeGizmo;

        public void ActivateSizeGizmo()
        {
            if (_playerSizeGizmo != null)
            {
                if (_playerCollider != null)
                {
                    _playerSizeGizmo.transform.position = _playerCollider.bounds.center;
                    _playerSizeGizmo.transform.localScale = new Vector3(_playerCollider.bounds.size.x, _playerCollider.bounds.size.y / 2, _playerCollider.bounds.size.z);
                }

                return;
            }

 

            if (_playerSizeGizmoMaterial == null)
            {
                _playerSizeGizmoMaterial = new Material(Shader.Find("Standard"));
                _playerSizeGizmoMaterial.SetFloat("_Mode", 2);
                _playerSizeGizmoMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _playerSizeGizmoMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _playerSizeGizmoMaterial.SetInt("_ZWrite", 0);
                _playerSizeGizmoMaterial.DisableKeyword("_ALPHATEST_ON");
                _playerSizeGizmoMaterial.EnableKeyword("_ALPHABLEND_ON");
                _playerSizeGizmoMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _playerSizeGizmoMaterial.SetFloat("_GlossMapScale", 0);
                _playerSizeGizmoMaterial.renderQueue = 3000;
                _playerSizeGizmoMaterial.color = new Color(1.0f, 0.0f, 0.0f, 0.3f);
            }


            Logger.Log("____________________ CREATE SIZE GIZMO");

            _playerSizeGizmo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _playerSizeGizmo.GetComponent<MeshRenderer>().material = _playerSizeGizmoMaterial;
            Destroy(_playerSizeGizmo.GetComponent<Collider>());
        }

        private void Update()
        {
            _vrmAnimator.transform.localPosition = Vector3.zero;

            //ActivateSizeGizmo();
 
            UpdateBones();

            //_vrmAnimator.transform.localPosition -= Vector3.up * (offset * _vrmSettings.PlayerVrmScale);

            //_vrmAnimator.transform.localPosition += Vector3.up * _vrmSettings.HeightOffsetY;
        }


        // private void LateUpdate()
        // {
        //      
        //
        //
        //     _vrmAnimator.transform.localPosition = Vector3.zero;
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


        private void LateUpdate()
        {
            //CustomLateUpdate();
        }


        // not sure why i need to include these next to methods when there not in other files of the game
        public void CustomFixedUpdate(float deltaTime)
        {
        }

        public void CustomUpdate(float deltaTime, float time)
        {
        }


        //Valheim has a Loop called CustomLateUpdate that is fired after LateUpdate but in such a way that you cant really create your own CustomLateUpdate
        //instead have to use a HarmonyPatch on MonoUpdaters LateUpdate instead.

        // with regard to the above comment. using CustomLateUpdate on a patched LateUpdate was only getting 42 fps the one way i got it to work.
        // all other ways it was jittery just the same as if there is no bone updates in Update()
        // after some review I dont think two bone update loops add any significant cpu usage so moving on. will revisit someday, maybe.

        private bool IsStartMenu()
        {
            return _player.gameObject.scene.name == "start";
        }

        private void CustomLateUpdate()
        {
            CustomLateUpdate(Time.deltaTime);
        }

        public void CustomLateUpdate(float deltaTime)
        {
            //Logger.Log("CustomLateUpdate");

            // this stops calulations from adding up per frame.
           _vrmAnimator.transform.localPosition = Vector3.zero;


            _playerPoseHandler.GetHumanPose(ref _humanPose);
            _vrmPoseHandler.SetHumanPose(ref _humanPose);


            // if (_runningSetupAttach2)
            // {
            //     foreach (var attachmentPoint in _attachmentPoints)
            //     {
            //         attachmentPoint.Player.position = attachmentPoint.Vrm.position;
            //         attachmentPoint.Player.rotation = attachmentPoint.Vrm.rotation;
            //         attachmentPoint.Player.localScale = Vector3.Scale(attachmentPoint.Player.localScale, attachmentPoint.Vrm.localScale);
            //     }
            // }


            Transform playerHips = _playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform vrmHips = _vrmAnimator.GetBoneTransform(HumanBodyBones.Hips);
 

            Transform vrmLeftFoot = _vrmAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform vrmRightFoot = _vrmAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

            

            
             var stateHash = _playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            
            
             var hipOffsetPosition = Vector3.zero;
             
             var surfacePlane = Mathf.Min(vrmLeftFoot.position.y - _vrmAnimator.transform.position.y,
                 vrmRightFoot.position.y - _vrmAnimator.transform.position.y);

    
             
             var _offset = ((vrmLeftFoot.position.y - _vrmAnimator.transform.position.y) +
                       (vrmRightFoot.position.y - _vrmAnimator.transform.position.y)) * 0.5f;

             var which = "";
             if (!IsStartMenu()) 
             {
                 if (_adjustHipHashes.Contains(stateHash)) //_adjustHipHashes.Contains(stateHash) || 
                 {
                     hipOffsetPosition += new Vector3(0, -surfacePlane, 0); // goodish
                     which = "In Hip Hash";

                     //hipOffsetPosition += new Vector3(0, (-ground + offset), 0);

                 }
                 else
                 {
                     hipOffsetPosition += new Vector3(0, -surfacePlane + offset, 0); // goodish
                     
                     which = "Not In Hip Hash";
                     // hipOffsetPosition = new Vector3(0f, offset, 0f);
                 }
             }
             Logger.Log($"{which}, {stateHash} -> anim parent y,{_vrmAnimator.transform.position.y }, l foot y {vrmLeftFoot.position.y}, surfacePlane {surfacePlane}, offset {offset}, _offset {_offset}");

             if (stateHash == FirstRise)
             { // not sure why this is needed, i probably borked calculations. but its good enough for now.
                 hipOffsetPosition += Vector3.up * 0.1f;
             }
             
             vrmHips.position += hipOffsetPosition; 
                
            
              //vrmHips.Translate(0, hipOffsetPosition.y, 0, Space.World);
             //_vrmAnimator.transform.Translate(0, -hipOffsetPosition.y, 0, Space.World);
 
 
            UpdateBones(); 
 

            //vrmHips.Translate(0, -ground + offset, 0, Space.World);
            //playerHips.Translate(0, -ground + offset, 0, Space.World);

            //Logger.Log($"_playerCollider.bounds.center {_playerCollider.bounds.center}");

            //playerHips.Translate(0, -ground + offset, 0, Space.World);
            //
            //_vrmAnimator.transform.localPosition -= Vector3.up * (offset * _vrmSettings.PlayerVrmScale);
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