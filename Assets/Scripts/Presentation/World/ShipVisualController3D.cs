using UnityEngine;
using SRG.Presentation.Common;
using SRG.Simulation;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Корпус корабля 3D-моделью под той же ортографической камерой «сверху».
    ///
    /// Модель — префаб <c>Resources/Models/…</c> по пути листа корпуса (см.
    /// <see cref="ModelLibrary.ModelPathFor"/>), иначе процедурная заглушка в цвете расы.
    /// Размер в плане совпадает с кадром спрайт-листа (<see cref="GraphicsCatalog.FrameWorldSize"/>),
    /// поэтому 2D и 3D занимают одинаковое место и дистанции геймплея не расходятся.
    /// Модель целиком лежит за плоскостью z = 0 (ближайшая к камере точка — на ней): порядок с
    /// остальной сценой задаёт sortingOrder, как у спрайтов, а глубина нужна только самой модели.
    /// </summary>
    public class ShipVisualController3D : ShipVisualController
    {
        private GameObject _body;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _mpb;

        protected override void BuildBody() => RebuildBody();

        protected override void RebuildBody()
        {
            DestroyBody();

            string path = Data?.GetBodyGraphicPath();
            var prefab = GraphicsManager.Instance?.TryGetModelPrefab(ModelLibrary.ModelPathFor(path));
            _body = prefab != null
                ? Instantiate(prefab, transform, false)
                : ModelLibrary.CreateShipPlaceholder(transform, ModelLibrary.ColorFor(Data));
            _body.name = "Body3D";

            // Модель нормирована на квадрат 1×1 — приводим к размеру кадра листа.
            float footprint = GraphicsCatalog.FrameWorldSize(path);
            _body.transform.localScale = Vector3.one * footprint;

            _renderers = _body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _renderers) r.sortingOrder = BodySortingOrder;
            ModelLibrary.PlaceBehindSystemPlane(_body.transform, _renderers);
        }

        protected override void ApplyBodyAlpha(float alpha)
        {
            if (_renderers == null) return;
            _mpb ??= new MaterialPropertyBlock();
            bool visible = alpha > 0.001f;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.enabled = visible;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(ModelLibrary.AlphaId, alpha);
                r.SetPropertyBlock(_mpb);
            }
        }

        protected override void PrepareBodyForExplosion() => DestroyBody();

        public override void Teardown()
        {
            base.Teardown();
            DestroyBody();
        }

        private void DestroyBody()
        {
            if (_body != null) Destroy(_body);
            _body = null;
            _renderers = null;
        }
    }
}
