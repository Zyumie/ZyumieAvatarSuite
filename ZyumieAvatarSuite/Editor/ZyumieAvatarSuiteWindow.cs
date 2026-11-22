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
        private string report = "👋 Bienvenue dans Zyumie Avatar Suite.\n\n1. Sélectionne ton avatar dans la scène.\n2. Choisis un onglet ci-dessus.\n3. Lance une action.";
        private int tab;

        private int textureMaxSize = 1024;
        private int compressionQuality = 50;
        private int polygonReductionPercent = 35;
        private int maxBoneInfluences = 4;
        private float minimumBoneWeightPercent = 0.5f;

        private bool showSafeSection = true;
        private bool showModerateSection = false;
        private bool showToolboxSection = false;

        public static void Open()
        {
            var window = GetWindow<ZyumieAvatarSuiteWindow>("Zyumie Avatar Suite");
            window.minSize = new Vector2(750, 850);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("ZYUMIE AVATAR SUITE", new GUIStyle(EditorStyles.largeLabel) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label("Optimisation & Analytics • VRChat", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                avatar = (GameObject)EditorGUILayout.ObjectField("🎯 Avatar Cible", avatar, typeof(GameObject), true);
                if (!avatar && Selection.activeGameObject) avatar = Selection.activeGameObject;
            }

            // 3 Onglets propres
            tab = GUILayout.Toolbar(tab, new[] { "📊 Diagnostics", "⚙ Optimisation", "👁 Configuration Tracking" }, GUILayout.Height(30));
            EditorGUILayout.Space(10);

            using (new EditorGUI.DisabledScope(!avatar))
            {
                switch (tab)
                {
                    case 0: DrawDiagnosticsTab(); break;
                    case 1: DrawOptimizationTab(); break;
                    case 2: DrawTrackingTab(); break;
                }
            }

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("📝 Rapport", EditorStyles.boldLabel);
            using (var sv = new EditorGUILayout.ScrollViewScope(scroll, EditorStyles.textArea, GUILayout.ExpandHeight(true)))
            {
                scroll = sv.scrollPosition;
                EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("📋 Copier", GUILayout.Height(25))) EditorGUIUtility.systemCopyBuffer = report;
                if (GUILayout.Button("💾 Exporter .txt", GUILayout.Height(25))) ExportReport();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ONGLET 1 : TOUS LES DIAGNOSTICS
        // ═══════════════════════════════════════════════════════════════
        private void DrawDiagnosticsTab()
        {
            EditorGUILayout.HelpBox("Les diagnostics sont en lecture seule. Ils ne modifient pas ton avatar.", MessageType.Info);
            EditorGUILayout.Space(5);

            if (GUILayout.Button("📊 Analyse de performance complète", GUILayout.Height(40)))
                report = AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar));

            if (GUILayout.Button("🦴 Audit Skinning / Materials / PhysBones", GUILayout.Height(35)))
                report = AvatarAdvancedOptimizer.Report(avatar);

            if (GUILayout.Button("🎞 Audit Animator / Avatar Masks", GUILayout.Height(35)))
                report = AvatarAnimatorDiagnostics.Report(avatar);

            if (GUILayout.Button("👁 Audit Eye Look (Mode B)", GUILayout.Height(35)))
                report = AvatarEyeLookSetup.Audit(avatar);

            if (GUILayout.Button("🔬 Diagnostic Face Tracking complet", GUILayout.Height(35)))
                report = FaceTrackingDiagnostic.Run(avatar);
        }

        // ═══════════════════════════════════════════════════════════════
        //  ONGLET 2 : OPTIMISATION
        // ═══════════════════════════════════════════════════════════════
        private void DrawOptimizationTab()
        {
            EditorGUILayout.HelpBox("Les opérations créent une COPIE de l'avatar. Les meshes originaux ne sont jamais écrasés.", MessageType.Info);
            EditorGUILayout.Space(5);

            showSafeSection = EditorGUILayout.BeginFoldoutHeaderGroup(showSafeSection, "🟢 Optimisations SÛRES (Aucune perte)");
            if (showSafeSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button("🔧 Optimiser Skinning & Bones", GUILayout.Height(30)))
                        RunSafe("Skinning", () => AvatarAdvancedOptimizer.OptimizeSkinningAndBonesOnAvatarCopy(CreateCopy("_SkinningOpt"), 4, 0.005f).Summary("SKINNING"));

                    if (GUILayout.Button("🎨 Fusionner les Material Slots identiques", GUILayout.Height(30)))
                        RunSafe("Materials", () => AvatarAdvancedOptimizer.MergeDuplicateMaterialSlotsOnAvatarCopy(CreateCopy("_MaterialsOpt")).Summary("MATERIALS"));

                    if (GUILayout.Button("🪢 Nettoyer les PhysBones (refs / ignore)", GUILayout.Height(30)))
                        RunSafe("PhysBones", () => AvatarAdvancedOptimizer.OptimizePhysBonesOnAvatarCopy(CreateCopy("_PhysBonesOpt"), false).Summary("PHYSBONES"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showModerateSection = EditorGUILayout.BeginFoldoutHeaderGroup(showModerateSection, "🟠 Optimisations MODÉRÉES (À vérifier)");
            if (showModerateSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button("🖼 Optimiser les Textures", GUILayout.Height(30)))
                        RunSafe("Textures", () => AvatarTextureOptimizer.OptimizeOnAvatarCopy(CreateCopy("_TexturesOpt"), 1024, 50).message);

                    polygonReductionPercent = EditorGUILayout.IntSlider("Réduction ciblée (%)", polygonReductionPercent, 5, 50);
                    if (GUILayout.Button("📐 Réduire les polygones", GUILayout.Height(30)))
                        RunSafe("Polygones", () => AvatarMeshOptimizer.ReduceOnAvatarCopy(CreateCopy("_Decimated"), polygonReductionPercent).Summary("POLYGONES"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showToolboxSection = EditorGUILayout.BeginFoldoutHeaderGroup(showToolboxSection, "🧰 Boîte à outils & Paramètres");
            if (showToolboxSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button("⚡ Optimiser les paramètres (Fix Bits)", GUILayout.Height(30)))
                    {
                        AutoParameterOptimizer.OptimizeParameters();
                        report = "Optimisation des paramètres lancée. Regarde la Console Unity pour voir combien de bits ont été économisés.";
                    }

                    if (GUILayout.Button("🧹 Supprimer les Missing Scripts", GUILayout.Height(30)))
                    {
                        if (EditorUtility.DisplayDialog("Nettoyage", "Supprimer les scripts manquants ?", "Oui", "Annuler"))
                            report = $"Missing Scripts supprimés : {AvatarOperations.RemoveMissingScripts(avatar)}";
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ONGLET 3 : CONFIGURATION DU TRACKING
        // ═══════════════════════════════════════════════════════════════
        private void DrawTrackingTab()
        {
            EditorGUILayout.HelpBox("Configuration de l'Eye Look et du Face Tracking.", MessageType.Info);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("👁 Mode B : Eye Look Natif (FrameEyeOSC)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("Configurer le suivi des yeux (Eye Look)", GUILayout.Height(35)))
                    report = AvatarEyeLookSetup.Configure(avatar);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🧠 Mode A : Face Tracking Complet (VRCFT/FEO)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("⚙ Configurer le Face Tracking Natif (FEO)", GUILayout.Height(35)))
                    report = FEOAutoIntegration.Configure(avatar);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🖱 Outils de test", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("🖱 Ouvrir le Simulateur d'Yeux (Souris)", GUILayout.Height(30)))
                    FEOMouseEyeSimulator.Open(avatar);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  FONCTIONS UTILITAIRES
        // ═══════════════════════════════════════════════════════════════
        private GameObject CreateCopy(string suffix)
        {
            var copy = AvatarOperations.CreatePlatformCopy(avatar, suffix);
            if (!copy) { report = "Impossible de créer la copie."; return null; }
            Selection.activeGameObject = copy;
            avatar = copy;
            return copy;
        }

        private void RunSafe(string operationName, Func<string> action)
        {
            try { report = $"✅ '{operationName}' terminé :\n\n" + action(); }
            catch (Exception ex) { report = $"❌ Erreur '{operationName}' :\n{ex.Message}"; Debug.LogException(ex); }
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            string path = EditorUtility.SaveFilePanel("Sauvegarder", "", "Zyumie_Rapport.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}