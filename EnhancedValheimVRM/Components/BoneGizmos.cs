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

        public void Setup(Player player, VrmInstance vrmInstance)
        {
            _player = player;
            _vAnimator = vrmInstance.GetAnimator();
            _animator = _player.GetField<Player, Animator>("m_animator");

            InitializeLineRenderersPlayer();
            InitializeLineRenderersVrm();
        }

        private void InitializeLineRenderersPlayer()
        {
            _playerGizmos = true;
            var bones = _animator.GetComponentsInChildren<Transform>();

            foreach (var bone in bones)
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
            lineRenderer.transform.SetParent(bone);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = 0.01f;
            lineRenderer.endWidth = 0.01f;
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = false;

            Material lineMaterial = new Material(Shader.Find("Unlit/Color"));
            lineMaterial.color = color;
            lineRenderer.material = lineMaterial;

            return lineRenderer;
        }

        private void Update()
        {
            UpdateLineRenderers();
        }

        private void UpdateLineRenderers()
        {
            int index = 0;

            if (_playerGizmos)
            {
                foreach (var bone in _animator.GetComponentsInChildren<Transform>())
                {
                    if (index + 2 < _playerLineRenderers.Count)
                    {
                        Vector3 boneRight = bone.TransformDirection(Vector3.right * 0.1f);
                        Vector3 boneUp = bone.TransformDirection(Vector3.up * 0.1f);
                        Vector3 boneForward = bone.TransformDirection(Vector3.forward * 0.1f);

                        UpdateLineRenderer(_playerLineRenderers[index++], bone.position, bone.position + boneRight);
                        UpdateLineRenderer(_playerLineRenderers[index++], bone.position, bone.position + boneUp);
                        UpdateLineRenderer(_playerLineRenderers[index++], bone.position, bone.position + boneForward);
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
                        Vector3 boneRight = bone.TransformDirection(Vector3.right * 0.1f);
                        Vector3 boneUp = bone.TransformDirection(Vector3.up * 0.1f);
                        Vector3 boneForward = bone.TransformDirection(Vector3.forward * 0.1f);

                        UpdateLineRenderer(_vrmLineRenderers[index++], bone.position, bone.position + boneRight);
                        UpdateLineRenderer(_vrmLineRenderers[index++], bone.position, bone.position + boneUp);
                        UpdateLineRenderer(_vrmLineRenderers[index++], bone.position, bone.position + boneForward);
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