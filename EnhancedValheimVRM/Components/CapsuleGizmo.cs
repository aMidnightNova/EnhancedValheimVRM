using UnityEngine;

namespace EnhancedValheimVRM
{
    // /vrm dev capsule show: translucent shapes the size of the
    // players live capsule (red), the vanilla capsule it replaced (blue) and the wall sensor
    // (green), so what the physics sees can be held against what the avatar looks like. the live
    // one follows the collider every frame.
    public sealed class CapsuleGizmo : MonoBehaviour
    {
        private const float VanillaRadius = 0.49f, VanillaHeight = 1.85f, VanillaCentre = 0.925f;
        private static Material _red, _blue, _green;
        private Shape _live, _vanilla;
        private GameObject _sensor;
        private CapsuleCollider _capsule;

        // a capsule the way physics has it: a straight middle and a round end each side
        private sealed class Shape
        {
            internal GameObject Middle, Top, Bottom;

            internal void Fit(Vector3 centre, float radius, float height)
            {
                var half = Mathf.Max(0f, height / 2f - radius);
                Middle.transform.position = centre;
                Middle.transform.localScale = new Vector3(radius * 2f, half, radius * 2f);
                Top.transform.position = centre + Vector3.up * half;
                Bottom.transform.position = centre - Vector3.up * half;
                Top.transform.localScale = Bottom.transform.localScale = Vector3.one * (radius * 2f);
            }

            internal void Destroy()
            {
                Object.Destroy(Middle);
                Object.Destroy(Top);
                Object.Destroy(Bottom);
            }
        }

        internal static string Set(Player player, bool show)
        {
            if (player == null) return "Enter a world first.";
            var gizmo = player.GetComponent<CapsuleGizmo>();
            if (!show)
            {
                if (gizmo != null) Destroy(gizmo);
                return "Capsules hidden.";
            }

            if (gizmo == null) player.gameObject.AddComponent<CapsuleGizmo>();
            return "Capsules shown: red is the live one, blue the vanilla one, green the wall sensor.";
        }

        private void Awake()
        {
            _capsule = GetComponent<CapsuleCollider>();
            _live = Capsule(_red ?? (_red = Translucent(new Color(1f, 0f, 0f, 0.3f))));
            _vanilla = Capsule(_blue ?? (_blue = Translucent(new Color(0f, 0.4f, 1f, 0.2f))));
            _sensor = Primitive(PrimitiveType.Cylinder, _green ?? (_green = Translucent(new Color(0f, 1f, 0f, 0.35f))));
        }

        private void LateUpdate()
        {
            var origin = transform.position;
            if (_capsule != null) _live.Fit(_capsule.bounds.center, _capsule.radius, _capsule.height);
            _vanilla.Fit(origin + Vector3.up * VanillaCentre, VanillaRadius, VanillaHeight);
            // unitys cylinder primitive is 2 tall and 1 wide
            _sensor.transform.position = origin + Vector3.up * VanillaRadius;
            _sensor.transform.localScale = new Vector3(VanillaRadius * 2f, 0.02f, VanillaRadius * 2f);
        }

        private static Shape Capsule(Material material)
        {
            return new Shape
            {
                Middle = Primitive(PrimitiveType.Cylinder, material),
                Top = Primitive(PrimitiveType.Sphere, material),
                Bottom = Primitive(PrimitiveType.Sphere, material)
            };
        }

        private static GameObject Primitive(PrimitiveType type, Material material)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = "CapsuleGizmo";
            primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
            Destroy(primitive.GetComponent<Collider>());
            return primitive;
        }

        private static Material Translucent(Color color)
        {
            var material = new Material(Shader.Find("Standard"));
            material.SetFloat("_Mode", 2);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetFloat("_GlossMapScale", 0);
            material.renderQueue = 3000;
            material.color = color;
            return material;
        }

        private void OnDestroy()
        {
            _live?.Destroy();
            _vanilla?.Destroy();
            Destroy(_sensor);
        }
    }
}
