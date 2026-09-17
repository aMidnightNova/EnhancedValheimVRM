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

        private GameObject _leftHandItemInstance;

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

        public GameObject RightHandItemInstance
        {
            get => _rightHandItemInstance;
            set
            {
                _rightHandItemInstance = value;
                SetupHands();
            }
        }

        // rigged weapon waiting to move onto the avatar, done after the next pose copy
        private GameObject _pendingRig;
        private string _rigItemName;

        private void SetupHands()
        {
            _pendingRig = null;
            if (_vrmGoAnimator == null) return;
            var name = RightHandItemInstanceName;
            var rig = GameItem.IsSpecialCase(name) ? _rightHandItemInstance : null;
            if (rig == null && GameItem.IsSpecialCase(LeftHandItemInstanceName))
            {
                name = LeftHandItemInstanceName;
                rig = _leftHandItemInstance;
            }

            if (rig == null) return;
            var pieces = rig.GetComponent<RiggedItemPieces>();
            if (pieces != null && pieces.Avatar == _vrmGoAnimator) return;
            _pendingRig = rig;
            _rigItemName = name;
        }

        // State variables for item names
        public string LeftHandItemInstanceName { get; set; }

        public string RightHandItemInstanceName { get; set; }

        private Transform _vrmLeftHandTransform;
        private Transform _vrmRightHandTransform;
        private Transform _playerLeftHandTransform;
        private Transform _playerRightHandTransform;
        private Transform _playerHips;
        private Transform _vrmHips;
        private Transform _playerLeftKnee;
        private Transform _playerRightKnee;
        private Transform _vrmLeftKnee;
        private Transform _vrmRightKnee;

        private Player _player;
        private VrmInstance _vrmInstance;

        internal RigOffsets Offsets { get; private set; }

        private Animator _playerAnimator;
        private Animator _vrmGoAnimator;
        private GameObject _vrmGo;
        private VrmSettings _vrmSettings;

        private HumanPose _humanPose = new HumanPose();
        private HumanPoseHandler _playerPoseHandler, _vrmPoseHandler;
        private static readonly int SittingTag = ZSyncAnimation.GetHash("sitting");

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
            _playerHips = BoneLookup.Get(_playerAnimator, HumanBodyBones.Hips);
            _vrmHips = BoneLookup.Get(_vrmGoAnimator, HumanBodyBones.Hips);
            _playerLeftKnee = BoneLookup.Get(_playerAnimator, HumanBodyBones.LeftLowerLeg);
            _playerRightKnee = BoneLookup.Get(_playerAnimator, HumanBodyBones.RightLowerLeg);
            _vrmLeftKnee = BoneLookup.Get(_vrmGoAnimator, HumanBodyBones.LeftLowerLeg);
            _vrmRightKnee = BoneLookup.Get(_vrmGoAnimator, HumanBodyBones.RightLowerLeg);

            if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
                _visEquipment = visEquipment;

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
            var torsoBack = Utils.GetTorsoBackDepth(_vrmGo, _vrmGoAnimator, chest);
            if (torsoBack <= 0f) return;
            var backward = -_vrmGo.transform.forward;
            backward.y = 0;
            if (backward.sqrMagnitude < 1e-6f) return;
            backward.Normalize();
            var chestRest = chest.position;
            var ratio = _vrmSettings.PlayerVrmScale;
            var moved = 0f;
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
                var current = Vector3.Dot(socket.position - chestRest, backward);
                var desired = torsoBack + pair.Item2 * ratio;
                socket.position += backward * (desired - current);
                moved = Mathf.Max(moved, Mathf.Abs(desired - current));
            }

            if (Settings.LogLoadTiming)
            {
                Logger.Log("Back sockets: torso extends " + torsoBack.ToString("F3") +
                    "m behind the chest; largest socket move " + moved.ToString("F3") + "m");
            }
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
            UpdateSeatedPosition();
            if (_vrmInstance != null && !_vrmInstance.DeathSeen && _player != null && _player.IsDead())
            {
                _vrmInstance.DeathSeen = true;
                // Another player's dead object may still be moved by the network before it is
                // destroyed. Stop following it now; the corpse claims the model when it arrives.
                if (_player != Player.m_localPlayer && VrmController.ParkDeadRemote(_player, _vrmInstance)) return;
            }

            if (_pendingRig != null)
            {
                MoveRigOntoAvatar(_pendingRig);
                _pendingRig = null;
            }
        }

        // rebinds a rigged weapon to its own skeleton, then parents its hands to the avatars hand sockets and
        // its forearms to the avatars forearms.
        private void MoveRigOntoAvatar(GameObject rig)
        {
            var body = _visEquipment != null ? _visEquipment.m_bodyModel : null;
            if (body == null || _playerLeftHandTransform == null || _playerRightHandTransform == null) return;
            // rigged weapons use the bodys bone names in the bodys order
            var own = new Dictionary<string, Transform>();
            var pieces = rig.GetComponent<RiggedItemPieces>() ?? rig.AddComponent<RiggedItemPieces>();
            // already moved pieces are no longer under the weapon
            foreach (var moved in pieces.Moved)
            {
                if (moved == null) continue;
                foreach (var bone in moved.GetComponentsInChildren<Transform>(true)) own[bone.name] = bone;
            }

            foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
            {
                if (!own.ContainsKey(bone.name)) own[bone.name] = bone;
            }

            var bodyBones = body.bones;
            foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.bones.Length != bodyBones.Length) continue;
                var bones = new Transform[bodyBones.Length];
                for (var i = 0; i < bones.Length; i++)
                    bones[i] = bodyBones[i] != null && own.TryGetValue(bodyBones[i].name, out var mine)
                        ? mine
                        : bodyBones[i];
                renderer.bones = bones;
                if (body.rootBone != null && own.TryGetValue(body.rootBone.name, out var root))
                    renderer.rootBone = root;
                // its pieces end up far from where the prefab put them
                renderer.updateWhenOffscreen = true;
            }

            pieces.Avatar = _vrmGoAnimator;
            // forearms first, the hands are their children in the weapons skeleton
            var turn = _vrmGo.transform.rotation *
                Quaternion.Inverse(_playerAnimator.transform.rotation * _visualRotationOffset);
            MoveForearm(own, pieces, turn, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "LeftForeArm");
            MoveForearm(own, pieces, turn, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "RightForeArm");
            MoveHand(own, pieces, _playerLeftHandTransform, _visEquipment.m_leftHand, "LeftHand");
            MoveHand(own, pieces, _playerRightHandTransform, _visEquipment.m_rightHand, "RightHand");
        }

        // forearms have no socket, the guard goes on the avatars forearm bone pointed elbow to wrist
        private void MoveForearm(Dictionary<string, Transform> own,
            RiggedItemPieces pieces,
            Quaternion turn,
            HumanBodyBones bone,
            HumanBodyBones hand,
            string part)
        {
            var player = BoneLookup.Get(_playerAnimator, bone);
            var avatar = BoneLookup.Get(_vrmGoAnimator, bone);
            var playerHand = BoneLookup.Get(_playerAnimator, hand);
            var avatarHand = BoneLookup.Get(_vrmGoAnimator, hand);
            if (player == null || avatar == null || playerHand == null || avatarHand == null ||
                !own.TryGetValue(player.name, out var piece))
                return;
            var rotation = turn * player.rotation;
            var vanillaForearm = turn * (playerHand.position - player.position);
            var avatarForearm = avatarHand.position - avatar.position;
            if (vanillaForearm.sqrMagnitude > 1e-8f && avatarForearm.sqrMagnitude > 1e-8f)
                rotation = Quaternion.FromToRotation(vanillaForearm, avatarForearm) * rotation;
            var position = avatar.position;
            if (_vrmSettings.TryGetRigOffset(_rigItemName, part, out var pos, out var rot))
            {
                // spins around the middle of the forearm, not the elbow
                var pivot = (avatar.position + avatarHand.position) * 0.5f;
                var turned = rotation * Quaternion.Euler(rot);
                position = pivot + turned * (Quaternion.Inverse(rotation) * (position - pivot)) +
                    rotation * (pos * _vrmSettings.PlayerVrmScale);
                rotation = turned;
            }

            piece.SetPositionAndRotation(position, rotation);
            piece.SetParent(avatar, true);
            if (!pieces.Moved.Contains(piece)) pieces.Moved.Add(piece);
        }

        // a hand piece goes on the avatars hand socket where the vanilla hand sits from its socket, so it keeps
        // the games grip, scaled around it like a normal weapon
        private void MoveHand(Dictionary<string, Transform> own,
            RiggedItemPieces pieces,
            Transform player,
            Transform socket,
            string part)
        {
            if (socket == null || !own.TryGetValue(player.name, out var piece) ||
                !VrmController.TryGetVanillaSocket(_player, socket, out var at, out var turn, out var size) ||
                size.x == 0 || size.y == 0 || size.z == 0)
                return;
            var ratio = _vrmSettings.PlayerVrmScale * _vrmSettings.GetWeaponScale(_rigItemName);
            var inverse = Quaternion.Inverse(turn);
            var shrink = new Vector3(1f / size.x, 1f / size.y, 1f / size.z);
            var localPosition = -Vector3.Scale(inverse * at, shrink) * ratio;
            var localRotation = inverse;
            if (_vrmSettings.TryGetRigOffset(_rigItemName, part, out var pos, out var rot))
            {
                // Rot spins it around the grip, Pos moves it along the sockets axes in meters
                var spin = Quaternion.Euler(rot);
                localRotation = spin * localRotation;
                localPosition = spin * localPosition +
                    socket.InverseTransformVector(socket.TransformDirection(pos) * _vrmSettings.PlayerVrmScale);
            }

            piece.SetParent(socket, false);
            piece.SetLocalPositionAndRotation(localPosition, localRotation);
            piece.localScale = shrink * ratio;
            if (!pieces.Moved.Contains(piece)) pieces.Moved.Add(piece);
        }

        private static bool _physicsPoseInstalled;

        // /vrm dev physics on | off. not saved
        internal static bool PoseAvatarBeforePhysics = true;

        // poses the avatar in the physics step too, like the game does its own skeleton, right before the simulation
        internal static void InstallPhysicsPose()
        {
            if (_physicsPoseInstalled) return;
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            if (!FrameClock.AddBefore(ref loop,
                    typeof(UnityEngine.PlayerLoop.FixedUpdate),
                    typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate),
                    CopyPosesForPhysics,
                    typeof(VrmAnimator)))
            {
                Logger.LogWarning(
                    "Could not hook the physics step, items that swing from the avatar may lag behind it.");
                return;
            }

            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            _physicsPoseInstalled = true;
        }

        private static void CopyPosesForPhysics()
        {
            if (!PoseAvatarBeforePhysics) return;
            for (var i = 0; i < Instances.Count; i++)
            {
                if (!(Instances[i] is VrmAnimator animator)) continue;
                try
                {
                    animator.CopyPoseForPhysics();
                }
                catch (Exception error)
                {
                    Logger.LogOnce("physics-pose:" + error.GetType().Name,
                        "Avatar pose for the physics step failed: " + error.Message);
                }
            }
        }

        // the frame copy without turn smoothing, that is only for drawing
        private void CopyPoseForPhysics()
        {
            if (!isActiveAndEnabled || _playerPoseHandler == null || _vrmPoseHandler == null ||
                _playerAnimator == null || _vrmGoAnimator == null || (_vrmInstance != null && _vrmInstance.DeathSeen))
                return;
            _playerPoseHandler.GetHumanPose(ref _humanPose);
            _vrmGo.transform.rotation = _playerAnimator.transform.rotation * _visualRotationOffset;
            _vrmPoseHandler.SetHumanPose(ref _humanPose);
            UpdateSeatedPosition();
        }

        private void UpdateSeatedPosition()
        {
            // ground sit is an emote, skip it
            if (_player == null || _vrmInstance == null || _playerHips == null || _vrmHips == null ||
                _player.InEmote() || _player.InBed() || _player.IsDead())
                return;

            // IsSitting is false during the blend in, catch that too
            if (!_player.IsSitting() && !(_playerAnimator.IsInTransition(0) &&
                    _playerAnimator.GetNextAnimatorStateInfo(0).tagHash == SittingTag))
                return;

            // works in the seats axes so ships and turned chairs are fine. SetHumanPose resets the
            // hips every frame before this so it cant stack
            var vanillaHips = AttachmentTransforms.Vector(_playerHips.position);
            var avatarHips = AttachmentTransforms.Vector(_vrmHips.position);
            // no knees, fall back to hips
            var vanillaKnees = vanillaHips;
            var avatarKnees = avatarHips;
            if (_playerLeftKnee != null && _playerRightKnee != null && _vrmLeftKnee != null && _vrmRightKnee != null)
            {
                vanillaKnees = (AttachmentTransforms.Vector(_playerLeftKnee.position) +
                    AttachmentTransforms.Vector(_playerRightKnee.position)) * 0.5f;
                avatarKnees = (AttachmentTransforms.Vector(_vrmLeftKnee.position) +
                    AttachmentTransforms.Vector(_vrmRightKnee.position)) * 0.5f;
            }

            var correction = AttachmentMath.SeatedProportionCorrection(vanillaHips,
                avatarHips,
                vanillaKnees,
                avatarKnees,
                AttachmentTransforms.Vector(_player.transform.position),
                AttachmentTransforms.Rotation(_player.transform.rotation),
                AttachmentTransforms.Vector(_vrmInstance.SeatProportions),
                _vrmSettings.PlayerVrmScale > 1);
            _vrmHips.position = AttachmentTransforms.Vector(AttachmentMath.PlaceBone(
                AttachmentTransforms.Vector(_vrmHips.position) + correction,
                AttachmentTransforms.Rotation(_player.transform.rotation),
                AttachmentTransforms.Vector(_vrmSettings.SittingOnChairOffset) * _vrmSettings.PlayerVrmScale));
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

        private void OnDestroy()
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
