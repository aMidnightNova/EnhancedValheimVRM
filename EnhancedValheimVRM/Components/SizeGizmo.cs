using UnityEngine;

namespace EnhancedValheimVRM 
{
    public class SizeGizmo : MonoBehaviour
    {
        private static Material _playerSizeGizmoMaterial;
        private CapsuleCollider _playerCollider;
        private GameObject _playerSizeGizmo;
        private Player _player;

        public void Setup(Player player)
        {
            _player = player;


            if (_playerSizeGizmoMaterial == null)
            {
                _playerSizeGizmoMaterial = new Material(Shader.Find("Standard"));
                _playerSizeGizmoMaterial.SetFloat("_Mode", 2);
                _playerSizeGizmoMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _playerSizeGizmoMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _playerSizeGizmoMaterial.SetInt("_ZWrite", 0);
                _playerSizeGizmoMaterial.DisableKeyword("_ALPHATEST_ON");
                _playerSizeGizmoMaterial.EnableKeyword("_ALPHABLEND_ON");
                _playerSizeGizmoMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _playerSizeGizmoMaterial.SetFloat("_GlossMapScale", 0);
                _playerSizeGizmoMaterial.renderQueue = 3000;
                _playerSizeGizmoMaterial.color = new Color(1.0f, 0.0f, 0.0f, 0.3f);
            }

            _playerSizeGizmo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _playerSizeGizmo.GetComponent<MeshRenderer>().material = _playerSizeGizmoMaterial;
            _playerCollider = _player.GetComponent<CapsuleCollider>();
            Destroy(_playerSizeGizmo.GetComponent<Collider>());
            Logger.Log("CREATE SIZE GIZMO");
        }


        private void LateUpdate()
        {
            if (_playerSizeGizmo != null)
            {
                if (_playerCollider != null)
                {
                    _playerSizeGizmo.transform.position = _playerCollider.bounds.center;
                    _playerSizeGizmo.transform.localScale = new Vector3(_playerCollider.bounds.size.x, _playerCollider.bounds.size.y / 2, _playerCollider.bounds.size.z);
                }
            }
        }
    }
}