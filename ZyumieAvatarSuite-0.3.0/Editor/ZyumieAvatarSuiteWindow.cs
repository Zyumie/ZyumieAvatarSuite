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
            var w = GetWindow<ZyumieAvatarSuiteWindow>("Zyumie Avatar Suite");
            w.minSize = new Vector2(600, 680);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ZYUMIE AVATAR SUITE", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("v0.3.0 • VRChat • PCVR • Android / Quest • Steam Frame", EditorStyles.miniLabel);
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
            EditorGUILayout.HelpBox("Les optimisations de textures utilisent des matériaux et textures clonés. Les composants d'origine ne sont pas supprimés automatiquement.", MessageType.Info);
        }

        private void DrawAnalysis()
        {
            if (GUILayout.Button("Analyser l'avatar", GUILayout.Height(36)))
                report = AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar));
        }

        private void DrawOptimization()
        {
            EditorGUILayout.HelpBox("L'opération crée une copie dans la scène, clone ses matériaux et copie les textures importées compatibles. Les assets source restent inchangés.", MessageType.Info);
            textureMaxSize = EditorGUILayout.IntPopup("Résolution maximale", textureMaxSize,
                new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
            compressionQuality = EditorGUILayout.IntSlider("Qualité compression", compressionQuality, 0, 100);

            if (GUILayout.Button("Créer une copie + optimiser les textures", GUILayout.Height(36)))
            {
                var copy = AvatarOperations.CreatePlatformCopy(avatar, "_TextureOptimized");
                Selection.activeGameObject = copy;
                avatar = copy;
                try
                {
                    report = AvatarTextureOptimizer.OptimizeOnAvatarCopy(copy, textureMaxSize, compressionQuality).message;
                }
                catch (System.Exception ex)
                {
                    report = "Erreur pendant l'optimisation :\n" + ex;
                    Debug.LogException(ex);
                }
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Supprimer les Missing Scripts"))
            {
                if (EditorUtility.DisplayDialog("Nettoyer les Missing Scripts ?", "Cette action supprime les composants manquants sur l'objet sélectionné. Elle est annulable avec Undo. Une sauvegarde du projet reste recommandée.", "Supprimer", "Annuler"))
                {
                    int n = AvatarOperations.RemoveMissingScripts(avatar);
                    report = "Composants Missing Script supprimés : " + n + "\nUne analyse complète est recommandée ensuite.";
                }
            }
            if (GUILayout.Button("Analyser les performances"))
                report = AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar));

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("La réduction polygonale et les atlas UV ne sont pas inclus : une décimation générique peut casser les BlendShapes, le skinning et les vêtements. Aucun mesh ni os n'est modifié.", MessageType.Warning);
        }

        private void DrawTracking()
        {
            EditorGUILayout.HelpBox("Le diagnostic inspecte le Descriptor, les os et les BlendShapes. Il ne génère pas le flux de tracking matériel.", MessageType.Info);
            if (GUILayout.Button("Diagnostiquer Eye Look / FrameEyeOSC", GUILayout.Height(32)))
                report = AvatarOperations.InspectTracking(avatar);
            if (GUILayout.Button("Désactiver les booléens Face Tracking du Descriptor"))
            {
                if (EditorUtility.DisplayDialog("Désactiver Face Tracking ?", "Cette action modifie uniquement les champs booléens FaceTracking reconnus sur le VRC Avatar Descriptor. Continuer ?", "Désactiver", "Annuler"))
                    report = AvatarOperations.DisableFaceTrackingFields(avatar);
            }
        }

        private void DrawPlatforms()
        {
            EditorGUILayout.HelpBox("VRChat propose deux cibles de build : Windows et Android. Quest et Steam Frame autonome utilisent Android ; Steam Frame en PC streaming utilise Windows.", MessageType.Info);
            if (GUILayout.Button("Créer une copie Windows / PCVR")) MakeCopy("_Windows_PCVR");
            if (GUILayout.Button("Créer une copie Android / Quest / Steam Frame")) MakeCopy("_Android_Quest_SteamFrame");
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Aucun composant n'est supprimé automatiquement. Vérifie les incompatibilités et utilise VRChat SDK Builder pour Build & Test.", MessageType.Warning);
        }

        private void MakeCopy(string suffix)
        {
            var copy = AvatarOperations.CreatePlatformCopy(avatar, suffix);
            Selection.activeGameObject = copy;
            avatar = copy;
            report = "Copie créée : " + copy.name + "\nAucun composant n'a été supprimé automatiquement.\nUtilise le SDK VRChat Builder pour Android ou Windows et lance Build & Test.";
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            string path = EditorUtility.SaveFilePanel("Exporter le rapport", "", "ZyumieAvatarReport.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}
