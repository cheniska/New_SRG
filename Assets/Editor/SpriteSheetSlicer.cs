using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace SRG.EditorTools
{
    public class SpriteSheetSlicer : AssetPostprocessor
    {
        // Runs after MyTexturePostprocessor (default order 0)
        public override int GetPostprocessOrder() => 100;

        void OnPreprocessTexture()
        {
            string jsonPath = assetPath + ".json";
            if (!File.Exists(jsonPath)) return;

            var data = ReadJson(jsonPath);
            if (data == null) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;

            string baseName = Path.GetFileNameWithoutExtension(
                Path.GetFileNameWithoutExtension(assetPath)); // strip both .png and nothing
            int totalHeight = data.Rows * data.FrameHeight;

            var sprites = new SpriteRect[data.FrameCount];
            int frame = 0;
            for (int row = 0; row < data.Rows && frame < data.FrameCount; row++)
            {
                for (int col = 0; col < data.Cols && frame < data.FrameCount; col++)
                {
                    sprites[frame] = new SpriteRect
                    {
                        name = $"{baseName}_{frame}",
                        rect = new Rect(
                            col * data.FrameWidth,
                            totalHeight - (row + 1) * data.FrameHeight, // Unity Y from bottom
                            data.FrameWidth,
                            data.FrameHeight),
                        pivot = new Vector2(0.5f, 0.5f),
                        alignment = SpriteAlignment.Center
                    };
                    frame++;
                }
            }

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            dataProvider.InitSpriteEditorDataProvider();
            dataProvider.SetSpriteRects(sprites);
            dataProvider.Apply();
        }

        [MenuItem("Tools/Slice All Sprite Sheets from JSON")]
        static void SliceAll()
        {
            string root = Path.Combine(Application.dataPath, "Resources/Graphics");
            string[] jsonFiles = Directory.GetFiles(root, "*.png.json", SearchOption.AllDirectories);

            int count = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string absJsonPath in jsonFiles)
                {
                    string absImagePath = absJsonPath[..^5]; // strip ".json"
                    if (!File.Exists(absImagePath)) continue;

                    string relImagePath = "Assets" + absImagePath
                        .Replace(Application.dataPath, "")
                        .Replace('\\', '/');

                    AssetDatabase.ImportAsset(relImagePath, ImportAssetOptions.ForceUpdate);
                    count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[SpriteSheetSlicer] Sliced {count} sprite sheet(s).");
        }

        // Minimal JSON parser — avoids Newtonsoft dependency in Editor assembly
        static SpriteSheetData ReadJson(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                var data = new SpriteSheetData();
                data.FrameCount = ParseInt(text, "FrameCount");
                data.Cols       = ParseInt(text, "Cols");
                data.Rows       = ParseInt(text, "Rows");
                data.FrameWidth  = ParseInt(text, "FrameWidth");
                data.FrameHeight = ParseInt(text, "FrameHeight");
                if (data.FrameCount <= 0 || data.Cols <= 0 || data.Rows <= 0 ||
                    data.FrameWidth <= 0 || data.FrameHeight <= 0)
                    return null;
                return data;
            }
            catch
            {
                return null;
            }
        }

        static int ParseInt(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"", System.StringComparison.Ordinal);
            if (idx < 0) return 0;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return 0;
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            int end = start;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            return end > start && int.TryParse(json[start..end], out int v) ? v : 0;
        }

        class SpriteSheetData
        {
            public int FrameCount, Cols, Rows, FrameWidth, FrameHeight;
        }
    }
}
