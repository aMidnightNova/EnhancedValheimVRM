using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // tells a wall from an edge. a thin trigger disk as wide as the vanilla capsule, sitting at the
    // height where its round bottom ends. anything still solid there is a wall the vanilla sphere
    // would have met flat on and never ridden up, however close the smaller avatar capsule gets to
    // it. the step over lift leaves whatever touches this alone.
    public sealed class WallSensor : MonoBehaviour
    {
        private const float Radius = 0.49f, Thickness = 0.04f;
        private const int Sides = 24;

        private static Mesh _mesh;

        // looked up per contact, the avatar hierarchy is far too big to search each time
        private static readonly Dictionary<Player, WallSensor> Sensors = new Dictionary<Player, WallSensor>();
        private readonly HashSet<Collider> _touching = new HashSet<Collider>();

        internal static void Attach(Player player)
        {
            if (player == null || Sensors.ContainsKey(player)) return;
            var sensor = new GameObject("wall sensor") { layer = player.gameObject.layer };
            sensor.transform.SetParent(player.transform, false);
            sensor.transform.localPosition = new Vector3(0f, Radius, 0f);
            var collider = sensor.AddComponent<MeshCollider>();
            collider.sharedMesh = _mesh ?? (_mesh = BuildDisk());
            collider.convex = true;
            collider.isTrigger = true;
            Sensors[player] = sensor.AddComponent<WallSensor>();
        }

        internal static void Detach(Player player)
        {
            if (player == null || !Sensors.TryGetValue(player, out var sensor)) return;
            Sensors.Remove(player);
            if (sensor != null) Destroy(sensor.gameObject);
        }

        internal static bool Touching(Player player, Collider collider)
        {
            return Sensors.TryGetValue(player, out var sensor) && sensor != null && sensor._touching.Contains(collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            _touching.Add(other);
        }

        private void OnTriggerExit(Collider other)
        {
            _touching.Remove(other);
        }

        private static Mesh BuildDisk()
        {
            var vertices = new Vector3[Sides * 2];
            for (var i = 0; i < Sides; i++)
            {
                var angle = i * Mathf.PI * 2f / Sides;
                var x = Mathf.Cos(angle) * Radius;
                var z = Mathf.Sin(angle) * Radius;
                vertices[i] = new Vector3(x, -Thickness / 2f, z);
                vertices[i + Sides] = new Vector3(x, Thickness / 2f, z);
            }

            var triangles = new List<int>();
            for (var i = 0; i < Sides; i++)
            {
                var next = (i + 1) % Sides;
                triangles.AddRange(new[] { i, next, i + Sides, next, next + Sides, i + Sides });
                if (i < Sides - 2)
                {
                    triangles.AddRange(new[] { 0, i + 2, i + 1 });
                    triangles.AddRange(new[] { Sides, Sides + i + 1, Sides + i + 2 });
                }
            }

            var mesh = new Mesh { name = "wall sensor", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
