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
                var originals = renderer.sharedMaterials;
                if (originals == null || originals.Length == 0) continue;
                var replacements = new Material[originals.Length];
                bool changed = false;

                for (int i = 0; i < originals.Length; i++)
                {
                    var original = originals[i];
                    if (!original) { replacements[i] = original; continue; }
                    if (!materialCopies.TryGetValue(original, out var materialCopy))
                    {
                        string materialPath = AssetDatabase.GenerateUniqueAssetPath(materialFolder + "/" + Sanitize(original.name) + ".mat");
                        materialCopy = new Material(original);
                        AssetDatabase.CreateAsset(materialCopy, materialPath);
                        materialCopies.Add(original, materialCopy);
                        result.materialsCloned++;

                        foreach (string property in materialCopy.GetTexturePropertyNames())
                        {
                            var texture = materialCopy.GetTexture(property);
                            if (!texture) continue;
                            if (!textureCopies.TryGetValue(texture, out var copiedTexture))
                            {
                                copiedTexture = CopyAndOptimizeTexture(texture, textureFolder, maxSize, compressionQuality, result);
                                textureCopies.Add(texture, copiedTexture);
                            }
                            if (copiedTexture) materialCopy.SetTexture(property, copiedTexture);
                        }
                        EditorUtility.SetDirty(materialCopy);
                    }
                    replacements[i] = materialCopy;
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(renderer, "Zyumie Assign Optimized Materials");
                    renderer.sharedMaterials = replacements;
                    EditorUtility.SetDirty(renderer);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            result.message = "Optimisation terminée sur la copie sélectionnée.\n" +
                "Matériaux clonés : " + result.materialsCloned + "\n" +
                "Textures copiées : " + result.texturesCopied + "\n" +
                "Textures optimisées : " + result.texturesOptimized + "\n" +
                "Textures ignorées : " + result.skippedTextures + "\n\nAssets générés : " + folder +
                "\nLes textures et matériaux d'origine n'ont pas été modifiés.";
            return result;
        }

        private static Texture CopyAndOptimizeTexture(Texture source, string folder, int maxSize, int quality, TextureOptimizationResult result)
        {
            if (!(source is Texture2D)) { result.skippedTextures++; return source; }
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || !SupportedExtensions.Contains(Path.GetExtension(sourcePath)))
            { result.skippedTextures++; return source; }

            string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Sanitize(Path.GetFileNameWithoutExtension(sourcePath)) + Path.GetExtension(sourcePath));
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
            path = path.Replace("\\", "/");
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
