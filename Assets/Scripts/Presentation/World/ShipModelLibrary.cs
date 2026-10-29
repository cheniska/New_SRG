using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Модели кораблей для 3D-визуала (<see cref="ShipVisualController3D"/>).
    ///
    /// Соглашение для префабов: <c>Resources/Models/…</c> повторяет путь листа корпуса в
    /// <c>Resources/Graphics/…</c> (<see cref="ModelPathFor"/>). Модель смотрит носом по +Y,
    /// верхом к камере (−Z) и вписана в квадрат 1×1 в плане — масштаб до кадра листа ставит визуал.
    /// Шейдер <c>Shaders/ShipModel</c>: плоское освещение от фиксированного направления плюс
    /// <c>_Alpha</c> для растворения при посадке и гиперпереходе. Пока модели нет — заглушка-«дротик»
    /// в цвете расы корабля.
    /// </summary>
    public static class ShipModelLibrary
    {
        public const string ShaderPath = "Shaders/ShipModel";
        public static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static readonly Color PlayerColor = new Color(0.45f, 0.85f, 1f);
        private static readonly Color ItemColor   = new Color(1f, 0.8f, 0.35f);
        private static readonly Color NeutralColor = new Color(0.7f, 0.7f, 0.72f);

        private static Mesh _placeholderMesh;
        private static Shader _shader;
        private static readonly Dictionary<Color, Material> _materials = new();

        /// <summary>Resources-путь модели по пути листа корпуса: <c>Graphics/…</c> → <c>Models/…</c>.</summary>
        public static string ModelPathFor(string sheetPath)
        {
            if (string.IsNullOrEmpty(sheetPath)) return null;
            const string graphics = "Graphics/";
            return sheetPath.StartsWith(graphics, System.StringComparison.Ordinal)
                ? "Models/" + sheetPath.Substring(graphics.Length)
                : "Models/" + sheetPath;
        }

        /// <summary>Цвет заглушки: игрок, предмет в космосе или цвет расы корабля.</summary>
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

        /// <summary>Заглушка корпуса: дочерний объект с процедурной моделью.</summary>
        public static GameObject CreatePlaceholder(Transform parent, Color color)
        {
            var go = new GameObject("Body3D");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = PlaceholderMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            return go;
        }

        private static Material MaterialFor(Color color)
        {
            if (_materials.TryGetValue(color, out var m) && m != null) return m;
            _shader ??= Resources.Load<Shader>(ShaderPath);
            if (_shader == null)
            {
                Debug.LogError($"[ShipModelLibrary] Шейдер не найден в Resources: {ShaderPath}");
                _shader = Shader.Find("Sprites/Default");
            }
            m = new Material(_shader) { name = "ShipPlaceholder" };
            m.SetColor(ColorId, color);
            _materials[color] = m;
            return m;
        }

        /// <summary>«Дротик»: гранёная пирамида с вырезом в корме. Вершина-гребень на z = 0
        /// (к камере), основание на z = Depth; нос по +Y, в плане — квадрат 1×1.</summary>
        private static Mesh PlaceholderMesh => _placeholderMesh != null ? _placeholderMesh : (_placeholderMesh = BuildPlaceholderMesh());

        private const float Depth = 0.18f;

        private static Mesh BuildPlaceholderMesh()
        {
            var top   = new Vector3(0f, 0.05f, 0f);
            var nose  = new Vector3(0f, 0.5f, Depth);
            var left  = new Vector3(-0.45f, -0.45f, Depth);
            var notch = new Vector3(0f, -0.25f, Depth);
            var right = new Vector3(0.45f, -0.45f, Depth);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            // Видимые грани — нормалью к камере (−Z), основание — от камеры (+Z).
            AddTri(verts, tris, top, nose, left, Vector3.back);
            AddTri(verts, tris, top, left, notch, Vector3.back);
            AddTri(verts, tris, top, notch, right, Vector3.back);
            AddTri(verts, tris, top, right, nose, Vector3.back);
            AddTri(verts, tris, nose, right, notch, Vector3.forward);
            AddTri(verts, tris, nose, notch, left, Vector3.forward);

            var mesh = new Mesh { name = "ShipPlaceholder" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();   // вершины не общие — нормали плоские, грани читаются
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Треугольник с обходом, при котором лицевая сторона смотрит в сторону <paramref name="facing"/>
        /// (в Unity лицевая нормаль = Cross(b − a, c − a)).</summary>
        private static void AddTri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }
    }
}
