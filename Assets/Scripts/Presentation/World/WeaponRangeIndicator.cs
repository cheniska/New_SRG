using System.Collections.Generic;
using UnityEngine;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Controllers;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Presentation.World
{
    // Отображает пунктирные кольца дальности каждой пушки вокруг корабля игрока.
    // Видимо только когда активен режим стрельбы (PlayerShip.IsWeaponModeActive).
    [DefaultExecutionOrder(100)]
    public class WeaponRangeIndicator : MonoBehaviour
    {
        [Header("Внешний вид")]
        public Color RingColor = new Color(1f, 0.25f, 0.1f, 0.65f);
        public float DotSize = 0.06f;
        public float DotAngularStep = 4f;
        [Range(0.1f, 0.9f)] public float DashRatio = 0.4f;
        public string SortingLayerName = "Default";
        public int SortingOrder = 27;

        private Sprite _dotSprite;
        private readonly List<RingPool> _rings = new();

        private class RingPool
        {
            public GameObject Root;
            public SpriteRenderer[] Dots;
            public int DotCount;
            public bool IsVisible;
        }

        private void Start()
        {
            _dotSprite = SpriteUtility.CreateCircleSprite(16);
        }

        private void OnDestroy()
        {
            foreach (var ring in _rings)
                if (ring.Root != null) Destroy(ring.Root);
            _rings.Clear();
            if (_dotSprite != null) { Destroy(_dotSprite.texture); Destroy(_dotSprite); }
        }

        private void Update()
        {
            if (GalaxyManager.Instance?.Phase == TurnPhase.Simulation)
            {
                HideAll();
                return;
            }

            var ship = PlayerShip.Instance;
            if (ship == null || !ship.IsWeaponModeActive)
            {
                HideAll();
                return;
            }

            var ranges = CollectWeaponRanges(ship.ShipData);
            Vector2 center = ship.transform.position;

            while (_rings.Count < ranges.Count)
                _rings.Add(CreatePool());

            for (int i = 0; i < ranges.Count; i++)
                ShowRing(_rings[i], center, ranges[i]);

            for (int i = ranges.Count; i < _rings.Count; i++)
                SetVisible(_rings[i], false);
        }

        private RingPool CreatePool()
        {
            var pool = new RingPool();
            pool.Root = new GameObject("WeaponRingIndicator");
            pool.Root.transform.SetParent(null);
            pool.Root.SetActive(false);
            return pool;
        }

        private void ShowRing(RingPool pool, Vector2 center, float radius)
        {
            int needed = Mathf.RoundToInt(360f / DotAngularStep);
            if (pool.Dots == null || pool.DotCount != needed)
                RebuildPool(pool, needed);

            pool.Root.SetActive(true);
            pool.IsVisible = true;
            float dashPeriod = 1f / Mathf.Max(0.01f, DashRatio);

            for (int i = 0; i < pool.DotCount; i++)
            {
                float angleRad = i * DotAngularStep * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;
                var sr = pool.Dots[i];
                sr.transform.position = new Vector3(pos.x, pos.y, 0f);
                sr.enabled = (i % dashPeriod) < (dashPeriod * DashRatio);
            }
        }

        private void SetVisible(RingPool pool, bool visible)
        {
            if (pool.IsVisible == visible) return;
            pool.IsVisible = visible;
            if (pool.Root != null) pool.Root.SetActive(visible);
        }

        private void HideAll()
        {
            foreach (var ring in _rings)
                SetVisible(ring, false);
        }

        private void RebuildPool(RingPool pool, int needed)
        {
            if (pool.Dots != null)
                foreach (var d in pool.Dots)
                    if (d != null) Destroy(d.gameObject);

            pool.DotCount = needed;
            pool.Dots = new SpriteRenderer[needed];

            for (int i = 0; i < needed; i++)
            {
                var go = new GameObject($"WDot_{i}");
                go.transform.SetParent(pool.Root.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _dotSprite;
                sr.color = RingColor;
                sr.sortingLayerName = SortingLayerName;
                sr.sortingOrder = SortingOrder;
                go.transform.localScale = Vector3.one * DotSize;
                pool.Dots[i] = sr;
            }
        }

        private static List<float> CollectWeaponRanges(ShipData ship)
        {
            var result = new List<float>();
            if (ship == null) return result;

            var seen = new HashSet<float>();
            foreach (var slotKey in ship.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Weapons))
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid == null || !ship.AllItems.TryGetValue(uid, out var weapon)) continue;
                if (!weapon.IsWorking) continue;
                float range = SRUnits.ToWorld(weapon.GetParam("Range", 0f));
                if (range > 0f && seen.Add(range))
                    result.Add(range);
            }
            return result;
        }
    }
}
