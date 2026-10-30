using UnityEngine;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Слой 3D-сферы планеты/спутника (поверхность или облака) на шейдере <c>PlanetSphere3D</c>:
    /// текстура, наклон оси, цвет и вращение — через MaterialPropertyBlock общего материала.
    /// </summary>
    public sealed class PlanetSphereLayer
    {
        private static readonly int MainTexId   = Shader.PropertyToID("_MainTex");
        private static readonly int RotationId  = Shader.PropertyToID("_Rotation");
        private static readonly int AxialTiltId = Shader.PropertyToID("_AxialTilt");
        private static readonly int TintId      = Shader.PropertyToID("_Tint");

        private readonly MeshRenderer _renderer;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private readonly float _speed;   // оборотов в секунду
        private float _rotation;         // 0..1

        public PlanetSphereLayer(Transform parent, string name, Texture texture, float axialTilt, Color tint,
                                 float radiusScale, int sortingOrder, float speed, float startRotation)
        {
            var go = ModelLibrary.CreateMeshObject(name, parent, ModelLibrary.SphereMesh, ModelLibrary.PlanetMaterial);
            go.transform.localScale = Vector3.one * radiusScale;
            _renderer = go.GetComponent<MeshRenderer>();
            _renderer.sortingOrder = sortingOrder;
            _speed = speed;
            _rotation = startRotation;

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(MainTexId, texture);
            _mpb.SetFloat(AxialTiltId, axialTilt);
            _mpb.SetColor(TintId, tint);
            _mpb.SetFloat(RotationId, _rotation);
            _renderer.SetPropertyBlock(_mpb);
        }

        public int SortingOrder { set { if (_renderer != null) _renderer.sortingOrder = value; } }

        public void Tick(float dt)
        {
            if (_speed == 0f || _renderer == null) return;
            _rotation += _speed * dt;
            if (_rotation > 1f) _rotation -= 1f;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(RotationId, _rotation);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
