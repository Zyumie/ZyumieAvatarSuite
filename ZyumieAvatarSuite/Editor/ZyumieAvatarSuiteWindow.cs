using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    public class ZyumieAvatarSuiteWindow : EditorWindow
    {
        private GameObject avatar;
        private Vector2 reportScroll;
        private string report = "Bienvenue dans Zyumie Avatar Suite.\n\n1. Sélectionne ton avatar dans la scène.\n2. Choisis une catégorie.\n3. Lance un diagnostic ou une opération.";
        private int tab;
        private int polygonReductionPercent = 35;
        private bool showSafeSection = true;
        private bool showModerateSection;

        public static void Open()
        {
            var window = GetWindow<ZyumieAvatarSuiteWindow>("Zyumie Avatar Suite");
            window.minSize = new Vector2(760, 780);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("ZYUMIE AVATAR SUITE", new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            });
            GUILayout.Label("VRChat • Diagnostics • Optimisation • Tracking",
                EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                avatar = (GameObject)EditorGUILayout.ObjectField(
                    "Avatar cible", avatar, typeof(GameObject), true);

                if (!avatar && Selection.activeGameObject)
                    avatar = Selection.activeGameObject;
            }

            tab = GUILayout.Toolbar(tab,
                new[] { "Diagnostic", "Actions rapides", "Optimisation" },
                GUILayout.Height(30));

            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(!avatar))
            {
                switch (tab)
                {
                    case 0: DrawDiagnosticsTab(); break;
                    case 1: DrawQuickActionsTab(); break;
                    case 2: DrawOptimizationTab(); break;
                }
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Rapport", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(
                reportScroll, EditorStyles.textArea, GUILayout.ExpandHeight(true)))
            {
                reportScroll = scroll.scrollPosition;
                EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copier le rapport", GUILayout.Height(26)))
                    EditorGUIUtility.systemCopyBuffer = report ?? string.Empty;

                if (GUILayout.Button("Exporter .txt", GUILayout.Height(26)))
                    ExportReport();
            }
        }

        private void DrawDiagnosticsTab()
        {
            EditorGUILayout.HelpBox(
                "Tous les outils de cette section sont en lecture seule : ils analysent l'avatar sans le modifier.",
                MessageType.Info);

            if (GUILayout.Button("Analyse de performance complète", GUILayout.Height(36)))
                RunSafe("Analyse de performance",
                    () => AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar)));

            if (GUILayout.Button("Audit Skinning / Materials / PhysBones", GUILayout.Height(32)))
                RunSafe("Audit Skinning / PhysBones",
                    () => AvatarAdvancedOptimizer.Report(avatar));

            if (GUILayout.Button("Audit Animator / Avatar Masks", GUILayout.Height(32)))
                RunSafe("Audit Animator",
                    () => AvatarAnimatorDiagnostics.Report(avatar));

            if (GUILayout.Button("Audit Eye Look", GUILayout.Height(32)))
                RunSafe("Audit Eye Look",
                    () => AvatarEyeLookSetup.Audit(avatar));

            if (GUILayout.Button("Diagnostic Face Tracking", GUILayout.Height(32)))
                RunSafe("Diagnostic Face Tracking",
                    () => FaceTrackingDiagnostic.Run(avatar));

            if (GUILayout.Button("Audit Tracking / OSC / Expression Parameters", GUILayout.Height(32)))
                RunSafe("Audit Tracking",
                    () => AvatarOperations.InspectTracking(avatar));
        }

        private void DrawQuickActionsTab()
        {
            EditorGUILayout.HelpBox(
                "Ces opérations peuvent modifier le Descriptor ou les composants de l'avatar. Une confirmation est demandée avant leur exécution.",
                MessageType.Warning);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Nettoyage", EditorStyles.boldLabel);
                if (GUILayout.Button("Supprimer les Missing Scripts", GUILayout.Height(34)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Nettoyer les Missing Scripts",
                        "Supprimer les composants manquants sur l'avatar sélectionné et ses enfants ?",
                        "Nettoyer", "Annuler"))
                    {
                        RunSafe("Nettoyage Missing Scripts", () =>
                        {
                            int removed = AvatarOperations.RemoveMissingScripts(avatar);
                            return "Composants manquants supprimés : " + removed;
                        });
                    }
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Configuration du tracking", EditorStyles.boldLabel);

                if (GUILayout.Button("Configurer Eye Look", GUILayout.Height(34)))
                {
                    RunConfirmed("Configurer Eye Look",
                        "Cette opération peut modifier les réglages Eye Look du VRC Avatar Descriptor.",
                        () => AvatarEyeLookSetup.Configure(avatar));
                }

                if (GUILayout.Button("Configurer Face Tracking (FEO)", GUILayout.Height(34)))
                {
                    RunConfirmed("Configurer Face Tracking (FEO)",
                        "Cette opération peut créer des assets Animator et modifier le Descriptor, le menu d'expressions et les paramètres de l'avatar.",
                        () => FEOAutoIntegration.Configure(avatar));
                }
            }
        }

        private void DrawOptimizationTab()
        {
            EditorGUILayout.HelpBox(
                "Les optimisations créent une copie de l'avatar. Les meshes et assets sources ne sont pas écrasés. Vérifie toujours le résultat dans Unity.",
                MessageType.Info);

            showSafeSection = EditorGUILayout.BeginFoldoutHeaderGroup(
                showSafeSection, "Optimisations conservatrices");
            if (showSafeSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button("Optimiser Skinning & Bones", GUILayout.Height(32)))
                        RunSafe("Skinning", () =>
                            AvatarAdvancedOptimizer.OptimizeSkinningAndBonesOnAvatarCopy(
                                CreateCopy("_SkinningOpt"), 4, 0.005f).Summary("SKINNING"));

                    if (GUILayout.Button("Fusionner les Material Slots identiques", GUILayout.Height(32)))
                        RunSafe("Materials", () =>
                            AvatarAdvancedOptimizer.MergeDuplicateMaterialSlotsOnAvatarCopy(
                                CreateCopy("_MaterialsOpt")).Summary("MATERIALS"));

                    if (GUILayout.Button("Nettoyer les références PhysBones", GUILayout.Height(32)))
                        RunSafe("PhysBones", () =>
                            AvatarAdvancedOptimizer.OptimizePhysBonesOnAvatarCopy(
                                CreateCopy("_PhysBonesOpt"), false).Summary("PHYSBONES"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showModerateSection = EditorGUILayout.BeginFoldoutHeaderGroup(
                showModerateSection, "Optimisations à vérifier visuellement");
            if (showModerateSection)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button("Optimiser les textures", GUILayout.Height(32)))
                        RunSafe("Textures", () =>
                            AvatarTextureOptimizer.OptimizeOnAvatarCopy(
                                CreateCopy("_TexturesOpt"), 1024, 50).message);

                    polygonReductionPercent = EditorGUILayout.IntSlider(
                        "Réduction ciblée (%)", polygonReductionPercent, 5, 50);

                    if (GUILayout.Button("Réduire les polygones", GUILayout.Height(32)))
                        RunSafe("Polygones", () =>
                            AvatarMeshOptimizer.ReduceOnAvatarCopy(
                                CreateCopy("_Decimated"), polygonReductionPercent).Summary("POLYGONES"));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void RunConfirmed(string title, string message, Func<string> action)
        {
            if (!EditorUtility.DisplayDialog(title, message, "Continuer", "Annuler"))
                return;

            RunSafe(title, action);
        }

        private GameObject CreateCopy(string suffix)
        {
            var copy = AvatarOperations.CreatePlatformCopy(avatar, suffix);
            if (!copy)
                throw new InvalidOperationException("Impossible de créer une copie de l'avatar.");

            Selection.activeGameObject = copy;
            avatar = copy;
            return copy;
        }

        private void RunSafe(string operationName, Func<string> action)
        {
            try
            {
                report = "Opération : " + operationName + "\n\n" + action();
            }
            catch (Exception ex)
            {
                report = "ERREUR — " + operationName + "\n\n" + ex.Message;
                Debug.LogException(ex);
            }
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report))
                return;

            string path = EditorUtility.SaveFilePanel(
                "Exporter le rapport", "", "Zyumie_Rapport.txt", "txt");

            if (!string.IsNullOrEmpty(path))
                File.WriteAllText(path, report);
        }
    }
}
