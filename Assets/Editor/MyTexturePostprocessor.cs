using UnityEngine;
using UnityEditor;

namespace SRG.EditorTools
{
    public class MyTexturePostprocessor : AssetPostprocessor
    {

    // Метод вызывается ПЕРЕД тем, как Unity импортирует текстуру
        void OnPreprocessTexture()
        {
            // Укажите часть пути, характерную только для ваших спрайт-листов
            if (assetPath.Contains("Resources/Graphics/SpaceObjects/Stars") || assetPath.Contains("Resources/Graphics/SpaceObjects/Planets/Textures"))
            {
                TextureImporter importer = (TextureImporter)assetImporter;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.wrapMode = TextureWrapMode.Repeat;
                // Настройки качества и размера
                importer.maxTextureSize = 8192; // Чтобы не ужимало 4k и 8k листы
                importer.textureCompression = TextureImporterCompression.Uncompressed; // Идеальное качество
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false; // Для 2D анимаций мип-мапы обычно не нужны (экономят 33% памяти)
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None; // Важно для вашей математики координат

                // Если текстуры очень большие, можно разрешить чтение/запись (но это ест в 2 раза больше памяти)
                // importer.isReadable = false; 
            }
        }
    }
}
