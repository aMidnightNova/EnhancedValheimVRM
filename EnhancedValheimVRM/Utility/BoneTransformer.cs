using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class BoneTransformer
    {
        public Dictionary<string, Transform> PlayerBoneToVrmBone { get; set; }
        public Dictionary<string, Transform> VrmBoneToPlayerBone { get; set; }
        public Transform PlayerRootBone { get; set; }
        public Transform VrmRootBone { get; set; }

        public Dictionary<HumanBodyBones, Transform> HumanBodyBoneToPlayerBone { get; set; }
        public Dictionary<HumanBodyBones, Transform> HumanBodyBoneToVrmBone { get; set; }

        private Dictionary<string, Transform> PlayerBoneDict { get; set; }
        private Dictionary<string, Transform> VrmBoneDict { get; set; }

        private Transform[] PlayerBones { get; set; }
        private Transform[] VrmBones { get; set; }

        public enum Bones
        {
            Head,
            LeftEye,
            RightEye,
            Jaw,
            Neck,
            UpperChest,
            Chest,
            Spine,
            Hips,
            LeftShoulder,
            LeftUpperArm,
            LeftLowerArm,
            LeftHand,
            LeftThumbProximal,
            LeftThumbIntermediate,
            LeftThumbDistal,
            LeftIndexProximal,
            LeftIndexIntermediate,
            LeftIndexDistal,
            LeftMiddleProximal,
            LeftMiddleIntermediate,
            LeftMiddleDistal,
            LeftRingProximal,
            LeftRingIntermediate,
            LeftRingDistal,
            LeftLittleProximal,
            LeftLittleIntermediate,
            LeftLittleDistal,
            RightShoulder,
            RightUpperArm,
            RightLowerArm,
            RightHand,
            RightThumbProximal,
            RightThumbIntermediate,
            RightThumbDistal,
            RightIndexProximal,
            RightIndexIntermediate,
            RightIndexDistal,
            RightMiddleProximal,
            RightMiddleIntermediate,
            RightMiddleDistal,
            RightRingProximal,
            RightRingIntermediate,
            RightRingDistal,
            RightLittleProximal,
            RightLittleIntermediate,
            RightLittleDistal,
            LeftUpperLeg,
            LeftLowerLeg,
            RightUpperLeg,
            RightLowerLeg,
            LeftFoot,
            RightFoot,
            LeftToes,
            LeftHallux,
            LeftToe2,
            LeftToe3,
            LeftToe4,
            LeftToe5,
            RightToes,
            RightHallux,
            RightToe2,
            RightToe3,
            RightToe4,
            RightToe5
        }

        public static Dictionary<Bones, List<string>> vrmBoneMap = new Dictionary<Bones, List<string>>
        {
            { Bones.Head, new List<string> { "Head", "head" } },
            { Bones.LeftEye, new List<string> { "LeftEye", "leftEye", "eye.L" } },
            { Bones.RightEye, new List<string> { "RightEye", "rightEye", "eye.R" } },
            { Bones.Jaw, new List<string> { "Jaw", "jaw", "Jaw" } },
            { Bones.Neck, new List<string> { "Neck", "neck", "Neck" } },
            { Bones.UpperChest, new List<string> { "UpperChest", "ChestRoot", "ChestUp" } },
            { Bones.Chest, new List<string> { "Chest", "chest" } },
            { Bones.Spine, new List<string> { "Spine", "spine" } },
            { Bones.Hips, new List<string> { "Hips", "Pelvis" } },
            { Bones.LeftShoulder, new List<string> { "LeftShoulder", "leftShoulder", "shoulder.L", "Left shoulder" } },
            { Bones.LeftUpperArm, new List<string> { "LeftUpperArm", "leftUpperArm", "upper_arm.L", "Left arm" } },
            { Bones.LeftLowerArm, new List<string> { "LeftLowerArm", "leftLowerArm", "forearm.L", "Left elbow" } },
            { Bones.LeftHand, new List<string> { "LeftHand", "leftHand", "hand.L", "Left wrist" } },
            { Bones.LeftThumbProximal, new List<string> { "LeftThumbProximal", "thumb.01.L", "ThumbFinger1_L" } },
            { Bones.LeftThumbIntermediate, new List<string> { "LeftThumbIntermediate", "thumb.02.L" } },
            { Bones.LeftThumbDistal, new List<string> { "LeftThumbDistal", "thumb.03.L" } },
            { Bones.LeftIndexProximal, new List<string> { "LeftIndexProximal", "f_index.01.L", "IndexFinger1_L" } },
            { Bones.LeftIndexIntermediate, new List<string> { "LeftIndexIntermediate", "f_index.02.L" } },
            { Bones.LeftIndexDistal, new List<string> { "LeftIndexDistal", "f_index.03.L" } },
            { Bones.LeftMiddleProximal, new List<string> { "LeftMiddleProximal", "f_middle.01.L", "MiddleFinger1_L" } },
            { Bones.LeftMiddleIntermediate, new List<string> { "LeftMiddleIntermediate", "f_middle.02.L" } },
            { Bones.LeftMiddleDistal, new List<string> { "LeftMiddleDistal", "f_middle.03.L" } },
            { Bones.LeftRingProximal, new List<string> { "LeftRingProximal", "f_ring.01.L", "RingFinger1_L" } },
            { Bones.LeftRingIntermediate, new List<string> { "LeftRingIntermediate", "f_ring.02.L" } },
            { Bones.LeftRingDistal, new List<string> { "LeftRingDistal", "f_ring.03.L" } },
            { Bones.LeftLittleProximal, new List<string> { "LeftLittleProximal", "f_pinky.01.L", "PinkyFinger1_L" } },
            { Bones.LeftLittleIntermediate, new List<string> { "LeftLittleIntermediate", "f_pinky.02.L" } },
            { Bones.LeftLittleDistal, new List<string> { "LeftLittleDistal", "f_pinky.03.L" } },
            { Bones.RightShoulder, new List<string> { "RightShoulder", "rightShoulder", "shoulder.R", "Right shoulder" } },
            { Bones.RightUpperArm, new List<string> { "RightUpperArm", "rightUpperArm", "upper_arm.R", "Right arm" } },
            { Bones.RightLowerArm, new List<string> { "RightLowerArm", "rightLowerArm", "forearm.R", "Right elbow" } },
            { Bones.RightHand, new List<string> { "RightHand", "rightHand", "hand.R", "Right wrist" } },
            { Bones.RightThumbProximal, new List<string> { "RightThumbProximal", "thumb.01.R", "ThumbFinger1_R" } },
            { Bones.RightThumbIntermediate, new List<string> { "RightThumbIntermediate", "thumb.02.R" } },
            { Bones.RightThumbDistal, new List<string> { "RightThumbDistal", "thumb.03.R" } },
            { Bones.RightIndexProximal, new List<string> { "RightIndexProximal", "f_index.01.R", "IndexFinger1_R" } },
            { Bones.RightIndexIntermediate, new List<string> { "RightIndexIntermediate", "f_index.02.R" } },
            { Bones.RightIndexDistal, new List<string> { "RightIndexDistal", "f_index.03.R" } },
            { Bones.RightMiddleProximal, new List<string> { "RightMiddleProximal", "f_middle.01.R", "MiddleFinger1_R" } },
            { Bones.RightMiddleIntermediate, new List<string> { "RightMiddleIntermediate", "f_middle.02.R" } },
            { Bones.RightMiddleDistal, new List<string> { "RightMiddleDistal", "f_middle.03.R" } },
            { Bones.RightRingProximal, new List<string> { "RightRingProximal", "f_ring.01.R", "RingFinger1_R" } },
            { Bones.RightRingIntermediate, new List<string> { "RightRingIntermediate", "f_ring.02.R" } },
            { Bones.RightRingDistal, new List<string> { "RightRingDistal", "f_ring.03.R" } },
            { Bones.RightLittleProximal, new List<string> { "RightLittleProximal", "f_pinky.01.R", "PinkyFinger1_R" } },
            { Bones.RightLittleIntermediate, new List<string> { "RightLittleIntermediate", "f_pinky.02.R" } },
            { Bones.RightLittleDistal, new List<string> { "RightLittleDistal", "f_pinky.03.R" } },
            { Bones.LeftUpperLeg, new List<string> { "LeftUpperLeg", "leftUpperLeg", "Left leg", "LeftLeg", "LeftUpLeg" } },
            { Bones.LeftLowerLeg, new List<string> { "LeftLowerLeg", "leftLowerLeg", "shin.L", "Left knee" } },
            { Bones.RightUpperLeg, new List<string> { "RightUpperLeg", "rightUpperLeg", "Right leg", "RightLeg", "RightUpLeg" } },
            { Bones.RightLowerLeg, new List<string> { "RightLowerLeg", "rightLowerLeg", "shin.R", "Right knee" } },
            { Bones.LeftFoot, new List<string> { "LeftFoot", "leftFoot", "foot.L", "Left ankle" } },
            { Bones.RightFoot, new List<string> { "RightFoot", "rightFoot", "foot.R", "Right ankle" } },
            { Bones.LeftToes, new List<string> { "LeftToes", "leftToes", "toe.L", "Left toe" } },
            { Bones.LeftHallux, new List<string> { "LeftHallux", "ToeIndex1_L", "toe.01.L" } },
            { Bones.LeftToe2, new List<string> { "LeftToe2", "ToeMid1_L", "toe.02.L" } },
            { Bones.LeftToe3, new List<string> { "LeftToe3", "ToeRing1_L", "toe.03.L" } },
            { Bones.LeftToe4, new List<string> { "LeftToe4", "ToePinky1_L", "toe.04.L" } },
            { Bones.LeftToe5, new List<string> { "LeftToe5", "ToePinky1_L" } },
            { Bones.RightToes, new List<string> { "RightToes", "righttoes", "toe.R", "Right toe" } },
            { Bones.RightHallux, new List<string> { "RightHallux", "ToeIndex1_R", "toe.01.R" } },
            { Bones.RightToe2, new List<string> { "RightToe2", "ToeMid1_R", "toe.02.R" } },
            { Bones.RightToe3, new List<string> { "RightToe3", "ToeRing1_R", "toe.03.R" } },
            { Bones.RightToe4, new List<string> { "RightToe4", "ToePinky1_R", "toe.04.R" } },
            { Bones.RightToe5, new List<string> { "RightToe5", "ToePinky1_R" } }
        };

        public static Dictionary<Bones, List<string>> playerBoneMap = new Dictionary<Bones, List<string>>
        {
            { Bones.Head, new List<string> { "Head" } },
            { Bones.LeftEye, new List<string> { "LeftEye" } },
            { Bones.RightEye, new List<string> { "RightEye" } },
            { Bones.Jaw, new List<string> { "Jaw", "Jaw_end" } },
            { Bones.Neck, new List<string> { "Neck" } },
            { Bones.UpperChest, new List<string> { "Spine2" } },
            { Bones.Chest, new List<string> { "Spine1" } },
            { Bones.Spine, new List<string> { "Spine" } },
            { Bones.Hips, new List<string> { "Hips" } },
            { Bones.LeftShoulder, new List<string> { "LeftShoulder" } },
            { Bones.LeftUpperArm, new List<string> { "LeftArm" } },
            { Bones.LeftLowerArm, new List<string> { "LeftForeArm" } },
            { Bones.LeftHand, new List<string> { "LeftHand" } },
            { Bones.LeftThumbProximal, new List<string> { "LeftHandThumb1" } },
            { Bones.LeftThumbIntermediate, new List<string> { "LeftHandThumb2" } },
            { Bones.LeftThumbDistal, new List<string> { "LeftHandThumb3" } },
            { Bones.LeftIndexProximal, new List<string> { "LeftHandIndex1" } },
            { Bones.LeftIndexIntermediate, new List<string> { "LeftHandIndex2" } },
            { Bones.LeftIndexDistal, new List<string> { "LeftHandIndex3" } },
            { Bones.LeftMiddleProximal, new List<string> { "LeftHandMiddle1" } },
            { Bones.LeftMiddleIntermediate, new List<string> { "LeftHandMiddle2" } },
            { Bones.LeftMiddleDistal, new List<string> { "LeftHandMiddle3" } },
            { Bones.LeftRingProximal, new List<string> { "LeftHandRing1" } },
            { Bones.LeftRingIntermediate, new List<string> { "LeftHandRing2" } },
            { Bones.LeftRingDistal, new List<string> { "LeftHandRing3" } },
            { Bones.LeftLittleProximal, new List<string> { "LeftHandPinky1" } },
            { Bones.LeftLittleIntermediate, new List<string> { "LeftHandPinky2" } },
            { Bones.LeftLittleDistal, new List<string> { "LeftHandPinky3" } },
            { Bones.RightShoulder, new List<string> { "RightShoulder" } },
            { Bones.RightUpperArm, new List<string> { "RightArm" } },
            { Bones.RightLowerArm, new List<string> { "RightForeArm" } },
            { Bones.RightHand, new List<string> { "RightHand" } },
            { Bones.RightThumbProximal, new List<string> { "RightHandThumb1" } },
            { Bones.RightThumbIntermediate, new List<string> { "RightHandThumb2" } },
            { Bones.RightThumbDistal, new List<string> { "RightHandThumb3" } },
            { Bones.RightIndexProximal, new List<string> { "RightHandIndex1" } },
            { Bones.RightIndexIntermediate, new List<string> { "RightHandIndex2" } },
            { Bones.RightIndexDistal, new List<string> { "RightHandIndex3" } },
            { Bones.RightMiddleProximal, new List<string> { "RightHandMiddle1" } },
            { Bones.RightMiddleIntermediate, new List<string> { "RightHandMiddle2" } },
            { Bones.RightMiddleDistal, new List<string> { "RightHandMiddle3" } },
            { Bones.RightRingProximal, new List<string> { "RightHandRing1" } },
            { Bones.RightRingIntermediate, new List<string> { "RightHandRing2" } },
            { Bones.RightRingDistal, new List<string> { "RightHandRing3" } },
            { Bones.RightLittleProximal, new List<string> { "RightHandPinky1" } },
            { Bones.RightLittleIntermediate, new List<string> { "RightHandPinky2" } },
            { Bones.RightLittleDistal, new List<string> { "RightHandPinky3" } },
            { Bones.LeftUpperLeg, new List<string> { "LeftUpLeg" } },
            { Bones.LeftLowerLeg, new List<string> { "LeftLeg" } },
            { Bones.RightUpperLeg, new List<string> { "RightUpLeg" } },
            { Bones.RightLowerLeg, new List<string> { "RightLeg" } },
            { Bones.LeftFoot, new List<string> { "LeftFoot" } },
            { Bones.RightFoot, new List<string> { "RightFoot" } },
            { Bones.LeftToes, new List<string> { "LeftToeBase" } },
            { Bones.RightToes, new List<string> { "RightToeBase" } } //RightToeBase_end
        };

        public BoneTransformer(Player player, GameObject vrmGo)
        {
            PlayerBoneToVrmBone = new Dictionary<string, Transform>();
            VrmBoneToPlayerBone = new Dictionary<string, Transform>();
            HumanBodyBoneToPlayerBone = new Dictionary<HumanBodyBones, Transform>();
            HumanBodyBoneToVrmBone = new Dictionary<HumanBodyBones, Transform>();

            // Find the VRM's SkinnedMeshRenderer for the body
            var smrs = vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer vrmSmrBody = smrs.FirstOrDefault(smr => smr.name == "Body");

            if (vrmSmrBody != null)
            {
                var visual = player.GetField<Player, GameObject>("m_visual");
                var body = visual?.transform.Find("body")?.gameObject;

                if (body != null)
                {
                    var playerSmrBody = visual.GetComponentInChildren<SkinnedMeshRenderer>();

                    if (playerSmrBody != null)
                    {
                        // Set root bones
                        PlayerRootBone = playerSmrBody.rootBone;
                        VrmRootBone = vrmSmrBody.rootBone;

                        PlayerBones = playerSmrBody.bones;
                        VrmBones = vrmSmrBody.bones;

                        // Create dictionaries to map bone names to their Transform objects
                        PlayerBoneDict = playerSmrBody.bones.ToDictionary(b => b.name, b => b);
                        VrmBoneDict = vrmSmrBody.bones.ToDictionary(b => b.name, b => b);

                        // Create a dictionary to map VRM bone names to their Transform objects
                        Dictionary<string, Transform> vrmBoneDictionary = vrmSmrBody.bones.ToDictionary(b => b.name, b => b);

                        // Populate PlayerToVrmBones and VrmToPlayerBones
                        foreach (var bone in playerSmrBody.bones)
                        {
                            foreach (var playerBoneEntry in playerBoneMap)
                            {
                                if (playerBoneEntry.Value.Contains(bone.name))
                                {
                                    Bones boneEnum = playerBoneEntry.Key;
                                    foreach (var vrmBoneName in vrmBoneMap[boneEnum])
                                    {
                                        if (vrmBoneDictionary.TryGetValue(vrmBoneName, out var vrmBone))
                                        {
                                            PlayerBoneToVrmBone[bone.name] = vrmBone;
                                            VrmBoneToPlayerBone[vrmBone.name] = bone;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        // Populate HumanBodyBoneToPlayerBone and HumanBodyBoneToVrmBone
                        foreach (HumanBodyBones humanBone in Enum.GetValues(typeof(HumanBodyBones)))
                        {
                            if (humanBone == HumanBodyBones.LastBone) continue;

                            // Convert HumanBodyBones to custom Bones enum
                            if (Enum.TryParse(humanBone.ToString(), out Bones boneEnum))
                            {
                                // Get player bone transform
                                Transform playerBoneTransform = playerSmrBody.bones.FirstOrDefault(b => playerBoneMap[boneEnum].Contains(b.name));
                                if (playerBoneTransform != null)
                                {
                                    HumanBodyBoneToPlayerBone[humanBone] = playerBoneTransform;
                                }

                                // Get VRM bone transform
                                Transform vrmBoneTransform = vrmSmrBody.bones.FirstOrDefault(b => vrmBoneMap[boneEnum].Contains(b.name));
                                if (vrmBoneTransform != null)
                                {
                                    HumanBodyBoneToVrmBone[humanBone] = vrmBoneTransform;
                                }
                            }
                        }
                    }
                }
            }
        }


        private static SkeletonBone CreateSkeletonBoneFromBone(Transform bone)
        {
            return new SkeletonBone
            {
                name = bone.name,
                position = bone.localPosition,
                rotation = bone.localRotation,
                scale = bone.localScale
            };
        }

        private static SkeletonBone CreateSkeletonBoneFromBone(Transform bone, string boneName)
        {
            return new SkeletonBone
            {
                name = boneName,
                position = bone.localPosition,
                rotation = bone.localRotation,
                scale = bone.localScale
            };
        }

        private static HumanBone CreateHumanBoneFromBone(string boneName, string humanName)
        {
            HumanBone bone = new HumanBone
            {
                boneName = boneName,
                humanName = humanName,
                limit = new HumanLimit
                {
                    useDefaultValues = true
                }
            };

            return bone;
        }

        private static SkeletonBone[] CreateSkeletonFromAvatar(GameObject avatar)
        {
            List<SkeletonBone> skeleton = new List<SkeletonBone>();

            Transform[] avatarBones = avatar.GetComponentsInChildren<Transform>();
            foreach (Transform avatarBone in avatarBones)
            {
                skeleton.Add(CreateSkeletonBoneFromBone(avatarBone));
            }

            return skeleton.ToArray();
        }


        private SkeletonBone[] CreateSkeletonBonesFromVrm()
        {
            List<SkeletonBone> skeleton = new List<SkeletonBone>();
            Dictionary<string, SkeletonBone> skeletonBoneMap = new Dictionary<string, SkeletonBone>();

            foreach (Transform vrmBone in VrmBones)
            {
                if (VrmBoneToPlayerBone.TryGetValue(vrmBone.name, out var playerBone))
                {
                    // Create the SkeletonBone using the new method
                    SkeletonBone skeletonBone = CreateSkeletonBoneFromBone(vrmBone, playerBone.name);
                    skeleton.Add(skeletonBone);
                    skeletonBoneMap[skeletonBone.name] = skeletonBone;
                }
            }

            // Now set the correct parent bones
            for (int i = 0; i < skeleton.Count; i++)
            {
                var skeletonBone = skeleton[i];

                // Use the PlayerBoneDict to find the parent relationship
                if (PlayerBoneDict.TryGetValue(skeletonBone.name, out var playerBone))
                {
                    if (playerBone.parent != null && skeletonBoneMap.TryGetValue(playerBone.parent.name, out var parentBone))
                    {
                        skeletonBone.SetField("parentName", parentBone.name);
                        skeleton[i] = skeletonBone;
                    }
                }
            }

            return skeleton.ToArray();
        }


        private SkeletonBone[] CreatePlayerNamedSkeletonBonesFromVrm(GameObject avatar)
        {
            List<SkeletonBone> skeleton = new List<SkeletonBone>();

            Transform[] vrmBones = avatar.GetComponentsInChildren<Transform>();
            foreach (Transform vrmBone in vrmBones)
            {
                if (VrmBoneToPlayerBone.TryGetValue(vrmBone.name, out var playerBone))
                {
                    skeleton.Add(CreateSkeletonBoneFromBone(vrmBone, playerBone.name));
                }
            }

            return skeleton.ToArray();
        }

        private HumanBone[] CreatePlayerNamedHumanBonesFromVrm()
        {
            List<HumanBone> human = new List<HumanBone>();


            foreach (Transform vrmBone in VrmBones)
            {
                // Try to get the HumanBodyBones enum value from the VRM bone name
                if (vrmBoneMap.Values.SelectMany(v => v).Contains(vrmBone.name))
                {
                    // Find the corresponding custom Bones enum
                    var boneEnum = vrmBoneMap.FirstOrDefault(v => v.Value.Contains(vrmBone.name)).Key;
                    if (boneEnum != default)
                    {
                        // Find the corresponding HumanBodyBones enum
                        if (Enum.TryParse(boneEnum.ToString(), out HumanBodyBones humanBodyBone))
                        {
                            // Find the corresponding player bone name
                            if (playerBoneMap.TryGetValue(boneEnum, out List<string> playerBoneNames))
                            {
                                foreach (var playerBoneName in playerBoneNames)
                                {
                                    HumanBone bone = CreateHumanBoneFromBone(humanBodyBone.ToString(), playerBoneName);

                                    human.Add(bone);
                                }
                            }
                        }
                    }
                }
            }

            return human.ToArray();
        }


        public HumanDescription CreateHumanDescription(HumanDescription humanDescription)
        {
            HumanDescription description = new HumanDescription()
            {
                armStretch = humanDescription.armStretch,
                feetSpacing = humanDescription.feetSpacing,
                hasTranslationDoF = humanDescription.hasTranslationDoF,
                legStretch = humanDescription.legStretch,
                lowerArmTwist = humanDescription.lowerArmTwist,
                lowerLegTwist = humanDescription.lowerLegTwist,
                upperArmTwist = humanDescription.upperArmTwist,
                upperLegTwist = humanDescription.upperLegTwist,
                skeleton = CreateSkeletonBonesFromVrm(),
                human = CreatePlayerNamedHumanBonesFromVrm(),
            };
            return description;
        }

        public void ResizePlayerAvatarToVrmSize(Player player)
        {
            var playerAnimator = player.GetField<Player, Animator>("m_animator");

            // Get the root GameObject of the player's bone structure
            GameObject playerRootGameObject = playerAnimator.gameObject;

            // Create the HumanDescription from the current bone mappings
            HumanDescription description = CreateHumanDescription(playerAnimator.avatar.humanDescription);

            // Build the human avatar using the root GameObject and the human description
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(playerRootGameObject, description);

            // Ensure the avatar is correctly named and assigned to the player's animator
            avatar.name = playerAnimator.avatar.name;
            playerAnimator.avatar = avatar;
        }

        //TODO: determin if anything below is worth keepign around,

        public void CopyBoneTransforms2(Player player, VrmInstance vrmInstance)
        {
            var playerAnimator = player.GetField<Player, Animator>("m_animator");
            var vrmAnimator = vrmInstance.GetAnimator();


            playerAnimator.enabled = false;
            vrmAnimator.enabled = false;

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                Transform sourceBone = vrmAnimator.GetBoneTransform(bone);
                Transform targetBone = playerAnimator.GetBoneTransform(bone);

                if (sourceBone != null && targetBone != null)
                {
                    targetBone.position = sourceBone.position;
                }
            }

            playerAnimator.Rebind();

            playerAnimator.enabled = true;
            vrmAnimator.enabled = true;
        }

        public void CopyBoneTransforms(Player player, VrmInstance vrmInstance)
        {
            var playerAnimator = player.GetField<Player, Animator>("m_animator");
            var vrmAnimator = vrmInstance.GetAnimator();


            //var visEquipment = player.GetField<Player, VisEquipment>("m_visEquipment");
            //var playerSmr = visEquipment.m_bodyModel;

            var playerSmr = playerAnimator.GetComponentInChildren<SkinnedMeshRenderer>();
            var vrmSmr = vrmAnimator.GetComponentInChildren<SkinnedMeshRenderer>();

            if (playerSmr == null || vrmSmr == null)
            {
                Logger.LogError("Skinned Mesh Renderer not found on either player or VRM.");
                return;
            }

            // Disable animators during the transformation process
            playerAnimator.enabled = false;
            vrmAnimator.enabled = false;

            HumanBodyBones[] bones = (HumanBodyBones[])Enum.GetValues(typeof(HumanBodyBones));

            var bindposes = new Matrix4x4[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                HumanBodyBones bone = bones[i];
                if (bone == HumanBodyBones.LastBone) continue;

                Transform sourceBone;
                Transform targetBone;

                var playerAnimatorBone = playerAnimator.GetBoneTransform(bone);

                // Try to get the source and target bones from the dictionaries
                if (!HumanBodyBoneToVrmBone.TryGetValue(bone, out sourceBone) || !HumanBodyBoneToPlayerBone.TryGetValue(bone, out targetBone))
                {
                    Logger.LogWarning($"Bone {bone} not found in either HumanBodyBoneToVrmBone or HumanBodyBoneToPlayerBone dictionaries.");
                    continue;
                }

                if (sourceBone != null && targetBone != null)
                {
                    Logger.Log($"sourceBone.position: {sourceBone.position}");
                    Logger.Log($"sourceBone.rotation: {sourceBone.rotation}");
                    Logger.Log($"sourceBone.localScale: {sourceBone.localScale}");

                    Logger.Log($"targetBone.position: {targetBone.position}");
                    Logger.Log($"targetBone.rotation: {targetBone.rotation}");
                    Logger.Log($"targetBone.localScale: {targetBone.localScale}");

                    targetBone.position = sourceBone.position;
                    targetBone.rotation = sourceBone.rotation;
                    targetBone.localScale = sourceBone.localScale;
                }

                // The bind pose is bone's inverse transformation matrix relative to the root.
                // bindposes[i] = targetBone.worldToLocalMatrix * sourceBone.transform.parent.localToWorldMatrix;
            }

            //playerSmr.sharedMesh.bindposes = bindposes;


            // Enable animators after the transformation process
            playerAnimator.enabled = true;
            vrmAnimator.enabled = true;

            playerAnimator.Rebind();
        }


        public void SetupBones2(Player player, GameObject vrmGo)
        {
            var playerSmrBody = player.GetField<Player, GameObject>("m_visual").GetComponentInChildren<SkinnedMeshRenderer>();
            var vrmSmrBody = vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(smr => smr.name == "Body");

            if (playerSmrBody != null && vrmSmrBody != null)
            {
                MapVrmToPlayerRootBone();
                ScalePlayerBones();
            }
        }

        public void ScalePlayerBones()
        {
            foreach (var playerBoneName in PlayerBoneToVrmBone.Keys)
            {
                var playerBone = PlayerBoneToVrmBone[playerBoneName];
                var vrmBone = VrmBoneToPlayerBone[playerBone.name];

                if (vrmBone != null)
                {
                    ApplyBoneScale(playerBone, CalculateBoneLengthScaleFactor(playerBone, vrmBone));
                    Logger.Log($"Mapped player bone '{playerBoneName}' to VRM bone '{vrmBone.name}'");
                }
                else
                {
                    Logger.Log($"No VRM bone found for player bone '{playerBoneName}'");
                }
            }
        }

        public void MapVrmToPlayerRootBone()
        {
            if (PlayerRootBone != null && VrmRootBone != null)
            {
                ApplyBoneTransform(PlayerRootBone, VrmRootBone);
                Logger.Log($"Mapped player root bone '{PlayerRootBone.name}' to VRM root bone '{VrmRootBone.name}'");
            }
            else
            {
                Logger.Log("Root bone mapping failed due to null reference.");
            }
        }

        private Dictionary<string, Transform> clonedBoneCache = new Dictionary<string, Transform>();

        public Transform CloneVrmBoneWithPlayerBoneInfo(Transform vrmBone)
        {
            if (VrmBoneToPlayerBone.TryGetValue(vrmBone.name, out Transform playerBone))
            {
                if (clonedBoneCache.TryGetValue(vrmBone.name, out Transform cachedClonedBone))
                {
                    Logger.Log($"Returning cached cloned bone for '{vrmBone.name}'");
                    return cachedClonedBone;
                }

                Logger.Log($"VRM bone found for player bone '{vrmBone.name}' p {playerBone.name}");
                Logger.Log($"vrmBone.localScale {vrmBone.localScale}");
                Logger.Log($"playerBone.localScale {playerBone.localScale}");
                Logger.Log($"-------------");

                Transform clonedBone = UnityEngine.Object.Instantiate(playerBone);
                clonedBone.position = playerBone.position;
                //clonedBone.localPosition = playerBone.localPosition;
                clonedBone.localScale = playerBone.localScale;
                //clonedBone.localRotation = vrmBone.localRotation;
                //clonedBone.rotation = vrmBone.rotation;

                clonedBone.name = playerBone.name;

                clonedBoneCache[vrmBone.name] = clonedBone;

                return clonedBone;
            }

            return null;
        }


        public Transform[] CloneVrmBonesWithPlayerBoneInfo(Transform[] vrmBones)
        {
            List<Transform> clonedBones = new List<Transform>();
            foreach (var vrmBone in vrmBones)
            {
                Transform clonedBone = CloneVrmBoneWithPlayerBoneInfo(vrmBone);
                if (clonedBone != null)
                {
                    clonedBones.Add(clonedBone);
                }
            }

            return clonedBones.ToArray();
        }

        public void RenameVrmBones(Transform[] vrmBones)
        {
            foreach (var vrmBone in vrmBones)
            {
                if (VrmBoneToPlayerBone.TryGetValue(vrmBone.name, out Transform playerBone))
                {
                    vrmBone.name = playerBone.name;
                }
            }
        }

        private static float CalculateBoneLengthScaleFactor(Transform playerBone, Transform vrmBone)
        {
            float playerBoneLength = Vector3.Distance(playerBone.position, playerBone.parent.position);
            float vrmBoneLength = Vector3.Distance(vrmBone.position, vrmBone.parent.position);

            Logger.Log($"Player Bone: {playerBone.name}, Player Parent: {playerBone.parent.name}, Player Length: {playerBoneLength}");
            Logger.Log($"VRM Bone: {vrmBone.name}, VRM Parent: {vrmBone.parent.name}, VRM Length: {vrmBoneLength}");

            if (playerBoneLength == 0)
            {
                Logger.Log($"Player bone length is zero for bone '{playerBone.name}'");
                return float.NaN;
            }

            return vrmBoneLength / playerBoneLength;
        }

        private static void ApplyBoneScale(Transform playerBone, float scaleFactor)
        {
            Vector3 newScale = playerBone.localScale * scaleFactor;
            playerBone.localScale = newScale;
        }

        private static void ApplyBoneTransform(Transform playerBone, Transform vrmBone)
        {
            Vector3 effectiveScale = GetEffectiveScale(vrmBone);
            playerBone.position = Vector3.Scale(vrmBone.position, effectiveScale);
            playerBone.rotation = vrmBone.rotation;
            playerBone.localScale = vrmBone.localScale;
        }

        private static Vector3 GetEffectiveScale(Transform bone)
        {
            Vector3 scale = bone.localScale;
            Transform current = bone.parent;

            while (current != null)
            {
                scale = Vector3.Scale(scale, current.localScale);
                current = current.parent;
            }

            return scale;
        }
    }
}