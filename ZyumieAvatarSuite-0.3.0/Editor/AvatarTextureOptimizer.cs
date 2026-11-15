using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    internal sealed class TextureOptimizationResult
    {
        public int materialsCloned;
        public int texturesCopied;
        public int texturesOptimized;
        public int skippedTextures;
        public string outputFolder;
        public string message;
    }

    internal static class AvatarTextureOptimizer
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".hdr" };

        public static TextureOptimizationResult OptimizeOnAvatarCopy(GameObject avatarCopy, int maxSize, int compressionQuality)
        {
            if (!avatarCopy) throw new ArgumentNullException(nameof(avatarCopy));
            maxSize = Mathf.Clamp(maxSize, 32, 4096);
            compressionQuality = Mathf.Clamp(compressionQuality, 0, 100);

            string folder = "Assets/ZyumieAvatarSuite/Generated/" + Sanitize(avatarCopy.name);
            EnsureFolder(folder);
            string textureFolder = folder + "/Textures";
            string materialFolder = folder + "/Materials";
            EnsureFolder(textureFolder);
            EnsureFolder(materialFolder);

            var result = new TextureOptimizationResult { outputFolder = folder };
            var textureCopies = new Dictionary<Texture, Texture>();
            var materialCopies = new Dictionary<Material, Material>();

            foreach (var renderer in avatarCopy.GetComponentsInChildren<Renderer>(true))
            {
                var sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0) continue;

                var assigned = new Material[sourceMaterials.Length];
                bool changed = false;

                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    Material source = sourceMaterials[i];
                    if (!source) { assigned[i] = source; continue; }

                    Material cloned;
                    if (!materialCopies.TryGetValue(source, out cloned))
                    {
                        string materialPath = AssetDatabase.GenerateUniqueAssetPath(materialFolder + "/" + Sanitize(source.name) + ".mat");
                        cloned = new Material(source);
                        AssetDatabase.CreateAsset(cloned, materialPath);
                        materialCopies.Add(source, cloned);
                        result.materialsCloned++;

                        foreach (string property in cloned.GetTexturePropertyNames())
                        {
                            Texture originalTexture = cloned.GetTexture(property);
                            if (!originalTexture) continue;

                            Texture copiedTexture;
                            if (!textureCopies.TryGetValue(originalTexture, out copiedTexture))
                            {
                                copiedTexture = CopyAndOptimizeTexture(originalTexture, textureFolder, maxSize, compressionQuality, result);
                                textureCopies.Add(originalTexture, copiedTexture);
                            }
                            if (copiedTexture) cloned.SetTexture(property, copiedTexture);
                        }
                        EditorUtility.SetDirty(cloned);
                    }

                    assigned[i] = cloned;
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(renderer, "Zyumie Assign Optimized Materials");
                    renderer.sharedMaterials = assigned;
                    EditorUtility.SetDirty(renderer);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            result.message =
                "Optimisation terminée sur la copie sélectionnée.\n" +
                "Matériaux clonés : " + result.materialsCloned + "\n" +
                "Textures copiées : " + result.texturesCopied + "\n" +
                "Textures optimisées : " + result.texturesOptimized + "\n" +
                "Textures ignorées : " + result.skippedTextures + "\n\n" +
                "Assets générés : " + folder + "\n" +
                "Les textures et matériaux d'origine n'ont pas été modifiés.";
            return result;
        }

        private static Texture CopyAndOptimizeTexture(Texture source, string destinationFolder, int maxSize, int quality, TextureOptimizationResult result)
        {
            if (!(source is Texture2D)) { result.skippedTextures++; return source; }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath)) { result.skippedTextures++; return source; }

            string extension = Path.GetExtension(sourcePath);
            if (!SupportedExtensions.Contains(extension)) { result.skippedTextures++; return source; }

            string destination = AssetDatabase.GenerateUniqueAssetPath(
                destinationFolder + "/" + Sanitize(Path.GetFileNameWithoutExtension(sourcePath)) + extension);
            if (!AssetDatabase.CopyAsset(sourcePath, destination)) { result.skippedTextures++; return source; }
            result.texturesCopied++;

            var importer = AssetImporter.GetAtPath(destination) as TextureImporter;
            if (!importer) { result.skippedTextures++; return AssetDatabase.LoadAssetAtPath<Texture2D>(destination); }

            if (importer.maxTextureSize > maxSize) importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = quality;

            var android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = Mathf.Min(android.maxTextureSize > 0 ? android.maxTextureSize : maxSize, maxSize);
            android.format = TextureImporterFormat.Automatic;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.compressionQuality = quality;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();

            result.texturesOptimized++;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        private static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return string.IsNullOrEmpty(value) ? "Avatar" : value.Replace("/", "_").Replace("\\", "_");
        }
    }
}
