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
        public static Dictionary<string, VrmInstance> VrmInstances => _vrmInstances;

        public static void AttachVrmToPlayer(Player player)
        {
            Logger.Log("___________________________________________ Start AttachVrmToPlayer", Logger.LogLevel.Debug);

            
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
                // if (vrmGo != null)
                // {
                //     // var vrmAnimator = vrmGo.GetComponentInChildren<Animator>();
                //     // vrmAnimator.transform.SetParent(null,false);
                //     vrmGo.transform.SetParent(null, false);
                //     vrmGo.SetActive(false);
                // }


                var vrmAnimator = player.GetComponent<VrmAnimator>();
                if (vrmAnimator != null)
                {
                    UnityEngine.Object.Destroy(vrmAnimator);
                }

                var vrmEyeController = player.GetComponent<VrmEyeAnimator>();
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
            
            Logger.Log("Start VrmSetup", Logger.LogLevel.Debug);
            
            var settings = vrmInstance.GetSettings();

            
            foreach (var smr in player.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                //control Player Visible PlayerVisible
                smr.forceRenderingOff = true;
                smr.updateWhenOffscreen = true;

                yield return null;
            }
 
            var vrmGo = vrmInstance.GetGameObject();

            if (vrmGo == null)
            {
                Logger.LogError("VrmGo Is Null VrmSetup");
                yield break;
            }


            vrmGo.SetActive(true);

            player.m_maxInteractDistance *= settings.InteractionDistanceScale;





 

            var rigidBody = player.GetComponent<Rigidbody>();
            var collider = player.GetComponent<CapsuleCollider>();

            if (collider != null)
            {
                collider.height = settings.VrmHeight;
                collider.radius = settings.VrmRadius;
                collider.center = new Vector3(0, settings.VrmHeight / 2, 0);
            }
            else
            {
                Logger.LogError("CapsuleCollider component is missing on the player object.");
            }

            if (rigidBody != null)
            {
                if (collider != null)
                {
                    Logger.Log("______________________________ Set Rigidbody centerOfMass.");

                    rigidBody.centerOfMass = collider.center;
                }
                else
                {
                    Logger.LogError("Cannot set Rigidbody centerOfMass because CapsuleCollider is missing.");
                }
            }
            else
            {
                Logger.LogError("Rigidbody component is missing on the player object.");
            }
            
            
            yield return null;



            if (player.TryGetField<Player, Animator>("m_animator", out var playerAnimator))
            {
                
                playerAnimator.keepAnimatorStateOnDisable = true;
                playerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                
                yield return null;
                
                // var vrmBonesTransformer = vrmInstance.GetBoneTransformer();
                // vrmBonesTransformer.ResizePlayerAvatarToVrmSize(player);
                if (vrmGo == null)
                {
                    Logger.LogError("VrmGo Is Null VrmSetup 2");
                    yield break;
                }
                var vrmAnimator = vrmGo.GetComponent<VrmAnimator>();

                if (vrmAnimator == null)
                {
                    vrmAnimator = vrmGo.AddComponent<VrmAnimator>();
                    vrmAnimator.Setup(player, playerAnimator, vrmInstance);
                }
                else
                {
                    vrmAnimator.Setup(player, playerAnimator, vrmInstance);
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

            //player.gameObject.AddComponent<BoneGizmos>().Setup(player, vrmInstance);
 
            //BoneTransforms.CopyBoneTransforms(player, vrmInstance);
            //BoneTransforms.CopyBoneTransforms(player, vrmInstance.GetGameObject());
            //SetupAttachPoints(player, vrmInstance);
            
            
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
                return instance.GetVrmGoAnimator();
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