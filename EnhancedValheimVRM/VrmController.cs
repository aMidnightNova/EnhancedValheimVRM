using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRM;

namespace EnhancedValheimVRM
{
    public static class VrmController
    {
        
        private static Dictionary<Player, VrmInstance> _vrmInstances = new Dictionary<Player, VrmInstance>();

        public static void AttachVrmToPlayer(Player player)
        {
            VrmInstance vrmInstance;

            var exists = _vrmInstances.TryGetValue(player, out vrmInstance);

            if (!exists)
            {
                try
                {
                    vrmInstance = new VrmInstance(player);
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex.Message);
                }
            }

            if (vrmInstance != null)
            {
                if(!exists) _vrmInstances.Add(player,vrmInstance);
                CoroutineHelper.Instance.StartCoroutine(VrmSetup(player, vrmInstance));
            }
            
        }
        public static void DetachVrmFromPlayer(Player player)
        {
            if (_vrmInstances.TryGetValue(player, out var instance))
            {
                var vrmGo = instance.GetGameObject();
                if (vrmGo != null)
                {
                    vrmGo.transform.parent = null;
                }

                var vrmAnimationController = player.GetComponent<VrmAnimationController>();
                if (vrmAnimationController != null)
                {
                    UnityEngine.Object.Destroy(vrmAnimationController);
                }
                
                var vrmEyeController = player.GetComponent<VrmEyeController>();
                if (vrmEyeController != null)
                {
                    UnityEngine.Object.Destroy(vrmEyeController);
                }
                
                var mToonController = player.GetComponent<MToonController>();
                if (mToonController != null)
                {
                    UnityEngine.Object.Destroy(mToonController);
                }
            }
        }

        private static IEnumerator VrmSetup(Player player, VrmInstance vrmInstance)
        {
            var vrmGo = vrmInstance.GetGameObject();
            var settings = vrmInstance.GetSettings();
            
            vrmGo.SetActive(true);
            
            player.m_maxInteractDistance *= settings.InteractionDistanceScale;
            
            
            
            var animator = player.GetComponentInChildren<Animator>(); 
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
                smr.forceRenderingOff = true;
                smr.updateWhenOffscreen = true;
                yield return null;
            }
            
            if (player.TryGetField<Player,Animator>("m_animator", out var playerAnimator))
            {
                playerAnimator.keepAnimatorStateOnDisable = true;
                playerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                
                yield return null;
                
                vrmGo.transform.localPosition = playerAnimator.transform.localPosition;
                
                player.gameObject.AddComponent<VrmAnimationController>().Setup(player, playerAnimator, vrmInstance);
                
                yield return null;
                
                if (settings.FixCameraHeight)
                {
                    player.gameObject.AddComponent<VrmEyeController>().Setup(player, playerAnimator, vrmInstance);
                }
            }
            else
            {
                Debug.LogError("playerAnimator Not found.");
            }
            
            
            if (settings.UseMToonShader)
            {
                vrmGo.AddComponent<MToonController>().Setup(vrmGo);
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


        }

        public static VrmInstance GetVrmInstance(this Player player)
        {
            if (_vrmInstances.TryGetValue(player, out var instance))
            {
                return instance;
            }

            return null;
        }

        public static bool HasVrmForPlayer(Player player)
        {
            return _vrmInstances.ContainsKey(player);
        }

        public static GameObject GetVrmInstanceGameObject(Player player)
        {
            if (_vrmInstances.TryGetValue(player, out var instance))
            {
                return instance.GetGameObject();
            }
            return null;
        }
    }
}