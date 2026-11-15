using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3A.Editor;
using VRC.SDK3.Avatars.Components;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Creates and verifies VRChat per-platform overrides.
    /// It does not pretend that cloning a PC avatar automatically makes it Quest-compatible.
    /// </summary>
    internal static class AvatarPlatformBuilder
    {
        private const string AndroidSuffix = "_Android_Quest";

        public static string CreateOrUpdateAndroidOverride(GameObject baseAvatar)
        {
            if (!baseAvatar) return "Sélectionne d'abord l'avatar PC/Windows.";
            VRCAvatarDescriptor baseDescriptor = FindDescriptor(baseAvatar);
            if (!baseDescriptor) return "Aucun VRC Avatar Descriptor trouvé sur l'avatar de base.";

            string variantName = baseAvatar.name + AndroidSuffix;
            GameObject androidAvatar = FindSiblingOrRoot(baseAvatar, variantName);
            bool created = androidAvatar == null;
            try
            {
                if (created) androidAvatar = AvatarOperations.CreatePlatformCopy(baseAvatar, AndroidSuffix);
                if (!androidAvatar) throw new InvalidOperationException("La copie Android n'a pas pu être créée.");
                VRCAvatarDescriptor androidDescriptor = FindDescriptor(androidAvatar);
                if (!androidDescriptor) throw new InvalidOperationException("La copie Android ne contient pas de VRC Avatar Descriptor au niveau attendu.");
                if (androidDescriptor == baseDescriptor) throw new InvalidOperationException("La variante Android doit être un avatar distinct de l'avatar PC.");

                // Synced Expression Parameters must keep the same asset/order/type on both platform variants.
                // Reuse the base menu and parameter assets so FEO toggles do not silently diverge between platforms.
                Undo.RecordObject(androidDescriptor, "Zyumie Synchronize Android Expression Assets");
                androidDescriptor.customExpressions = baseDescriptor.customExpressions;
                androidDescriptor.expressionsMenu = baseDescriptor.expressionsMenu;
                androidDescriptor.expressionParameters = baseDescriptor.expressionParameters;
                EditorUtility.SetDirty(androidDescriptor);

                var current = PerPlatformOverrides.GetPlatformOverrides(baseAvatar);
                var updated = current != null
                    ? new List<PerPlatformOverrides.Option>(current)
                    : new List<PerPlatformOverrides.Option>();
                updated.RemoveAll(option => option.platform == BuildTarget.Android);
                updated.Add(new PerPlatformOverrides.Option
                {
                    platform = BuildTarget.Android,
                    avatar = androidDescriptor
                });

                Undo.RegisterCompleteObjectUndo(baseAvatar, "Zyumie Configure Android Platform Override");
                PerPlatformOverrides.SetPlatformOverrides(baseAvatar, updated);
                var verified = PerPlatformOverrides.GetPlatformOverrides(baseAvatar);
                bool found = verified != null && verified.Any(option =>
                    option.platform == BuildTarget.Android && option.avatar == androidDescriptor);
                if (!found) throw new InvalidOperationException("Le SDK n'a pas confirmé l'enregistrement de la variante Android.");

                EditorUtility.SetDirty(baseAvatar);
                EditorUtility.SetDirty(androidAvatar);
                EditorUtility.SetDirty(androidDescriptor);
                AssetDatabase.SaveAssets();

                return "OVERRIDE ANDROID ENREGISTRÉ\n\n" +
                    "Avatar principal (PC / Windows) : " + baseAvatar.name + "\n" +
                    "Avatar Android / Quest : " + androidAvatar.name + "\n\n" +
                    "L'override Android est bien enregistré sur l'avatar principal via PerPlatformOverrides.\n" +
                    "Le menu et l'asset Expression Parameters sont maintenant partagés avec la variante PC pour conserver les mêmes contrôles et l'ordre des paramètres synchronisés.\n" +
                    (created ? "Une copie brute de l'avatar PC vient d'être créée. Elle n'est pas encore optimisée pour Quest.\n" : "La copie Android existante a été réutilisée.\n") +
                    "\nÉTAPE OBLIGATOIRE : ouvre VRChat SDK > Builder, sélectionne l'avatar PRINCIPAL, puis lance Multi-Platform Build & Publish. " +
                    "Un simple Build & Publish PC ne publie pas la variante Android sous le même Avatar ID.\n\n" +
                    AuditAndroid(androidAvatar, baseAvatar, false);
            }
            catch (Exception ex)
            {
                if (created && androidAvatar) Undo.DestroyObjectImmediate(androidAvatar);
                Debug.LogException(ex);
                return "ÉCHEC DE LA CONFIGURATION ANDROID\n" + ex.GetBaseException().Message +
                    "\nAucune publication n'a été lancée. Vérifie que VRChat Avatars SDK 3.8.1+ est installé.";
            }
        }

        public static string AuditAndroid(GameObject androidAvatar, GameObject baseAvatar, bool includePublishSteps = true)
        {
            if (!androidAvatar) return "Sélectionne la variante Android à analyser.";
            var b = new StringBuilder();
            b.AppendLine("ZYUMIE AVATAR SUITE — AUDIT ANDROID / QUEST");
            b.AppendLine("Avatar Android : " + androidAvatar.name);
            VRCAvatarDescriptor androidDescriptor = FindDescriptor(androidAvatar);
            b.AppendLine("Descriptor Android : " + (androidDescriptor ? "présent" : "ABSENT"));
            if (androidDescriptor)
            {
                var descriptorData = new SerializedObject(androidDescriptor);
                descriptorData.Update();
                var faceEnabled = descriptorData.FindProperty("enableFaceTracking");
                var allKeys = descriptorData.FindProperty("useAllTrackingKeys");
                b.AppendLine("Face Tracking natif Android : " + (faceEnabled != null ? faceEnabled.boolValue.ToString() : "SDK sans champs natifs"));
                b.AppendLine("Use All Tracking Keys Android : " + (allKeys != null ? allKeys.boolValue.ToString() : "non disponible"));
            }

            if (baseAvatar)
            {
                var overrides = PerPlatformOverrides.GetPlatformOverrides(baseAvatar);
                bool registered = overrides != null && androidDescriptor && overrides.Any(option =>
                    option.platform == BuildTarget.Android && option.avatar == androidDescriptor);
                b.AppendLine("Override Android sur l'avatar principal : " + (registered ? "ENREGISTRÉ" : "NON ENREGISTRÉ"));
                var baseDescriptor = FindDescriptor(baseAvatar);
                if (baseDescriptor && androidDescriptor)
                    b.AppendLine("Même Expressions Parameters PC/Android : " + (baseDescriptor.expressionParameters == androidDescriptor.expressionParameters));
            }

            var materials = androidAvatar.GetComponentsInChildren<Renderer>(true)
                .Where(r => r).SelectMany(r => r.sharedMaterials ?? Array.Empty<Material>())
                .Where(m => m).Distinct().ToList();
            var unsupported = materials.Where(m => !IsKnownQuestShader(m.shader)).ToList();
            b.AppendLine("Matériaux trouvés : " + materials.Count);
            b.AppendLine("Shaders hors whitelist mobile courante : " + unsupported.Count);
            foreach (var material in unsupported.Take(30))
                b.AppendLine("  - " + material.name + " : " + (material.shader ? material.shader.name : "Shader manquant"));
            if (unsupported.Count > 30) b.AppendLine("  ... " + (unsupported.Count - 30) + " autre(s) matériau(x).");
            if (unsupported.Count > 0)
                b.AppendLine("AVERTISSEMENT : remplace ou configure ces shaders avec des shaders VRChat/Mobile compatibles, puis recontrôle le Builder Android.");

            long triangles = 0;
            int meshCount = 0;
            foreach (var skin in androidAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin || !skin.sharedMesh) continue;
                triangles += CountTriangles(skin.sharedMesh);
                meshCount++;
            }
            foreach (var filter in androidAvatar.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter || !filter.sharedMesh) continue;
                triangles += CountTriangles(filter.sharedMesh);
                meshCount++;
            }
            b.AppendLine("Meshes comptés : " + meshCount);
            b.AppendLine("Triangles bruts estimés : " + triangles);
            b.AppendLine("PhysBones, Contacts, constraints et scripts : conservés, non supprimés automatiquement.");
            b.AppendLine("Cet audit est indicatif : seul le VRChat SDK Builder peut confirmer les erreurs et le rang Android finaux.");

            if (includePublishSteps)
            {
                b.AppendLine("\nPUBLICATION CROSS-PLATFORM :");
                b.AppendLine("1. Sélectionne l'avatar principal PC dans SDK > Builder.");
                b.AppendLine("2. Vérifie qu'Android est listé dans Per-Platform Overrides.");
                b.AppendLine("3. Vérifie la variante Android dans le Builder et corrige ses shaders / performances.");
                b.AppendLine("4. Utilise Multi-Platform Build & Publish, pas le bouton de publication PC seul.");
                b.AppendLine("5. Après publication, vérifie que la version Android a bien été mise à jour sous le même Avatar ID.");
            }
            return b.ToString();
        }

        private static bool IsKnownQuestShader(Shader shader)
        {
            if (!shader) return false;
            string name = shader.name ?? string.Empty;
            return name.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Mobile/", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Legacy Shaders/Mobile/", StringComparison.OrdinalIgnoreCase);
        }

        private static long CountTriangles(Mesh mesh)
        {
            if (!mesh) return 0;
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                try { count += (long)mesh.GetIndexCount(i) / 3L; }
                catch { }
            }
            return count;
        }

        private static VRCAvatarDescriptor FindDescriptor(GameObject root)
        {
            if (!root) return null;
            return root.GetComponent<VRCAvatarDescriptor>() ?? root.GetComponentsInChildren<VRCAvatarDescriptor>(true).FirstOrDefault();
        }

        private static GameObject FindSiblingOrRoot(GameObject source, string objectName)
        {
            if (source.transform.parent)
            {
                Transform found = source.transform.parent.Find(objectName);
                return found ? found.gameObject : null;
            }
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .FirstOrDefault(go => go != source && go.name == objectName);
        }
    }
}
