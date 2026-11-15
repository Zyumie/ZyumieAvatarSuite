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

        [MenuItem("Zyumie/Avatar Suite")]
        public static void Open()
        {
            var window = GetWindow<ZyumieAvatarSuiteWindow>("Zyumie Avatar Suite");
            window.minSize = new Vector2(620, 700);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ZYUMIE AVATAR SUITE", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("v0.4.0 • VRChat • PCVR • Android / Quest", EditorStyles.miniLabel);
            EditorGUILayout.Space(8);
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatar, typeof(GameObject), true);
            if (!avatar && Selection.activeGameObject) avatar = Selection.activeGameObject;
            tab = GUILayout.Toolbar(tab, new[] { "Analyse", "Optimisation", "Tracking", "Plateformes" });
            EditorGUILayout.Space(8);

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
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copier")) EditorGUIUtility.systemCopyBuffer = report;
                if (GUILayout.Button("Exporter .txt")) ExportReport();
            }
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("Les assets source sont conservés. Les optimisations Android restent à valider dans le VRChat SDK Builder.", MessageType.Info);
        }

        private void DrawAnalysis()
        {
            if (GUILayout.Button("Analyser l'avatar", GUILayout.Height(36)))
                report = AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar));
        }

        private void DrawOptimization()
        {
            EditorGUILayout.HelpBox("Crée une copie de l'avatar puis clone ses matériaux et les textures compatibles. Les assets source restent inchangés.", MessageType.Info);
            textureMaxSize = EditorGUILayout.IntPopup("Résolution maximale", textureMaxSize,
                new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
            compressionQuality = EditorGUILayout.IntSlider("Qualité compression", compressionQuality, 0, 100);

            if (GUILayout.Button("Créer une copie + optimiser les textures", GUILayout.Height(36)))
            {
                var copy = AvatarOperations.CreatePlatformCopy(avatar, "_TextureOptimized");
                if (!copy) { report = "Impossible de créer la copie."; return; }
                Selection.activeGameObject = copy;
                avatar = copy;
                try { report = AvatarTextureOptimizer.OptimizeOnAvatarCopy(copy, textureMaxSize, compressionQuality).message; }
                catch (Exception ex) { report = "Erreur optimisation :\n" + ex; Debug.LogException(ex); }
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Supprimer les Missing Scripts"))
            {
                if (EditorUtility.DisplayDialog("Nettoyer les Missing Scripts ?", "Cette action est annulable avec Undo. Une sauvegarde reste recommandée.", "Supprimer", "Annuler"))
                    report = "Composants Missing Script supprimés : " + AvatarOperations.RemoveMissingScripts(avatar);
            }
            if (GUILayout.Button("Analyser les performances"))
                report = AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar));
            EditorGUILayout.HelpBox("La réduction polygonale automatique n'est pas incluse : une décimation générique peut casser les BlendShapes, le skinning, les vêtements et les visèmes.", MessageType.Warning);
        }

        private void DrawTracking()
        {
            EditorGUILayout.HelpBox("FEO v0.4.0 requiert VRChat Avatars SDK 3.10.6+ pour configurer le Face Tracking natif Unified Expressions.", MessageType.Info);
            if (GUILayout.Button("Configurer automatiquement Function > FEO", GUILayout.Height(38)))
                report = FEOAutoIntegration.Configure(avatar);
            if (GUILayout.Button("Diagnostiquer Eye Look / Face Tracking / OSC", GUILayout.Height(34)))
                report = AvatarOperations.InspectTracking(avatar);
            if (GUILayout.Button("Ouvrir le simulateur des yeux à la souris", GUILayout.Height(34)))
                FEOMouseEyeSimulator.Open(avatar);
            EditorGUILayout.HelpBox("MainGestures ne touche plus au contrôleur Gesture ni aux doigts. Il masque uniquement les couches FX détectées comme liées aux gestes et aux BlendShapes faciales.", MessageType.None);
            EditorGUILayout.HelpBox("Pour FrameEyeOSC, la réception native attend /tracking/face/v1 avec un blob de 102 octets. Les messages /tracking/eye/* sont un autre flux.", MessageType.Warning);
        }

        private void DrawPlatforms()
        {
            EditorGUILayout.HelpBox("Crée une variante Android et l'enregistre sur l'avatar PC avec l'API officielle PerPlatformOverrides. La copie n'est pas optimisée automatiquement pour Quest.", MessageType.Info);
            if (GUILayout.Button("Créer / mettre à jour le pack CROSS-PLATFORM", GUILayout.Height(40)))
                report = AvatarPlatformBuilder.CreateOrUpdateAndroidOverride(avatar);
            EditorGUILayout.Space(6);
            if (GUILayout.Button("Créer une copie Android seule")) MakeCopy("_Android_Quest");
            if (GUILayout.Button("Créer une copie Windows / PCVR")) MakeCopy("_Windows_PCVR");
            EditorGUILayout.HelpBox("Après vérification de la variante Android, publie depuis l'avatar principal avec VRChat SDK > Builder > Multi-Platform Build & Publish. Un Build & Publish PC seul ne met pas à jour Quest.", MessageType.Warning);
        }

        private void MakeCopy(string suffix)
        {
            var copy = AvatarOperations.CreatePlatformCopy(avatar, suffix);
            if (!copy) { report = "Impossible de créer la copie."; return; }
            Selection.activeGameObject = copy;
            avatar = copy;
            report = "Copie créée : " + copy.name + "\nAucun composant ni shader n'a été converti automatiquement. Vérifie le Builder de la plateforme cible.";
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            string path = EditorUtility.SaveFilePanel("Exporter le rapport", "", "ZyumieAvatarReport.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}
