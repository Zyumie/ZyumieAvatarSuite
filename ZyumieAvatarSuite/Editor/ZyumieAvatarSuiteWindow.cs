using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    public class ZyumieAvatarSuiteWindow : EditorWindow
    {
        private GameObject avatar;
        private Vector2 scroll;
        private string report = "Sélectionne la racine de ton avatar puis lance une analyse.";
        private int tab;
        private int textureMaxSize = 1024;
        private int compressionQuality = 50;
        private int polygonReductionPercent = 35;
        private int maxBoneInfluences = 4;
        private float minimumBoneWeightPercent = 0.5f;
        private bool removeOrphanedPhysBoneColliders;

        // État d'ouverture/fermeture des sections repliables
        private bool showSafeSection = true;
        private bool showModerateSection = false;
        private bool showHeavySection = false;
        private bool showToolboxSection = false;

        [MenuItem("Zyumie/Avatar Suite")]
        public static void Open()
        {
            var window = GetWindow<ZyumieAvatarSuiteWindow>("Zyumie Avatar Suite");
            window.minSize = new Vector2(700, 800);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ZYUMIE AVATAR SUITE", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("v0.8.0 • VRChat • PCVR • Android / Quest • FrameEyeOSC (Mode B)",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatar, typeof(GameObject), true);
            if (!avatar && Selection.activeGameObject) avatar = Selection.activeGameObject;

            if (avatar && !IsWorkingCopy(avatar) && tab == 1)
            {
                EditorGUILayout.HelpBox(
                    "⚠ Cet avatar ne ressemble pas à une COPIE. Les optimisations créent une copie " +
                    "automatiquement, mais tu peux le faire manuellement d'abord si tu préfères.",
                    MessageType.Warning);
            }

            tab = GUILayout.Toolbar(tab, new[] { "🔍 Analyse", "⚙ Optimisation", "👁 Tracking", "📱 Plateformes" });
            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(!avatar))
            {
                switch (tab)
                {
                    case 0: DrawAnalysis(); break;
                    case 1: DrawOptimization(); break;
                    case 2: DrawTracking(); break;
                    case 3: DrawPlatforms(); break;
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Rapport", EditorStyles.boldLabel);
            using (var sv = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true)))
            {
                scroll = sv.scrollPosition;
                EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copier")) EditorGUIUtility.systemCopyBuffer = report;
                if (GUILayout.Button("Exporter .txt")) ExportReport();
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Les diagnostics sont en lecture seule. Les optimisations travaillent sur une copie et créent " +
                "des assets séparés. Valide toujours le résultat avec VRChat SDK Builder.",
                MessageType.Info);
        }

        private static bool IsWorkingCopy(GameObject go)
        {
            string n = go.name;
            return n.Contains("Copy") || n.Contains("Zyumie") || n.Contains("_Optimized") ||
                   n.Contains("_Decimated") || n.Contains("_Merged") || n.Contains("_Android") ||
                   n.Contains("_Windows");
        }

        // ─────────────────────────────────────────────────────────────
        private void DrawAnalysis()
        {
            if (GUILayout.Button("📊 Analyser l'avatar (métriques complètes)", GUILayout.Height(38)))
                report = AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar));
            if (GUILayout.Button("🧩 Audit meshes / fusions possibles", GUILayout.Height(34)))
                report = AvatarGeometryDiagnostics.Report(avatar);
            if (GUILayout.Button("🦴 Audit Skinning / Materials / PhysBones", GUILayout.Height(34)))
                report = AvatarAdvancedOptimizer.Report(avatar);
            if (GUILayout.Button("🎞 Audit Animator / Avatar Masks", GUILayout.Height(34)))
                report = AvatarAnimatorDiagnostics.Report(avatar);
            if (GUILayout.Button("👁 Audit Eye Look (Mode B) + FrameEyeOSC", GUILayout.Height(34)))
                report = AvatarEyeLookSetup.Audit(avatar);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "L'audit géométrique liste les meshes les plus lourds et les groupes réellement " +
                "compatibles avec la fusion automatique conservatrice.",
                MessageType.None);
        }

        // ─────────────────────────────────────────────────────────────
        private void DrawOptimization()
        {
            EditorGUILayout.HelpBox(
                "Toutes les opérations géométriques créent d'abord une copie de l'avatar " +
                "et des Mesh assets distincts. Les meshes source restent inchangés.",
                MessageType.Info);

            // Section SÛR
            showSafeSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSafeSection, "🟢 SÛR — aucune perte visuelle attendue");
            if (showSafeSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    maxBoneInfluences = EditorGUILayout.IntPopup("Influences max / vertex", maxBoneInfluences,
                        new[] { "2", "4", "8" }, new[] { 2, 4, 8 });
                    minimumBoneWeightPercent = EditorGUILayout.Slider("Poids minimal conservé (%)",
                        minimumBoneWeightPercent, 0f, 5f);

                    if (GUILayout.Button("🔧 Optimiser Skinning & Bones", GUILayout.Height(32)))
                    {
                        var copy = MakeCopy("_SkinningOptimized");
                        if (copy)
                            RunSafe(() => AvatarAdvancedOptimizer
                                .OptimizeSkinningAndBonesOnAvatarCopy(copy, maxBoneInfluences, minimumBoneWeightPercent / 100f)
                                .Summary("OPTIMISATION SKINNING / BONES"), copy);
                    }

                    if (GUILayout.Button("🎨 Fusionner les Material Slots identiques", GUILayout.Height(32)))
                    {
                        var copy = MakeCopy("_MaterialSlotsOptimized");
                        if (copy)
                            RunSafe(() => AvatarAdvancedOptimizer
                                .MergeDuplicateMaterialSlotsOnAvatarCopy(copy)
                                .Summary("OPTIMISATION MATERIAL SLOTS"), copy);
                    }

                    removeOrphanedPhysBoneColliders = EditorGUILayout.ToggleLeft(
                        "Supprimer aussi les PhysBoneColliders non globaux non référencés", removeOrphanedPhysBoneColliders);

                    if (GUILayout.Button("🪢 Nettoyer les PhysBones (refs / ignore)", GUILayout.Height(32)))
                    {
                        var copy = MakeCopy("_PhysBonesOptimized");
                        if (!copy) return;
                        if (removeOrphanedPhysBoneColliders && !EditorUtility.DisplayDialog(
                            "Supprimer les colliders orphelins ?",
                            "Seuls les VRCPhysBoneCollider non globaux et non référencés sont supprimés. " +
                            "Des scripts personnalisés pourraient les chercher par nom. Continuer ?",
                            "Supprimer sur la copie", "Annuler")) return;
                        RunSafe(() => AvatarAdvancedOptimizer
                            .OptimizePhysBonesOnAvatarCopy(copy, removeOrphanedPhysBoneColliders)
                            .Summary("OPTIMISATION PHYSBONES"), copy);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Section MODÉRÉ
            showModerateSection = EditorGUILayout.BeginFoldoutHeaderGroup(showModerateSection, "🟠 MODÉRÉ — vérifie visuellement après");
            if (showModerateSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "Fusion : uniquement les renderers STRICTEMENT compatibles (même transform, " +
                        "rootBone, bindposes, ordre des bones, réglages de rendu). " +
                        "Les BlendShapes et renderers animés sont exclus.",
                        MessageType.None);

                    if (GUILayout.Button("🧬 Fusionner les SkinnedMeshRenderers compatibles", GUILayout.Height(32)))
                    {
                        var copy = MakeCopy("_Merged");
                        if (copy)
                            RunSafe(() => AvatarSkinnedMeshMerger
                                .MergeCompatibleOnAvatarCopy(copy)
                                .Summary("FUSION DES SKINNED MESHES"), copy);
                    }

                    polygonReductionPercent = EditorGUILayout.IntSlider("Réduction cible (%)",
                        polygonReductionPercent, 5, 50);
                    EditorGUILayout.HelpBox(
                        "La réduction inclut les SkinnedMeshRenderers et les MeshRenderers statiques. " +
                        "Les zones Face/Head/Body/Hair/Eye sont protégées, ainsi que les vertices bougés " +
                        "par BlendShapes. Les vêtements/accessoires sont réduits.",
                        MessageType.None);

                    if (GUILayout.Button("📐 Créer une copie + réduire les polygones", GUILayout.Height(32)))
                    {
                        var copy = MakeCopy("_Decimated");
                        if (copy)
                            RunSafe(() => AvatarMeshOptimizer
                                .ReduceOnAvatarCopy(copy, polygonReductionPercent)
                                .Summary("RÉDUCTION POLYGONALE"), copy);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Section LOURD
            showHeavySection = EditorGUILayout.BeginFoldoutHeaderGroup(showHeavySection, "🔴 LOURD — perte visuelle possible");
            if (showHeavySection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "Réduction polygonale agressive : à utiliser sur les vêtements ou accessoires " +
                        "secondaires. Vérifie toujours le résultat sur une COPIE avant publication.",
                        MessageType.Warning);

                    if (GUILayout.Button("⚡ Créer une copie + réduction 60 %", GUILayout.Height(32)))
                    {
                        if (!EditorUtility.DisplayDialog("Réduction agressive",
                            "Une réduction de 60 % peut déformer les silhouettes et les articulations. Continuer ?",
                            "Continuer", "Annuler")) return;
                        var copy = MakeCopy("_Decimated60");
                        if (copy)
                            RunSafe(() => AvatarMeshOptimizer
                                .ReduceOnAvatarCopy(copy, 60)
                                .Summary("RÉDUCTION POLYGONALE 60%"), copy);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Section BOÎTE À OUTILS
            showToolboxSection = EditorGUILayout.BeginFoldoutHeaderGroup(showToolboxSection, "🧰 Boîte à outils");
            if (showToolboxSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawTextureSettings();

                    if (GUILayout.Button("🖼 Créer une copie + optimiser les textures", GUILayout.Height(32)))
                    {
                        var copy = MakeCopy("_TextureOptimized");
                        if (copy)
                        {
                            try
                            {
                                report = AvatarTextureOptimizer
                                    .OptimizeOnAvatarCopy(copy, textureMaxSize, compressionQuality).message;
                            }
                            catch (Exception ex) { report = "Erreur textures :\n" + ex; Debug.LogException(ex); }
                        }
                    }

                    if (GUILayout.Button("🧹 Supprimer les Missing Scripts (Undo possible)", GUILayout.Height(32)))
                    {
                        if (EditorUtility.DisplayDialog("Nettoyer les Missing Scripts ?",
                            "Cette action est annulable. Sauvegarde recommandée.", "Supprimer", "Annuler"))
                            report = "Composants Missing Script supprimés : " + AvatarOperations.RemoveMissingScripts(avatar);
                    }

                    if (GUILayout.Button("🚀 Optimisation avancée complète (SÛR)", GUILayout.Height(38)))
                    {
                        var copy = MakeCopy("_AdvancedOptimized");
                        if (!copy) return;
                        if (removeOrphanedPhysBoneColliders && !EditorUtility.DisplayDialog(
                            "Optimisation complète",
                            "La copie sera optimisée puis les colliders PhysBone non globaux et non référencés supprimés. Continuer ?",
                            "Continuer", "Annuler")) return;
                        try
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine(AvatarAdvancedOptimizer
                                .OptimizeSkinningAndBonesOnAvatarCopy(copy, maxBoneInfluences, minimumBoneWeightPercent / 100f)
                                .Summary("SKINNING / BONES"));
                            sb.AppendLine(AvatarSkinnedMeshMerger
                                .MergeCompatibleOnAvatarCopy(copy)
                                .Summary("FUSION SKINNED MESHES"));
                            sb.AppendLine(AvatarAdvancedOptimizer
                                .MergeDuplicateMaterialSlotsOnAvatarCopy(copy)
                                .Summary("MATERIAL SLOTS"));
                            sb.AppendLine(AvatarAdvancedOptimizer
                                .OptimizePhysBonesOnAvatarCopy(copy, removeOrphanedPhysBoneColliders)
                                .Summary("PHYSBONES"));
                            report = sb.ToString();
                        }
                        catch (Exception ex) { report = "Erreur optimisation complète :\n" + ex; Debug.LogException(ex); }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ─────────────────────────────────────────────────────────────
        private void DrawTracking()
        {
            EditorGUILayout.HelpBox(
                "MODE B — Eye Look natif VRChat. FrameEyeOSC (Steam Frame → PC) envoie " +
                "/tracking/eye/* que VRChat traite en interne. Aucun Animator layer, aucun paramètre FT. " +
                "Limite : clignement symétrique uniquement, pas d'EyeWide.",
                MessageType.Info);

            if (GUILayout.Button("👁 Configurer Eye Look natif (Mode B)", GUILayout.Height(38)))
                report = AvatarEyeLookSetup.Configure(avatar);

            if (GUILayout.Button("🔍 Auditer Eye Look / FrameEyeOSC", GUILayout.Height(34)))
                report = AvatarEyeLookSetup.Audit(avatar);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Diagnostics complémentaires", EditorStyles.boldLabel);

            if (GUILayout.Button("🔬 Diagnostic tracking complet (Eye Look + Face Tracking + FEO)", GUILayout.Height(32)))
                report = AvatarOperations.InspectTracking(avatar);

            if (GUILayout.Button("🎞 Diagnostic Animator / Avatar Masks", GUILayout.Height(32)))
                report = AvatarAnimatorDiagnostics.Report(avatar);

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "FrameEyeOSC : Basic → VRChat | Output → ✔ 'Send to Face Tracking too' (ou 'native_eyes'). " +
                "VRChat : Options → OSC → Enabled. Steam Link OSC → DÉSACTIVÉ.",
                MessageType.Warning);

            EditorGUILayout.HelpBox(
                "Si tu veux le clin d'œil indépendant + EyeWide + pupilles dynamiques, il faut passer " +
                "en Mode A (VRCFaceTracking + paramètres FT/v2/...). Le Mode B est plus simple mais limité.",
                MessageType.None);
        }

        // ─────────────────────────────────────────────────────────────
        private void DrawPlatforms()
        {
            EditorGUILayout.HelpBox(
                "Les profils PCVR et Android/Quest sont des variantes distinctes. " +
                "Un override Android ne convertit pas automatiquement les shaders ni les meshes du PC.",
                MessageType.Info);

            if (GUILayout.Button("📦 Créer / mettre à jour le pack CROSS-PLATFORM", GUILayout.Height(40)))
                report = AvatarPlatformBuilder.CreateOrUpdateAndroidOverride(avatar);
            if (GUILayout.Button("🔎 Auditer la variante Android / Quest existante", GUILayout.Height(34)))
                report = AvatarPlatformBuilder.AuditCurrentAndroidOverride(avatar);

            EditorGUILayout.Space(6);
            DrawTextureSettings();
            if (GUILayout.Button("📦 Créer / mettre à jour Android + textures optimisées", GUILayout.Height(38)))
                report = AvatarPlatformBuilder.CreateOptimizedAndroidVariant(avatar, textureMaxSize, compressionQuality);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("📱 Créer une copie Android / Quest")) MakeCopyReport("_Android_Quest");
            if (GUILayout.Button("🖥 Créer une copie Windows / PCVR")) MakeCopyReport("_Windows_PCVR");

            EditorGUILayout.HelpBox(
                "Steam Frame en streaming Steam Link exécute la session VRChat PC sur Windows. " +
                "Le profil à utiliser pour ton cas (FrameEyeOSC) est PCVR, pas Android.",
                MessageType.Warning);
        }

        // ─────────────────────────────────────────────────────────────
        private void DrawTextureSettings()
        {
            textureMaxSize = EditorGUILayout.IntPopup("Résolution maximale", textureMaxSize,
                new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
            compressionQuality = EditorGUILayout.IntSlider("Qualité compression", compressionQuality, 0, 100);
        }

        private GameObject MakeCopy(string suffix)
        {
            var copy = AvatarOperations.CreatePlatformCopy(avatar, suffix);
            if (!copy) { report = "Impossible de créer la copie."; return null; }
            Selection.activeGameObject = copy;
            avatar = copy;
            return copy;
        }

        private void MakeCopyReport(string suffix)
        {
            var copy = MakeCopy(suffix);
            if (!copy) return;
            report = "Copie créée : " + copy.name +
                     "\nAucun composant ni shader n'a été converti automatiquement.";
        }

        private void RunSafe(Func<string> action, GameObject context)
        {
            try { report = action(); }
            catch (Exception ex)
            {
                report = "Erreur pendant l'opération sur " + context.name + " :\n" + ex;
                Debug.LogException(ex);
            }
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            string path = EditorUtility.SaveFilePanel("Exporter le rapport", "", "ZyumieAvatarReport.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}