using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class BoneTransforms
    {
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
            { Bones.Head, new List<string> { "Head", "head", "Head" } },
            { Bones.LeftEye, new List<string> { "LeftEye", "leftEye", "eye.L" } },
            { Bones.RightEye, new List<string> { "RightEye", "rightEye", "eye.R" } },
            { Bones.Jaw, new List<string> { "Jaw", "jaw", "Jaw" } },
            { Bones.Neck, new List<string> { "Neck", "neck", "Neck" } },
            { Bones.UpperChest, new List<string> { "UpperChest", "ChestRoot", "ChestUp" } },
            { Bones.Chest, new List<string> { "Chest", "chest", "Chest" } },
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
            { Bones.RightToes, new List<string> { "RightToeBase", "RightToeBase_end" } }
        };


        public static void CopyBoneTransforms(Animator playerAnimator, Animator vrmAnimator)
        {
            playerAnimator.enabled = false;
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                Transform sourceBone = vrmAnimator.GetBoneTransform(bone);
                Transform targetBone = playerAnimator.GetBoneTransform(bone);

                if (sourceBone != null && targetBone != null)
                {
                    targetBone.localPosition = sourceBone.localPosition;
                    targetBone.localRotation = sourceBone.localRotation;
                    targetBone.localScale = sourceBone.localScale;
                }
            }

            playerAnimator.enabled = true;
        }

        public static void SetupBones(Player player, GameObject vrmGo)
        {
            var smrs = vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer vrmSmrBody = null;
            foreach (var smr in smrs)
            {
                if (smr.name == "Body")
                {
                    vrmSmrBody = smr;
                    break;
                }
            }

            if (vrmSmrBody != null)
            {
                var visual = player.GetField<Player, GameObject>("m_visual");
                var body = visual?.transform.Find("body")?.gameObject;

                if (body != null)
                {
                    var playerSmrBody = visual.GetComponentInChildren<SkinnedMeshRenderer>();

                    if (playerSmrBody != null)
                    {
                        MapVrmToPlayerRootBone(playerSmrBody.rootBone, vrmSmrBody.rootBone);
                        MapVrmToPlayerBones(playerSmrBody.bones, vrmSmrBody.bones);

                        Logger.Log(" ____ smr -->  " + playerSmrBody.name);
                    }
                }
            }
        }

        public static void MapVrmToPlayerBones(Transform[] playerBones, Transform[] vrmBones)
        {
            Logger.Log("vrmBones Count: " + vrmBones.Length);
            Dictionary<string, Transform> vrmBoneDictionary = new Dictionary<string, Transform>();
            foreach (var bone in vrmBones)
            {
                vrmBoneDictionary[bone.name] = bone;
            }

            foreach (var playerBoneEntry in playerBoneMap)
            {
                Bones boneEnum = playerBoneEntry.Key;
                foreach (var playerBoneName in playerBoneEntry.Value)
                {
                    bool boneMapped = false;
                    foreach (var vrmBoneName in vrmBoneMap[boneEnum])
                    {
                        if (vrmBoneDictionary.TryGetValue(vrmBoneName, out var vrmBone))
                        {
                            foreach (var playerBone in playerBones)
                            {
                                if (playerBone.name == playerBoneName)
                                {
                                    ApplyBoneTransform(playerBone, vrmBone);
                                    Logger.Log($"Mapped player bone '{playerBoneName}' to VRM bone '{vrmBoneName}'");
                                    boneMapped = true;
                                    break;
                                }
                            }

                            if (boneMapped)
                            {
                                break;
                            }
                        }
                    }

                    if (!boneMapped)
                    {
                        Logger.Log($"No VRM bone found for player bone '{playerBoneName}'");
                    }
                }
            }
        }

        public static void MapVrmToPlayerRootBone(Transform playerRootBone, Transform vrmRootBone)
        {
            if (playerRootBone != null && vrmRootBone != null)
            {
                ApplyBoneTransform(playerRootBone, vrmRootBone);
                Logger.Log($"Mapped player root bone '{playerRootBone.name}' to VRM root bone '{vrmRootBone.name}' {vrmRootBone.localScale}");
            }
            else
            {
                Logger.Log("Root bone mapping failed due to null reference.");
            }
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