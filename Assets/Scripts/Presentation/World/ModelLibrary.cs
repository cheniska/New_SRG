using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Общее для 3D-визуалов объектов системы: пути моделей, материалы, процедурные меши и
    /// размещение модели относительно плоскости спрайтов.
    ///
    /// Соглашение для префабов кораблей: <c>Resources/Models/…</c> повторяет путь листа корпуса в
    /// <c>Resources/Graphics/…</c> (<see cref="ModelPathFor"/>). Модель смотрит носом по +Y,
    /// верхом к камере (−Z) и вписана в квадрат 1×1 в плане — масштаб до кадра листа ставит визуал.
    ///
    /// Шейдеры: <c>Shaders/ModelLit</c> (корабли, астероиды — плоский Ламберт от звезды, <c>_Alpha</c>)
    /// и <c>Shaders/PlanetSphere3D</c> (планеты, спутники, облака — текстура как в 2D + день/ночь).
    /// </summary>
    public static class ModelLibrary
    {
        public const string LitShaderPath    = "Shaders/ModelLit";
        public const string PlanetShaderPath = "Shaders/PlanetSphere3D";

        public static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static readonly Color PlayerColor  = new Color(0.45f, 0.85f, 1f);
        private static readonly Color ItemColor    = new Color(1f, 0.8f, 0.35f);
        private static readonly Color NeutralColor = new Color(0.7f, 0.7f, 0.72f);

        private static Shader _litShader, _planetShader;
        private static readonly Dictionary<Color, Material> _litMaterials = new();
        private static Material _planetMaterial;
        private static Mesh _shipPlaceholder, _sphere;
        private static readonly Dictionary<int, Mesh> _rocks = new();

        // ── Пути и цвета ─────────────────────────────────────────────────────────

        /// <summary>Resources-путь модели по пути листа: <c>Graphics/…</c> → <c>Models/…</c>.</summary>
        public static string ModelPathFor(string sheetPath)
        {
            if (string.IsNullOrEmpty(sheetPath)) return null;
            const string graphics = "Graphics/";
            return sheetPath.StartsWith(graphics, System.StringComparison.Ordinal)
                ? "Models/" + sheetPath.Substring(graphics.Length)
                : "Models/" + sheetPath;
        }

        /// <summary>Цвет заглушки корабля: игрок, предмет в космосе или цвет расы.</summary>
        public static Color ColorFor(ShipData ship)
        {
            if (ship == null) return NeutralColor;
            if (ship.IsPlayer) return PlayerColor;
            if (ship.IsItem) return ItemColor;
            var races = GameWorld.Context?.AvailableRaces;
            if (races != null && !string.IsNullOrEmpty(ship.Race) && races.TryGetValue(ship.Race, out var race)
                && ColorUtility.TryParseHtmlString(race.Color, out var c))
                return c;
            return NeutralColor;
        }

        // ── Материалы ────────────────────────────────────────────────────────────

        /// <summary>Общий материал <c>ModelLit</c> заданного цвета (один на цвет).</summary>
        public static Material LitMaterial(Color color)
        {
            if (_litMaterials.TryGetValue(color, out var m) && m != null) return m;
            m = new Material(LoadShader(ref _litShader, LitShaderPath)) { name = "ModelLit" };
            m.SetColor(ColorId, color);
            _litMaterials[color] = m;
            return m;
        }

        /// <summary>Общий материал <c>PlanetSphere3D</c>; текстура, вращение и наклон — через MaterialPropertyBlock.</summary>
        public static Material PlanetMaterial
            => _planetMaterial != null
                ? _planetMaterial
                : (_planetMaterial = new Material(LoadShader(ref _planetShader, PlanetShaderPath)) { name = "PlanetSphere3D" });

        private static Shader LoadShader(ref Shader cache, string path)
        {
            if (cache != null) return cache;
            cache = Resources.Load<Shader>(path);
            if (cache == null)
            {
                Debug.LogError($"[ModelLibrary] Шейдер не найден в Resources: {path}");
                cache = Shader.Find("Sprites/Default");
            }
            return cache;
        }

        // ── Объекты ──────────────────────────────────────────────────────────────

        /// <summary>Дочерний объект с мешем и материалом.</summary>
        public static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Заглушка корпуса корабля в цвете <paramref name="color"/>.</summary>
        public static GameObject CreateShipPlaceholder(Transform parent, Color color)
            => CreateMeshObject("Body3D", parent, ShipPlaceholderMesh, LitMaterial(color));

        /// <summary>
        /// Сдвигает <paramref name="model"/> по z так, чтобы ближайшая к камере точка его
        /// рендереров легла на плоскость родителя (z = 0 системы). Модели с ZWrite тогда не
        /// перекрывают спрайты, которые по sortingOrder должны быть выше.
        /// </summary>
        public static void PlaceBehindSystemPlane(Transform model, Renderer[] renderers)
        {
            if (model == null || renderers == null || renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float planeZ = model.parent != null ? model.parent.position.z : 0f;
            var p = model.position;
            model.position = new Vector3(p.x, p.y, p.z + (planeZ - bounds.min.z));
        }

        // ── Процедурные меши ─────────────────────────────────────────────────────

        /// <summary>«Дротик»: гранёная пирамида с вырезом в корме. Вершина-гребень на z = 0
        /// (к камере), основание на z = 0.18; нос по +Y, в плане — квадрат 1×1.</summary>
        public static Mesh ShipPlaceholderMesh => _shipPlaceholder != null ? _shipPlaceholder : (_shipPlaceholder = BuildShipPlaceholder());

        /// <summary>UV-сфера радиуса 0.5 (диаметр 1 — как квад спрайта планеты под маской).</summary>
        public static Mesh SphereMesh => _sphere != null ? _sphere : (_sphere = BuildSphere(48, 24));

        /// <summary>Гранёный камень радиуса ~0.5, форма определяется <paramref name="seed"/>
        /// (вариантов немного — меши общие).</summary>
        public static Mesh RockMesh(int seed)
        {
            int variant = ((seed % RockVariants) + RockVariants) % RockVariants;
            if (_rocks.TryGetValue(variant, out var m) && m != null) return m;
            m = BuildRock(variant);
            _rocks[variant] = m;
            return m;
        }

        private const int RockVariants = 8;

        private static Mesh BuildShipPlaceholder()
        {
            const float depth = 0.18f;
            var top   = new Vector3(0f, 0.05f, 0f);
            var nose  = new Vector3(0f, 0.5f, depth);
            var left  = new Vector3(-0.45f, -0.45f, depth);
            var notch = new Vector3(0f, -0.25f, depth);
            var right = new Vector3(0.45f, -0.45f, depth);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            // Видимые грани — нормалью к камере (−Z), основание — от камеры (+Z).
            AddTri(verts, tris, top, nose, left, Vector3.back);
            AddTri(verts, tris, top, left, notch, Vector3.back);
            AddTri(verts, tris, top, notch, right, Vector3.back);
            AddTri(verts, tris, top, right, nose, Vector3.back);
            AddTri(verts, tris, nose, right, notch, Vector3.forward);
            AddTri(verts, tris, nose, notch, left, Vector3.forward);
            return Faceted("ShipPlaceholder", verts, tris);
        }

        private static Mesh BuildSphere(int segments, int rings)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.PI * r / rings - Mathf.PI / 2f;
                for (int s = 0; s <= segments; s++)
                {
                    float lon = 2f * Mathf.PI * s / segments;
                    var n = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    normals.Add(n);
                    verts.Add(n * 0.5f);
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s, b = a + 1, c = a + row, d = c + 1;
                    AddTriOutward(verts, tris, a, b, c);
                    AddTriOutward(verts, tris, b, d, c);
                }
            var mesh = new Mesh { name = "Sphere" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Низкополигональный камень: октаэдр, дважды подразделённый, со случайным
        /// смещением вершин по радиусу (детерминированно от варианта).</summary>
        private static Mesh BuildRock(int variant)
        {
            var rng = new System.Random(7919 * (variant + 1));
            var pts = new List<Vector3>
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
            };
            var faces = new List<int[]>
            {
                new[] { 2, 0, 4 }, new[] { 2, 4, 1 }, new[] { 2, 1, 5 }, new[] { 2, 5, 0 },
                new[] { 3, 4, 0 }, new[] { 3, 1, 4 }, new[] { 3, 5, 1 }, new[] { 3, 0, 5 },
            };
            for (int step = 0; step < 2; step++) faces = Subdivide(pts, faces);

            for (int i = 0; i < pts.Count; i++)
            {
                float radius = 0.5f * (0.72f + 0.42f * (float)rng.NextDouble());
                var p = pts[i].normalized * radius;
                pts[i] = new Vector3(p.x * 1.1f, p.y * 0.9f, p.z * 0.85f);   // чуть вытянутый
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in faces)
            {
                Vector3 a = pts[f[0]], b = pts[f[1]], c = pts[f[2]];
                AddTri(verts, tris, a, b, c, (a + b + c).normalized);   // наружу
            }
            return Faceted("Rock" + variant, verts, tris);
        }

        private static List<int[]> Subdivide(List<Vector3> pts, List<int[]> faces)
        {
            var mid = new Dictionary<long, int>();
            int Mid(int i, int j)
            {
                long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                if (mid.TryGetValue(key, out int m)) return m;
                pts.Add(((pts[i] + pts[j]) * 0.5f).normalized);
                mid[key] = pts.Count - 1;
                return pts.Count - 1;
            }
            var result = new List<int[]>(faces.Count * 4);
            foreach (var f in faces)
            {
                int ab = Mid(f[0], f[1]), bc = Mid(f[1], f[2]), ca = Mid(f[2], f[0]);
                result.Add(new[] { f[0], ab, ca });
                result.Add(new[] { f[1], bc, ab });
                result.Add(new[] { f[2], ca, bc });
                result.Add(new[] { ab, bc, ca });
            }
            return result;
        }

        private static Mesh Faceted(string name, List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();   // вершины не общие — нормали плоские, грани читаются
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Треугольник с обходом, при котором лицевая сторона смотрит в сторону <paramref name="facing"/>
        /// (в Unity лицевая нормаль = Cross(b − a, c − a)). Вершины не общие.</summary>
        private static void AddTri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>Треугольник по индексам общих вершин сферы — лицевой стороной наружу.</summary>
        private static void AddTriOutward(List<Vector3> verts, List<int> tris, int a, int b, int c)
        {
            Vector3 va = verts[a], vb = verts[b], vc = verts[c];
            bool outward = Vector3.Dot(Vector3.Cross(vb - va, vc - va), va + vb + vc) >= 0f;
            tris.Add(a);
            tris.Add(outward ? b : c);
            tris.Add(outward ? c : b);
        }
    }
}
