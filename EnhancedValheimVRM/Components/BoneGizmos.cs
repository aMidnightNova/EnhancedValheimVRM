using UnityEngine;
using System.Collections.Generic;

namespace EnhancedValheimVRM // TODO: fix this, it does not work... yet.
{
    public class BoneGizmos : MonoBehaviour
    {
        private Player _player;
        private Animator _animator;
        private Animator _vAnimator;
        private List<LineRenderer> _playerLineRenderers = new List<LineRenderer>();
        private List<LineRenderer> _vrmLineRenderers = new List<LineRenderer>();
        private bool _playerGizmos = false;
        private bool _vrmGizmos = false;
        private VisEquipment _visEquipment;
        private Shader _shader = Shader.Find("Unlit/Color");

        public void Setup(Player player, VrmInstance vrmInstance)
        {
            _player = player;
            _vAnimator = vrmInstance.GetVrmGoAnimator();
            _animator = _player.GetField<Player, Animator>("m_animator");
            if (_player.TryGetField<Player, VisEquipment>("m_visEquipment", out var visEquipment))
            {
                _visEquipment = visEquipment;
            }

            var bones = _animator.GetComponentsInChildren<Transform>();

            if (_visEquipment.TryGetField<VisEquipment, GameObject>("m_rightItemInstance", out var go))
            {
                var goAnimator = go.GetComponentInChildren<Animator>();
                bones = goAnimator.GetComponentsInChildren<Transform>();


                _animator = goAnimator;
                Logger.Log("_ ____ goAnimator");
            }
            
            
            
            InitializeLineRenderers(bones);
            //InitializeLineRenderersVrm();
            UpdateLineRenderers();
        }

        private void InitializeLineRenderers(Transform[] transforms)
        {
            _playerGizmos = true;
            

            foreach (var bone in transforms)
            {
                _playerLineRenderers.Add(CreateLineRenderer(bone, Color.red));
                _playerLineRenderers.Add(CreateLineRenderer(bone, Color.green));
                _playerLineRenderers.Add(CreateLineRenderer(bone, Color.blue));
            }
        }

        private void InitializeLineRenderersVrm()
        {
            _vrmGizmos = true;
            var vBones = _vAnimator.GetComponentsInChildren<Transform>();

            foreach (var bone in vBones)
            {
                _vrmLineRenderers.Add(CreateLineRenderer(bone, Color.red));
                _vrmLineRenderers.Add(CreateLineRenderer(bone, Color.green));
                _vrmLineRenderers.Add(CreateLineRenderer(bone, Color.blue));
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
                foreach (var bone in _animator.GetComponentsInChildren<Transform>())
                {
                    if (index + 2 < _playerLineRenderers.Count)
                    {
                        var boneRight = bone.TransformDirection(Vector3.right * 0.0004f);
                        var boneUp = bone.TransformDirection(Vector3.up * 0.0004f);
                        var boneForward = bone.TransformDirection(Vector3.forward * 0.0004f);

                        UpdateLineRenderer(_playerLineRenderers[index++], bone.localPosition, bone.localPosition + boneRight);
                        UpdateLineRenderer(_playerLineRenderers[index++], bone.localPosition, bone.localPosition + boneUp);
                        UpdateLineRenderer(_playerLineRenderers[index++], bone.localPosition, bone.localPosition + boneForward);
                    }
                }
            }

            index = 0; // Reset index for VRM gizmos

            if (_vrmGizmos)
            {
                foreach (var bone in _vAnimator.GetComponentsInChildren<Transform>())
                {
                    if (index + 2 < _vrmLineRenderers.Count)
                    {
                        var boneRight = bone.TransformDirection(Vector3.right * 0.04f);
                        var boneUp = bone.TransformDirection(Vector3.up * 0.04f);
                        var boneForward = bone.TransformDirection(Vector3.forward * 0.04f);

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
