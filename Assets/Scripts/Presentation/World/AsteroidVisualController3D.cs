using UnityEngine;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Presentation.Effects;
using SRG.Utils;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Астероид 3D-камнем: процедурный гранёный меш (вариант формы — от Uid), цвет по типу,
    /// кувыркание вокруг собственной случайной оси со скоростью <see cref="AsteroidData.SelfRotationSpeed"/>.
    /// Размер — как у спрайта (<see cref="AsteroidVisual.VisualScale"/>); модель лежит за плоскостью z = 0.
    /// </summary>
    public class AsteroidVisualController3D : AsteroidVisual
    {
        private static readonly Color RockyColor    = new Color(0.55f, 0.47f, 0.4f);
        private static readonly Color MetallicColor = new Color(0.52f, 0.6f, 0.7f);

        private Transform _rock;
        private Vector3 _tumbleAxis;
        private float _rotationSpeed;   // °/сек

        public override void Setup(AsteroidData data)
        {
            int seed = StableHash.Of(data.Uid);
            _rotationSpeed = data.SelfRotationSpeed;
            var rnd = new System.Random(seed);
            _tumbleAxis = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f,
                                      (float)rnd.NextDouble() - 0.5f).normalized;
            if (_tumbleAxis.sqrMagnitude < 0.01f) _tumbleAxis = Vector3.forward;

            bool metallic = (data.TypeId ?? data.GraphicPath ?? "").Contains("Metallic");
            var go = ModelLibrary.CreateMeshObject("Rock3D", transform, ModelLibrary.RockMesh(seed),
                ModelLibrary.LitMaterial(metallic ? MetallicColor : RockyColor));
            _rock = go.transform;
            _rock.localRotation = Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360));

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingOrder = SortingLayerRegistry.Get(SortLayer.Asteroid);

            transform.localScale = Vector3.one * VisualScale(data);
            transform.localPosition = new Vector3(data.Position.x, data.Position.y, 0f);
        }

        public override void PlayExplosion(string explosionPath = null)
        {
            enabled = false;
            if (_rock != null) Destroy(_rock.gameObject);
            _rock = null;
            ExplosionPlayback.PlayOn(gameObject, explosionPath);
        }

        private void Update()
        {
            if (_rock == null) return;
            // Кувыркание вокруг своей оси; камень может выступать к камере — держим его за плоскостью.
            _rock.Rotate(_tumbleAxis, _rotationSpeed * Time.deltaTime, Space.Self);
            _rock.localPosition = Vector3.zero;
            ModelLibrary.PlaceBehindSystemPlane(_rock, _renderers ??= _rock.GetComponentsInChildren<Renderer>());
        }

        private Renderer[] _renderers;
    }
}
