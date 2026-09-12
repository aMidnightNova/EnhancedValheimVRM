using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class BoneGizmos : MonoBehaviour
    {
        private sealed class SocketGizmo
        {
            public Transform Bone;
            public string Label;
            public readonly List<LineRenderer> Axes = new List<LineRenderer>();
        }

        private readonly List<SocketGizmo> _gizmos = new List<SocketGizmo>();
        private readonly HashSet<Transform> _equipmentSockets = new HashSet<Transform>();
        private GUIStyle _labelStyle;

        public void Setup(Player player, VrmInstance vrmInstance, bool playerGizmoEnabled = false, bool vrmGizmoEnabled = false)
        {
            ClearGizmos();
            _equipmentSockets.Clear();
            if (player.TryGetField<Player, VisEquipment>("m_visEquipment", out var equipment) && equipment != null)
            {
                foreach (var socket in new[] { equipment.m_leftHand, equipment.m_rightHand, equipment.m_helmet,
                    equipment.m_backShield, equipment.m_backMelee, equipment.m_backTwohandedMelee,
                    equipment.m_backBow, equipment.m_backTool, equipment.m_backAtgeir })
                    if (socket != null) _equipmentSockets.Add(socket);
            }
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) return;
            if (playerGizmoEnabled) AddSockets(player.GetField<Player, Animator>("m_animator"), "Player", shader);
            if (vrmGizmoEnabled) AddSockets(vrmInstance.GetVrmGoAnimator(), "VRM", shader);
            UpdateAxes();
        }

        private void AddSockets(Animator animator, string source, Shader shader)
        {
            if (animator == null) return;
            var leftForearm = BoneLookup.Get(animator, HumanBodyBones.LeftLowerArm);
            var rightForearm = BoneLookup.Get(animator, HumanBodyBones.RightLowerArm);
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            {
                // Use the game's actual sockets, plus any additional attachment
                // markers in either rig. Do not restrict this to particular slots.
                bool forearm = bone == leftForearm || bone == rightForearm;
                if (!forearm && !_equipmentSockets.Contains(bone) && bone.name.IndexOf("_attach", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_gizmos.Exists(existing => existing.Bone == bone)) continue;
                var gizmo = new SocketGizmo { Bone = bone, Label = source + " / " + bone.name + (forearm ? " (forearm bone)" : "") };
                foreach (var color in new[] { Color.red, Color.green, Color.blue })
                {
                    var line = new GameObject("BoneGizmoLine").AddComponent<LineRenderer>();
                    line.transform.SetParent(bone, false);
                    line.startColor = line.endColor = color;
                    line.startWidth = line.endWidth = 0.005f;
                    line.positionCount = 2;
                    line.useWorldSpace = false;
                    // Unlit/Color uses material color rather than vertex colors.
                    line.sharedMaterial = new Material(shader) { color = color };
                    gizmo.Axes.Add(line);
                }
                _gizmos.Add(gizmo);
            }
        }

        internal void SetVisible(bool visible)
        {
            enabled = visible;
            foreach (var gizmo in _gizmos)
                foreach (var line in gizmo.Axes)
                    if (line != null) line.enabled = visible;
        }

        private void LateUpdate() => UpdateAxes();

        private void UpdateAxes()
        {
            foreach (var gizmo in _gizmos)
            {
                if (gizmo.Bone == null) continue;
                for (int axis = 0; axis < gizmo.Axes.Count; axis++)
                {
                    var line = gizmo.Axes[axis];
                    if (line == null) continue;
                    var direction = axis == 0 ? gizmo.Bone.right : axis == 1 ? gizmo.Bone.up : gizmo.Bone.forward;
                    // Each line starts at its own socket, with a 12 cm world length
                    // independent of avatar import scale. Parenting follows animation.
                    line.SetPosition(0, Vector3.zero);
                    line.SetPosition(1, gizmo.Bone.InverseTransformVector(direction * 0.12f));
                }
            }
        }

        private void OnGUI()
        {
            var camera = Camera.main;
            if (camera == null || Event.current.type != EventType.Repaint) return;
            if (_labelStyle == null)
                _labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 14, fontStyle = FontStyle.Bold };

            foreach (var gizmo in _gizmos)
            {
                if (gizmo.Bone == null || !gizmo.Bone.gameObject.activeInHierarchy) continue;
                var screen = camera.WorldToScreenPoint(gizmo.Bone.position);
                if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) continue;
                var rect = new Rect(screen.x - 160, Screen.height - screen.y - 32, 320, 24);
                _labelStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), gizmo.Label, _labelStyle);
                _labelStyle.normal.textColor = Color.white;
                GUI.Label(rect, gizmo.Label, _labelStyle);
            }
        }

        private void ClearGizmos()
        {
            foreach (var gizmo in _gizmos)
            foreach (var line in gizmo.Axes)
            {
                if (line == null) continue;
                Destroy(line.sharedMaterial);
                Destroy(line.gameObject);
            }
            _gizmos.Clear();
        }

        private void OnDestroy() => ClearGizmos();
    }
}
