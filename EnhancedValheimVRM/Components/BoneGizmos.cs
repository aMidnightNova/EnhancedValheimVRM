using System;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

namespace EnhancedValheimVRM // TODO: fix this, it does not work... yet.
{
    public class BoneGizmos : MonoBehaviour
    {
        private Player _player;
        private Animator _playerAnimator;
        private Animator _vrmGoAnimator;
        private List<LineRenderer> _playerLineRenderers = new List<LineRenderer>();
        private List<LineRenderer> _vrmLineRenderers = new List<LineRenderer>();
        private bool _playerGizmos = false;
        private bool _vrmGizmos = false;
        private VisEquipment _visEquipment;
        private Shader _shader = Shader.Find("Unlit/Color");

        public void Setup(Player player, VrmInstance vrmInstance, bool playerGizmoEnabled = false, bool vrmGizmoEnabled = false)
        {
            _playerGizmos = playerGizmoEnabled;
            _vrmGizmos = vrmGizmoEnabled;


            _player = player;
            _vrmGoAnimator = vrmInstance.GetVrmGoAnimator();
            _playerAnimator = _player.GetField<Player, Animator>("m_animator");
            if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
            {
                _visEquipment = visEquipment;
            }


            if (_visEquipment.GetFieldValue<FieldInfo>("m_rightItem")?.GetValue(_visEquipment) is string rightItemName)
            {
                if (GameItem.IsSpecialCase(rightItemName))
                {
                    if (_visEquipment.TryGetField<VisEquipment, GameObject>("m_rightItemInstance", out var go))
                    {
                        // this is overriding _playerAnimator to the armiture inside a rigged weapon.
                        var animator = go.GetComponentInChildren<Animator>();
                        _playerAnimator = animator;
                    }
                }
            }


            if (_playerGizmos) InitializeLineRenderersPlayer();
            if (_vrmGizmos) InitializeLineRenderersVrm();

            UpdateLineRenderers();
        }

        private void InitializeLineRenderersPlayer()
        {
            var bones = _playerAnimator.GetComponentsInChildren<Transform>();


            foreach (var bone in bones)
            {
                //if (bone.name.Contains("_attach") || bone.name.Contains("_Attach"))
                if(bone.name == "LeftHand_Attach" || bone.name == "RightHand_Attach" || bone.name == "BackTool_attach" )
                {
                    _playerLineRenderers.Add(CreateLineRenderer(bone, Color.red));
                    _playerLineRenderers.Add(CreateLineRenderer(bone, Color.green));
                    _playerLineRenderers.Add(CreateLineRenderer(bone, Color.blue));
                }

            }
        }

        private void InitializeLineRenderersVrm()
        {
            var vBones = _vrmGoAnimator.GetComponentsInChildren<Transform>();

            foreach (var bone in vBones)
            {
                //if (bone.name.Contains("_attach") || bone.name.Contains("_Attach"))
                if(bone.name == "LeftHand_Attach" || bone.name == "RightHand_Attach" || bone.name == "BackTool_attach")
                {
                    _vrmLineRenderers.Add(CreateLineRenderer(bone, Color.red));
                    _vrmLineRenderers.Add(CreateLineRenderer(bone, Color.green));
                    _vrmLineRenderers.Add(CreateLineRenderer(bone, Color.blue));
                }

            }
        }

        private LineRenderer CreateLineRenderer(Transform bone, Color color)
        {
            var lineRenderer = new GameObject("BoneGizmoLine").AddComponent<LineRenderer>();
            lineRenderer.transform.SetParent(bone, false);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = 0.01f;
            lineRenderer.endWidth = 0.01f;
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = false;

            Material lineMaterial = new Material(_shader);
            lineMaterial.color = color;
            lineRenderer.material = lineMaterial;

            return lineRenderer;
        }

        private void LateUpdate()
        {
            //UpdateLineRenderers();
        }

        private void UpdateLineRenderers()
        {
            var index = 0;

            if (_playerGizmos)
            {
                foreach (var bone in _playerAnimator.GetComponentsInChildren<Transform>())
                {
                    if (index + 2 < _playerLineRenderers.Count)
                    {
                        var boneLength = 10f;
                        var boneRight = bone.TransformDirection(Vector3.right * (bone.localScale.x * boneLength));
                        var boneUp = bone.TransformDirection(Vector3.up * (bone.localScale.y * boneLength));
                        var boneForward = bone.TransformDirection(Vector3.forward * (bone.localScale.z * boneLength));

                        UpdateLineRenderer(_playerLineRenderers[index++], bone.localPosition, bone.localPosition + boneRight);
                        UpdateLineRenderer(_playerLineRenderers[index++], bone.localPosition, bone.localPosition + boneUp);
                        UpdateLineRenderer(_playerLineRenderers[index++], bone.localPosition, bone.localPosition + boneForward);
                    }
                }
            }

            index = 0; // Reset index for VRM gizmos

            if (_vrmGizmos)
            {
                foreach (var bone in _vrmGoAnimator.GetComponentsInChildren<Transform>())
                {
                    if (index + 2 < _vrmLineRenderers.Count)
                    {
                        var boneLength = 10f;
                        var boneRight = bone.TransformDirection(Vector3.right * (bone.localScale.x * boneLength));
                        var boneUp = bone.TransformDirection(Vector3.up * (bone.localScale.y * boneLength));
                        var boneForward = bone.TransformDirection(Vector3.forward * (bone.localScale.z * boneLength));

                        UpdateLineRenderer(_vrmLineRenderers[index++], bone.localPosition, bone.localPosition + boneRight);
                        UpdateLineRenderer(_vrmLineRenderers[index++], bone.localPosition, bone.localPosition + boneUp);
                        UpdateLineRenderer(_vrmLineRenderers[index++], bone.localPosition, bone.localPosition + boneForward);
                    }
                }
            }
        }

        private void UpdateLineRenderer(LineRenderer lineRenderer, Vector3 startPosition, Vector3 endPosition)
        {
            if (lineRenderer != null)
            {
                lineRenderer.SetPosition(0, startPosition);
                lineRenderer.SetPosition(1, endPosition);
            }
        }
    }
}