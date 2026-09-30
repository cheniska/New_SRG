// Минимальный управляемый заменитель UnityEngine для прогона тестов симуляции вне движка
// (tools/headless-tests). Математика повторяет семантику Unity; всё, что требует движка, — заглушки.
// Если симуляция начала использовать новый API UnityEngine — добавить его сюда.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityEngine
{
    public struct Vector2 : IEquatable<Vector2>
    {
        public const float kEpsilon = 1e-5f;
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float this[int i] { get => i == 0 ? x : i == 1 ? y : throw new IndexOutOfRangeException(); set { if (i == 0) x = value; else if (i == 1) y = value; else throw new IndexOutOfRangeException(); } }
        public void Set(float nx, float ny) { x = nx; y = ny; }
        public Vector2 normalized { get { var v = new Vector2(x, y); v.Normalize(); return v; } }
        public void Normalize() { float m = magnitude; if (m > kEpsilon) this = this / m; else this = zero; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 down => new Vector2(0, -1);
        public static Vector2 left => new Vector2(-1, 0);
        public static Vector2 right => new Vector2(1, 0);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vector2 a, Vector2 b) { float dx = a.x - b.x, dy = a.y - b.y; return (float)Math.Sqrt(dx * dx + dy * dy); }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) => new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        public static Vector2 MoveTowards(Vector2 c, Vector2 t, float d) { float tx = t.x - c.x, ty = t.y - c.y; float sq = tx * tx + ty * ty; if (sq == 0 || (d >= 0 && sq <= d * d)) return t; float dist = (float)Math.Sqrt(sq); return new Vector2(c.x + tx / dist * d, c.y + ty / dist * d); }
        public static float Angle(Vector2 from, Vector2 to) { float den = (float)Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude); if (den < 1e-15f) return 0f; float dot = Mathf.Clamp(Dot(from, to) / den, -1f, 1f); return (float)Math.Acos(dot) * Mathf.Rad2Deg; }
        public static float SignedAngle(Vector2 from, Vector2 to) { float u = Angle(from, to); float s = Math.Sign(from.x * to.y - from.y * to.x); return u * s; }
        public static Vector2 ClampMagnitude(Vector2 v, float max) { float sq = v.sqrMagnitude; if (sq > max * max) { float m = (float)Math.Sqrt(sq); return new Vector2(v.x / m * max, v.y / m * max); } return v; }
        public static Vector2 Perpendicular(Vector2 d) => new Vector2(-d.y, d.x);
        public static Vector2 Reflect(Vector2 d, Vector2 n) { float f = -2f * Dot(n, d); return new Vector2(f * n.x + d.x, f * n.y + d.y); }
        public static Vector2 Scale(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public static Vector2 Min(Vector2 a, Vector2 b) => new Vector2(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new Vector2(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public static Vector2 operator /(Vector2 a, Vector2 b) => new Vector2(a.x / b.x, a.y / b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 l, Vector2 r) { float dx = l.x - r.x, dy = l.y - r.y; return dx * dx + dy * dy < kEpsilon * kEpsilon; }
        public static bool operator !=(Vector2 l, Vector2 r) => !(l == r);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public bool Equals(Vector2 o) => x == o.x && y == o.y;
        public override bool Equals(object o) => o is Vector2 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2})", x, y);
        public string ToString(string f) => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", x.ToString(f, CultureInfo.InvariantCulture), y.ToString(f, CultureInfo.InvariantCulture));
    }

    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 l, Vector3 r) => (l - r).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 l, Vector3 r) => !(l == r);
        public bool Equals(Vector3 o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object o) => o is Vector3 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
    }

    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; } }
    public struct Vector3Int { public int x, y, z; public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; } }
    public struct Quaternion { public float x, y, z, w; public static Quaternion identity => new Quaternion { w = 1 }; public static Quaternion Euler(float x, float y, float z) => identity; }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) : this(r, g, b, 1f) { }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color red => new Color(1, 0, 0, 1);
        public static Color green => new Color(0, 1, 0, 1);
        public static Color blue => new Color(0, 0, 1, 1);
        public static Color yellow => new Color(1, 0.92156863f, 0.015686275f, 1);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1);
        public static Color grey => gray;
        public static Color cyan => new Color(0, 1, 1, 1);
        public static Color magenta => new Color(1, 0, 1, 1);
        public static Color Lerp(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t); }
        public static Color operator *(Color c, float d) => new Color(c.r * d, c.g * d, c.b * d, c.a * d);
        public static bool operator ==(Color l, Color r) => l.r == r.r && l.g == r.g && l.b == r.b && l.a == r.a;
        public static bool operator !=(Color l, Color r) => !(l == r);
        public override bool Equals(object o) => o is Color c && c == this;
        public override int GetHashCode() => r.GetHashCode() ^ g.GetHashCode() << 2 ^ b.GetHashCode() >> 2 ^ a.GetHashCode() >> 1;
    }
    public struct Color32 { public byte r, g, b, a; public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; } }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public Rect(Vector2 pos, Vector2 size) : this(pos.x, pos.y, size.x, size.y) { }
        public float xMin => x; public float yMin => y; public float xMax => x + width; public float yMax => y + height;
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public Vector2 size => new Vector2(width, height);
        public bool Contains(Vector2 p) => p.x >= xMin && p.x < xMax && p.y >= yMin && p.y < yMax;
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Deg2Rad = PI * 2f / 360f;
        public const float Rad2Deg = 1f / Deg2Rad;
        public static readonly float Epsilon = float.Epsilon;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Acos(float f) => (float)Math.Acos(f);
        public static float Atan(float f) => (float)Math.Atan(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int v) => Math.Abs(v);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Min(params float[] v) { if (v.Length == 0) return 0; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i]; return m; }
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Min(params int[] v) { if (v.Length == 0) return 0; int m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i]; return m; }
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Max(params float[] v) { if (v.Length == 0) return 0; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i]; return m; }
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Max(params int[] v) { if (v.Length == 0) return 0; int m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i]; return m; }
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Exp(float p) => (float)Math.Exp(p);
        public static float Log(float f, float p) => (float)Math.Log(f, p);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Log10(float f) => (float)Math.Log10(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float LerpAngle(float a, float b, float t) { float d = Repeat(b - a, 360); if (d > 180) d -= 360; return a + d * Clamp01(t); }
        public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Sign(t - c) * d;
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
        public static bool Approximately(float a, float b) => Math.Abs(b - a) < Math.Max(1e-6f * Math.Max(Math.Abs(a), Math.Abs(b)), Epsilon * 8);
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);
        public static float PingPong(float t, float length) { t = Repeat(t, length * 2f); return length - Math.Abs(t - length); }
        public static float DeltaAngle(float c, float t) { float d = Repeat(t - c, 360f); if (d > 180f) d -= 360f; return d; }
        public static bool IsPowerOfTwo(int v) => (v & (v - 1)) == 0;
        public static int NextPowerOfTwo(int v) { v -= 1; v |= v >> 16; v |= v >> 8; v |= v >> 4; v |= v >> 2; v |= v >> 1; return v + 1; }
    }

    public enum LogType { Error, Assert, Warning, Log, Exception }
    public interface ILogHandler { void LogFormat(LogType logType, Object context, string format, params object[] args); void LogException(Exception exception, Object context); }
    public static class Debug
    {
        public static bool Verbose = Environment.GetEnvironmentVariable("SRG_TEST_VERBOSE") == "1";
        public static void Log(object m) { if (Verbose) Console.WriteLine(m); }
        public static void Log(object m, Object c) => Log(m);
        public static void LogFormat(string f, params object[] a) => Log(string.Format(f, a));
        public static void LogWarning(object m) { if (Verbose) Console.WriteLine("[W] " + m); }
        public static void LogWarning(object m, Object c) => LogWarning(m);
        public static void LogWarningFormat(string f, params object[] a) => LogWarning(string.Format(f, a));
        public static void LogError(object m) => Console.Error.WriteLine("[E] " + m);
        public static void LogError(object m, Object c) => LogError(m);
        public static void LogErrorFormat(string f, params object[] a) => LogError(string.Format(f, a));
        public static void LogException(Exception e) => Console.Error.WriteLine("[X] " + e);
        public static void LogException(Exception e, Object c) => LogException(e);
        public static void Assert(bool c) { }
        public static void Assert(bool c, object m) { }
        public static void DrawLine(Vector3 a, Vector3 b, Color c, float d = 0) { }
    }

    public static class Application
    {
        public static string persistentDataPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "srg-tests");
        public static string dataPath => System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets");
        public static bool runInBackground { get; set; }
        public static bool isPlaying => false;
        public static bool isEditor => false;
    }

    public static class Time { public static float deltaTime => 0f; public static float unscaledDeltaTime => 0f; public static float time => 0f; public static float realtimeSinceStartup => 0f; public static int frameCount => 0; }

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object o) { }
        public static void Destroy(Object o, float t) { }
        public static void DestroyImmediate(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static T FindAnyObjectByType<T>() where T : Object => null;
        public static T Instantiate<T>(T o) where T : Object => o;
        public static implicit operator bool(Object o) => o is not null;
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject?.transform;
        public T GetComponent<T>() where T : class => gameObject?.GetComponent<T>();
        public bool TryGetComponent<T>(out T c) where T : class { c = GetComponent<T>(); return c != null; }
        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
    }
    public class Behaviour : Component { public bool enabled { get; set; } = true; public bool isActiveAndEnabled => enabled; }
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(System.Collections.IEnumerator r) => null;
        public void StopCoroutine(Coroutine c) { }
        public void StopAllCoroutines() { }
        public void Invoke(string m, float t) { }
    }
    public class Coroutine { }
    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; } = Vector3.one;
        public Quaternion rotation { get; set; }
        public Transform parent { get; private set; }
        public void SetParent(Transform p, bool worldPositionStays = true) => parent = p;
    }
    public class GameObject : Object
    {
        private readonly List<Component> _components = new();
        public Transform transform { get; }
        public GameObject() : this("GameObject") { }
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; _components.Add(transform); }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; _components.Add(c); return c; }
        public T GetComponent<T>() where T : class { foreach (var c in _components) if (c is T t) return t; return null; }
        public bool TryGetComponent<T>(out T c) where T : class { c = GetComponent<T>(); return c != null; }
        public void SetActive(bool v) { activeSelf = v; }
        public bool activeSelf { get; private set; } = true;
    }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); }
    public class TextAsset : Object { public TextAsset() { } public TextAsset(string text) { this.text = text; } public string text { get; } }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureFormat { RGBA32 = 4, ARGB32 = 5, RGB24 = 3 }
    public enum TextureWrapMode { Repeat, Clamp }
    public class Texture : Object { public int width { get; protected set; } public int height { get; protected set; } public FilterMode filterMode { get; set; } public TextureWrapMode wrapMode { get; set; } }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h) { width = w; height = h; }
        public Texture2D(int w, int h, TextureFormat f, bool mip) : this(w, h) { }
        public void SetPixel(int x, int y, Color c) { }
        public void SetPixels(Color[] c) { }
        public void SetPixels32(Color32[] c) { }
        public void Apply() { }
        public void Apply(bool a, bool b = false) { }
    }
    public class Sprite : Object
    {
        public Rect rect { get; private set; }
        public float pixelsPerUnit { get; private set; } = 100f;
        public Texture2D texture { get; private set; }
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu = 100f) => new Sprite { texture = t, rect = r, pixelsPerUnit = ppu };
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu, uint extrude, SpriteMeshType m) => Create(t, r, pivot, ppu);
    }
    public enum SpriteMeshType { FullRect, Tight }
    public class Font : Object { }
    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static Object Load(string path) => null;
        public static T[] LoadAll<T>(string path) where T : Object => Array.Empty<T>();
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }
    public enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)] public class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public class MinAttribute : Attribute { public MinAttribute(float a) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public class SpaceAttribute : Attribute { public SpaceAttribute() { } public SpaceAttribute(float h) { } }
    [AttributeUsage(AttributeTargets.Field)] public class TextAreaAttribute : Attribute { public TextAreaAttribute() { } public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.Field)] public class MultilineAttribute : Attribute { public MultilineAttribute() { } public MultilineAttribute(int l) { } }
    [AttributeUsage(AttributeTargets.Class)] public class CreateAssetMenuAttribute : Attribute { public string fileName { get; set; } public string menuName { get; set; } public int order { get; set; } }
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public class RequireComponent : Attribute { public RequireComponent(Type t) { } }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute() { } public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
}
namespace UnityEngine
{
    public enum KeyCode { None = 0, Space = 32, BackQuote = 96, C = 99, F = 102, I = 105, Q = 113, T = 116, Home = 278, LeftShift = 304 }
}

namespace UnityEngine
{
    public static class ColorUtility
    {
        static int B(float v) => (int)Math.Round(Mathf.Clamp01(v) * 255f);
        public static string ToHtmlStringRGB(Color c) => $"{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}";
        public static string ToHtmlStringRGBA(Color c) => $"{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}{B(c.a):X2}";
        public static bool TryParseHtmlString(string s, out Color c)
        {
            c = Color.white; if (string.IsNullOrEmpty(s)) return false; s = s.TrimStart('#');
            if (s.Length != 6 && s.Length != 8) return false;
            try { float P(int i) => Convert.ToInt32(s.Substring(i, 2), 16) / 255f; c = new Color(P(0), P(2), P(4), s.Length == 8 ? P(6) : 1f); return true; }
            catch { return false; }
        }
    }
}
