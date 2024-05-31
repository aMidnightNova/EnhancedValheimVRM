using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRM;

namespace EnhancedValheimVRM
{
    public static class VrmController
    {
        private static Dictionary<string, VrmInstance> _vrmInstances = new Dictionary<string, VrmInstance>();


        public static void AttachVrmToPlayer(Player player)
        {
            VrmInstance vrmInstance;
            var playerName = player.GetPlayerDisplayName();
            var exists = _vrmInstances.TryGetValue(playerName, out vrmInstance);
            if (!exists)
            {
                Logger.LogWarning("VRM DOES NOT EXISTS");
                try
                {
                    vrmInstance = new VrmInstance(player);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex.Message);
                }
            }
            else
            {
                Logger.Log("vrm exists", Logger.LogLevel.All);
                vrmInstance.SetPlayer(player);
            }

            if (vrmInstance != null)
            {
                if (!exists)
                {
                    Logger.Log("add vrm instance", Logger.LogLevel.Info);
                    _vrmInstances.Add(playerName, vrmInstance);
                }

                CoroutineHelper.Instance.StartCoroutine(VrmSetup(player, vrmInstance));
            }
        }

        public static void DetachVrmFromPlayer(Player player)
        {
            var playerName = player.GetPlayerDisplayName();


            Logger.Log($"Player Destroyed ->  {playerName} ");

            if (_vrmInstances.TryGetValue(playerName, out var instance))
            {
                var vrmGo = instance.GetGameObject();
                if (vrmGo != null)
                {
                    // var vrmAnimator = vrmGo.GetComponentInChildren<Animator>();
                    // vrmAnimator.transform.SetParent(null,false);
                    vrmGo.transform.SetParent(null, false);
                    vrmGo.SetActive(false);
                }


                // var vrmAnimationController = player.GetComponent<VrmAnimator>();
                // if (vrmAnimationController != null)
                // {
                //     UnityEngine.Object.Destroy(vrmAnimationController);
                // }

                var vrmEyeController = player.gameObject.GetComponent<VrmEyeAnimator>();
                if (vrmEyeController != null)
                {
                    UnityEngine.Object.Destroy(vrmEyeController);
                }

                var mToonController = vrmGo.GetComponent<VrmMToonFix>();
                if (mToonController != null)
                {
                    UnityEngine.Object.Destroy(mToonController);
                }
            }
        }

        private static IEnumerator VrmSetup(Player player, VrmInstance vrmInstance)
        {
            Logger.LogWarning("VRM SETUP");
            var vrmGo = vrmInstance.GetGameObject();

            if (vrmGo == null)
            {
                Logger.LogError("VrmGo is null");
                yield break;
            }

            var settings = vrmInstance.GetSettings();

            vrmGo.SetActive(true);

            player.m_maxInteractDistance *= settings.InteractionDistanceScale;


            var animator = player.GetComponentInChildren<Animator>();

            var oldModel = animator.transform.parent.Find(Constants.Vrm.GoName);
            if (oldModel != null)
            {
                UnityEngine.Object.Destroy(oldModel);
            }

            vrmGo.transform.SetParent(animator.transform.parent, false);


            float newHeight = settings.PlayerHeight;
            float newRadius = settings.PlayerRadius;

            var rigidBody = player.GetComponent<Rigidbody>();
            var collider = player.GetComponent<CapsuleCollider>();

            collider.height = newHeight;
            collider.radius = newRadius;
            collider.center = new Vector3(0, newHeight / 2, 0);


            rigidBody.centerOfMass = collider.center;

            yield return null;

            foreach (var smr in player.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                //smr.forceRenderingOff = true;
                //smr.updateWhenOffscreen = true;
                
                foreach (var mat in smr.materials)
                {
                    Logger.Log($"Mat Name: -> {mat.name} Mat Shader: -> {mat.shader.name}");
                }
                yield return null;
            }

            if (player.TryGetField<Player, Animator>("m_animator", out var playerAnimator))
            {
                playerAnimator.keepAnimatorStateOnDisable = true;
                playerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                yield return null;

                vrmGo.transform.localPosition = playerAnimator.transform.localPosition;

                var animationController = vrmGo.GetComponent<VrmAnimator>();

                if (animationController == null)
                {
                    animationController = vrmGo.AddComponent<VrmAnimator>();
                    animationController.Setup(player, playerAnimator, vrmInstance);
                }
                else
                {
                    animationController.Setup(player, playerAnimator, vrmInstance);
                }


                yield return null;

                if (settings.FixCameraHeight)
                {
                    var vrmEyeController = player.gameObject.GetComponent<VrmEyeAnimator>();
                    if (vrmEyeController == null)
                    {
                        player.gameObject.AddComponent<VrmEyeAnimator>().Setup(player, playerAnimator, vrmInstance);
                    }
                    else
                    {
                        vrmEyeController.Setup(player, playerAnimator, vrmInstance);
                    }
                }
            }
            else
            {
                Logger.LogError("playerAnimator Not found.");
            }


            if (settings.UseMToonShader)
            {
                var vrmMToonControler = vrmGo.GetComponent<VrmMToonFix>();

                if (vrmMToonControler == null)
                {
                    vrmGo.AddComponent<VrmMToonFix>().Setup(vrmGo);
                }
                else
                {
                    vrmMToonControler.Setup(vrmGo);
                }
            }

            yield return null;


            foreach (var springBone in vrmGo.GetComponentsInChildren<VRMSpringBone>())
            {
                springBone.m_stiffnessForce *= settings.SpringBoneStiffness;
                springBone.m_gravityPower *= settings.SpringBoneGravityPower;
                springBone.m_updateType = VRMSpringBone.SpringBoneUpdateType.FixedUpdate;
                springBone.m_center = null;
                yield return null;
            }


            //SetupBones(player, vrmInstance);
            //SetupAttachPoints(player, vrmInstance);
        }

 

        private static void SetupAttachPoints(Player player, VrmInstance vrmInstance)
        {
            if (player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
            {
                var vrmAnimator = vrmInstance.GetAnimator();


                visEquipment.m_leftHand = vrmAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                visEquipment.m_rightHand = vrmAnimator.GetBoneTransform(HumanBodyBones.RightHand);

                visEquipment.m_helmet = vrmAnimator.GetBoneTransform(HumanBodyBones.Head);
                visEquipment.m_backShield = vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backMelee = vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);

                visEquipment.m_backTwohandedMelee = vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backBow = vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backTool = vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
                visEquipment.m_backAtgeir = vrmAnimator.GetBoneTransform(HumanBodyBones.Chest);
            }
        }

        public static VrmInstance GetVrmInstance(this Player player)
        {
            var playerName = player.GetPlayerDisplayName();
            if (_vrmInstances.TryGetValue(playerName, out var instance))
            {
                return instance;
            }

            return null;
        }

        public static Animator GetVrmGoAnimator(this Player player)
        {
            var playerName = player.GetPlayerDisplayName();
            if (_vrmInstances.TryGetValue(playerName, out var instance))
            {
                return instance.GetAnimator();
            }

            return null;
        }

        public static bool HasVrmForPlayer(Player player)
        {
            var playerName = player.GetPlayerDisplayName();
            return _vrmInstances.ContainsKey(playerName);
        }

        public static GameObject GetVrmInstanceGameObject(Player player)
        {
            var playerName = player.GetPlayerDisplayName();
            if (_vrmInstances.TryGetValue(playerName, out var instance))
            {
                return instance.GetGameObject();
            }

            return null;
        }
    }
}