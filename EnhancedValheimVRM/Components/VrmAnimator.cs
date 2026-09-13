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
        public static List<IMonoUpdater> Instances { get; } = new List<IMonoUpdater>();

        private Transform _vrmLeftMiddleFinger;
        private Transform _vrmRightMiddleFinger;

        private GameObject _leftHandItemInstance;
        private Transform _leftHandItemInstanceTransform;

        public GameObject LeftHandItemInstance
        {
            get => _leftHandItemInstance;
            set
            {
                _leftHandItemInstance = value;
                SetupHands();
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
                SetupHands();
            }
        }

        private struct WeaponArmBone
        {
            internal Transform Weapon, Player, Avatar, PlayerHand, AvatarHand;
        }

        private readonly List<WeaponArmBone> _weaponArmBones = new List<WeaponArmBone>();

        private void SetupHands()
        {
            _weaponArmBones.Clear();
            _leftHandItemInstanceTransform = null;
            _rightHandItemInstanceTransform = null;
            _vrmLeftMiddleFinger = null;
            _vrmRightMiddleFinger = null;
            if (_vrmGoAnimator == null) return;

            // These weapons have a rig containing both hands; ordinary weapons follow
            // the reparented sockets without any per-frame bone adjustment.
            GameObject rig = GameItem.IsSpecialCase(RightHandItemInstanceName) ? _rightHandItemInstance : null;
            if (rig == null && GameItem.IsSpecialCase(LeftHandItemInstanceName)) rig = _leftHandItemInstance;
            if (rig == null) return;
            _leftHandItemInstanceTransform = BoneLookup.Find(rig.transform, HumanBodyBones.LeftHand);
            _rightHandItemInstanceTransform = BoneLookup.Find(rig.transform, HumanBodyBones.RightHand);
            // FistGold's guards are skinned to LeftForeArm/RightForeArm, not to extra sockets.
            // Bind present arm bones once when equipment changes; hands retain their palm correction below.
            foreach (var bone in new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
            {
                var hand = bone == HumanBodyBones.LeftLowerArm ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                var weaponBone = BoneLookup.Find(rig.transform, bone);
                var playerBone = BoneLookup.Get(_playerAnimator, bone);
                var avatarBone = BoneLookup.Get(_vrmGoAnimator, bone);
                var playerHand = BoneLookup.Get(_playerAnimator, hand);
                var avatarHand = BoneLookup.Get(_vrmGoAnimator, hand);
                if (weaponBone != null && playerBone != null && avatarBone != null && playerHand != null &&
                    avatarHand != null)
                    _weaponArmBones.Add(new WeaponArmBone
                    {
                        Weapon = weaponBone,
                        Player = playerBone,
                        Avatar = avatarBone,
                        PlayerHand = playerHand,
                        AvatarHand = avatarHand
                    });
            }

            _vrmLeftMiddleFinger = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _vrmRightMiddleFinger = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        }

        // State variables for item names
        public string LeftHandItemInstanceName { get; set; }

        public string RightHandItemInstanceName { get; set; }

        private Transform _vrmLeftHandTransform;
        private Transform _vrmRightHandTransform;
        private Transform _playerLeftHandTransform;
        private Transform _playerRightHandTransform;

        private Player _player;
        private VrmInstance _vrmInstance;

        internal RigOffsets Offsets { get; private set; }

        private Animator _playerAnimator;
        private Animator _vrmGoAnimator;
        private GameObject _vrmGo;
        private VrmSettings _vrmSettings;

        private HumanPose _humanPose = new HumanPose();
        private HumanPoseHandler _playerPoseHandler, _vrmPoseHandler;

        private VisEquipment _visEquipment;
        private readonly FixedRotationSmoother _turnSmoothing = new FixedRotationSmoother();
        private Quaternion _visualRotationOffset;
        private bool _rotationOffsetCaptured;

        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance)
        {
            _player = player;
            _playerAnimator = playerAnimator;
            _vrmInstance = vrmInstance;

            _vrmSettings = vrmInstance.GetSettings();

            _vrmGo = vrmInstance.GetGameObject();
            _vrmGoAnimator = _vrmGo.GetComponent<Animator>();
            if (!_rotationOffsetCaptured)
            {
                _visualRotationOffset =
                    Quaternion.Inverse(_playerAnimator.transform.rotation) * _vrmGo.transform.rotation;
                _rotationOffsetCaptured = true;
            }

            _turnSmoothing.Reset();
            // this is attached to vrmGo, this the below is the same as above, but the above is more clear.
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
            }

            CreatePoseHandlers();

            if (_visEquipment != null)
            {
                SetupAttachPoints();
                StartupGetItems();
            }

            if (!Instances.Contains(this)) Instances.Add(this);
        }

        internal void StartupGetItems()
        {
            // Read both slots before calculating offsets. Setters run on individual
            // equipment updates, but initialization must not observe half-old state.
            LeftHandItemInstanceName = _visEquipment.GetEquippedItemName("m_leftItem");
            RightHandItemInstanceName = _visEquipment.GetEquippedItemName("m_rightItem");
            _visEquipment.TryGetField<VisEquipment, GameObject>("m_leftItemInstance", out _leftHandItemInstance);
            _visEquipment.TryGetField<VisEquipment, GameObject>("m_rightItemInstance", out _rightHandItemInstance);
            SetupHands();
        }

        private void ReParentAttachPoint(Transform newParent, Transform child, Vector3? newScale, Vector3 rotation)
        {
            if (child == null || newParent == null)
            {
                Logger.LogWarning("Attachment bone or socket missing; retaining its existing transform.");
                return;
            }

            var reference = child.GetComponent<AttachmentReference>();
            if (reference == null) reference = child.gameObject.AddComponent<AttachmentReference>();
            // The original socket offset belongs to the original bone's coordinate
            // system. Preserve it once in socket-oriented world units, then map it
            // through the new bone's rotation and full parent matrix. No fixed 100x
            // conversion or hand-axis swapping is needed here.
            if (!reference.Reparent(newParent, Quaternion.Euler(rotation), _vrmSettings.PlayerVrmScale, newScale))
                Logger.LogWarning("Cannot map attachment " + child.name + "; retaining its existing transform.");
        }

        private void SetupAttachPoints()
        {
            // Keep the existing socket orientation calibration. Position and import
            // scale now come from each socket's original transform, including back slots.
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand),
                _visEquipment.m_leftHand,
                null,
                new Vector3(0, 0, -180));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand),
                _visEquipment.m_rightHand,
                null,
                new Vector3(0, 0, 0));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head),
                _visEquipment.m_helmet,
                null,
                new Vector3(-22.286f, -90, 0));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest),
                _visEquipment.m_backShield,
                null,
                new Vector3(260.842f, -110.573f, -95.08301f));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest),
                _visEquipment.m_backMelee,
                null,
                new Vector3(123.57f, 82.526f, 86.67f));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest),
                _visEquipment.m_backTwohandedMelee,
                null,
                new Vector3(126.719f, 84.227f, 89.261f));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest),
                _visEquipment.m_backBow,
                null,
                new Vector3(-111.234f, -56.51501f, 148.21f));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest),
                _visEquipment.m_backAtgeir,
                null,
                new Vector3(-74.321f, 30.416f, -213.174f));
            ReParentAttachPoint(_vrmGoAnimator.GetBoneTransform(HumanBodyBones.Hips),
                _visEquipment.m_backTool,
                null,
                new Vector3(101.664f, -90.17902f, -179.256f));
            PlaceBackSockets();
        }

        // Vanilla clearance of each back socket behind the vanilla body's back surface, in metres
        // (measured on the game's body mesh: sockets sit 0.135–0.258 m behind Spine1, the skin
        // 0.107 m behind it). Scaling the vanilla socket offset by avatar height keeps that
        // clearance only for avatars proportioned like the vanilla body. A deep-chested avatar
        // ends up with the socket inside its back, so the socket is placed relative to the
        // avatar's own back surface instead, with the same clearance scaled by height.
        private const float ShieldClearance = 0.151f,
            OneHandedClearance = 0.112f,
            TwoHandedClearance = 0.087f,
            BowClearance = 0.028f,
            AtgeirClearance = 0.089f;

        private void PlaceBackSockets()
        {
            var chest = _vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) return;
            float torsoBack = Utils.GetTorsoBackDepth(_vrmGo, _vrmGoAnimator, chest);
            if (torsoBack <= 0f) return;
            var backward = -_vrmGo.transform.forward;
            backward.y = 0;
            if (backward.sqrMagnitude < 1e-6f) return;
            backward.Normalize();
            var chestRest = chest.position;
            float ratio = _vrmSettings.PlayerVrmScale;
            float moved = 0f;
            foreach (var pair in new[]
                     {
                         (_visEquipment.m_backShield, ShieldClearance),
                         (_visEquipment.m_backMelee, OneHandedClearance),
                         (_visEquipment.m_backTwohandedMelee, TwoHandedClearance),
                         (_visEquipment.m_backBow, BowClearance), (_visEquipment.m_backAtgeir, AtgeirClearance)
                     })
            {
                var socket = pair.Item1;
                if (socket == null || socket.parent != chest) continue;
                float current = Vector3.Dot(socket.position - chestRest, backward);
                float desired = torsoBack + pair.Item2 * ratio;
                socket.position += backward * (desired - current);
                moved = Mathf.Max(moved, Mathf.Abs(desired - current));
            }

            if (Settings.LogLoadTiming)
                Logger.Log("Back sockets: torso extends " + torsoBack.ToString("F3") +
                    "m behind the chest; largest socket move " + moved.ToString("F3") + "m");
        }

        public Animator GetPlayerAnimator()
        {
            return _playerAnimator;
        }

        private void CreatePoseHandlers()
        {
            OnDestroy();
            _playerPoseHandler = new HumanPoseHandler(_playerAnimator.avatar, _playerAnimator.transform);
            _vrmPoseHandler = new HumanPoseHandler(_vrmGoAnimator.avatar, _vrmGoAnimator.transform);
        }

        public void CustomFixedUpdate(float deltaTime) { }

        public void CustomUpdate(float deltaTime, float time) { }

        public void CustomLateUpdate(float deltaTime)
        {
            if (!isActiveAndEnabled) return;
            if (_playerPoseHandler == null || _vrmPoseHandler == null || _playerAnimator == null ||
                _vrmGoAnimator == null)
                return;
            _playerPoseHandler.GetHumanPose(ref _humanPose);
            UpdateVisualRotation();
            _vrmPoseHandler.SetHumanPose(ref _humanPose);
            // Both rigs now show the same pose: record the per-bone offsets the corpse will need.
            if (Offsets == null) Offsets = RigOffsets.Capture(_playerAnimator, _vrmGoAnimator);
            if (_vrmInstance != null && !_vrmInstance.DeathSeen && _player != null && _player.IsDead())
            {
                _vrmInstance.DeathSeen = true;
                // Another player's dead object may still be moved by the network before it is
                // destroyed. Stop following it now; the corpse claims the model when it arrives.
                if (_player != Player.m_localPlayer && VrmController.ParkDeadRemote(_player, _vrmInstance)) return;
            }

            // Evaluate grip deltas after pose transfer. Startup can have the vanilla
            // rig animated while the newly imported VRM is still in its rest pose.
            // Keep the special weapon rig aligned with the same rendered turn.
            var visualTurnCorrection = _vrmGo.transform.rotation *
                Quaternion.Inverse(_playerAnimator.transform.rotation * _visualRotationOffset);
            // Move forearms before their child hands; then apply the existing hand/palm grip math.
            // The rig bone keeps the vanilla bone's axis convention, but its elbow-to-wrist
            // direction has to follow the avatar's forearm. Copying only the vanilla rotation
            // points the guard along the vanilla arm while the hand sits on the VRM wrist.
            foreach (var bone in _weaponArmBones)
            {
                if (bone.Weapon == null || bone.Player == null || bone.Avatar == null ||
                    bone.PlayerHand == null || bone.AvatarHand == null)
                    continue;
                var rotation = visualTurnCorrection * bone.Player.rotation;
                var vanillaForearm = visualTurnCorrection * (bone.PlayerHand.position - bone.Player.position);
                var avatarForearm = bone.AvatarHand.position - bone.Avatar.position;
                if (vanillaForearm.sqrMagnitude > 1e-8f && avatarForearm.sqrMagnitude > 1e-8f)
                    rotation = Quaternion.FromToRotation(vanillaForearm, avatarForearm) * rotation;
                bone.Weapon.SetPositionAndRotation(bone.Avatar.position, rotation);
            }

            var adjustment = new Vector3(0, -0.06f, 0.04f) * (1f - _vrmSettings.PlayerVrmScale);
            if (_leftHandItemInstanceTransform != null && _playerLeftHandTransform != null &&
                _vrmLeftHandTransform != null)
            {
                var rotation = visualTurnCorrection * _playerLeftHandTransform.rotation;
                // Move the palm midpoint 10% of the remaining distance toward the
                // middle-finger base (55% along hand -> finger). Keep the existing
                // one-tenth grip factor; missing finger bones use the hand origin.
                var leftPalmCenter = _vrmLeftMiddleFinger != null
                    ? Vector3.Lerp(_vrmLeftHandTransform.position, _vrmLeftMiddleFinger.position, 0.55f)
                    : _vrmLeftHandTransform.position;
                var delta = (leftPalmCenter - _vrmLeftHandTransform.position) / 10f;
                var offset = AttachmentMath.ToBoneOffset(AttachmentTransforms.Rotation(rotation),
                    AttachmentTransforms.Vector(delta),
                    AttachmentTransforms.Vector(adjustment));
                var position = AttachmentMath.PlaceBone(AttachmentTransforms.Vector(_vrmLeftHandTransform.position),
                    AttachmentTransforms.Rotation(rotation),
                    offset);
                _leftHandItemInstanceTransform.SetPositionAndRotation(AttachmentTransforms.Vector(position), rotation);
            }

            if (_rightHandItemInstanceTransform != null && _playerRightHandTransform != null &&
                _vrmRightHandTransform != null)
            {
                var rotation = visualTurnCorrection * _playerRightHandTransform.rotation;
                var rightPalmCenter = _vrmRightMiddleFinger != null
                    ? Vector3.Lerp(_vrmRightHandTransform.position, _vrmRightMiddleFinger.position, 0.55f)
                    : _vrmRightHandTransform.position;
                // Preserve the opposite sign used for the right-hand weapon rig.
                var delta = -(rightPalmCenter - _vrmRightHandTransform.position) / 10f;
                var offset = AttachmentMath.ToBoneOffset(AttachmentTransforms.Rotation(rotation),
                    AttachmentTransforms.Vector(delta),
                    AttachmentTransforms.Vector(adjustment));
                var position = AttachmentMath.PlaceBone(AttachmentTransforms.Vector(_vrmRightHandTransform.position),
                    AttachmentTransforms.Rotation(rotation),
                    offset);
                _rightHandItemInstanceTransform.SetPositionAndRotation(AttachmentTransforms.Vector(position), rotation);
            }
        }

        private void UpdateVisualRotation()
        {
            var target = _playerAnimator.transform.rotation * _visualRotationOffset;
            if (!Settings.SmoothAvatarTurning || _playerAnimator.updateMode != AnimatorUpdateMode.Fixed ||
                _player.IsAttached())
            {
                _turnSmoothing.Reset();
                _vrmGo.transform.rotation = target;
                return;
            }

            var rotation = _turnSmoothing.Sample(AttachmentTransforms.Rotation(target),
                AttachmentTransforms.Vector(_player.transform.position),
                Time.fixedTime,
                Time.time,
                Time.fixedDeltaTime);
            _vrmGo.transform.rotation =
                new Quaternion(rotation.value.x, rotation.value.y, rotation.value.z, rotation.value.w);
        }

        private void OnEnable()
        {
            if (_playerPoseHandler != null && !Instances.Contains(this)) Instances.Add(this);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }

        void OnDestroy()
        {
            Instances.Remove(this);

            if (_playerPoseHandler != null)
            {
                _playerPoseHandler.Dispose();
                _playerPoseHandler = null;
            }

            if (_vrmPoseHandler != null)
            {
                _vrmPoseHandler.Dispose();
                _vrmPoseHandler = null;
            }
        }
    }
}
