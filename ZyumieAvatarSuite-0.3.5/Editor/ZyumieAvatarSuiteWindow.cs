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
            window.minSize = new Vector2(600, 680);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ZYUMIE AVATAR SUITE", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("v0.3.5 • VRChat • PCVR • Android / Quest • Steam Frame", EditorStyles.miniLabel);
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
            EditorGUILayout.HelpBox("Les textures et matériaux sont copiés avant optimisation. Aucun Collider, Rigidbody, PhysBone ou Contact n'est supprimé automatiquement.", MessageType.Info);
        }

        private void DrawAnalysis()
        {
            if (GUILayout.Button("Analyser l'avatar", GUILayout.Height(36)))
                report = AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar));
        }

        private void DrawOptimization()
        {
            EditorGUILayout.HelpBox("Crée une copie de l'avatar dans la scène puis clone ses matériaux et les textures importées compatibles. Les assets source restent inchangés.", MessageType.Info);
            textureMaxSize = EditorGUILayout.IntPopup("Résolution maximale", textureMaxSize,
                new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
            compressionQuality = EditorGUILayout.IntSlider("Qualité compression", compressionQuality, 0, 100);

            if (GUILayout.Button("Créer une copie + optimiser les textures", GUILayout.Height(36)))
            {
                var copy = AvatarOperations.CreatePlatformCopy(avatar, "_TextureOptimized");
                Selection.activeGameObject = copy;
                avatar = copy;
                try { report = AvatarTextureOptimizer.OptimizeOnAvatarCopy(copy, textureMaxSize, compressionQuality).message; }
                catch (System.Exception ex) { report = "Erreur optimisation :\n" + ex; Debug.LogException(ex); }
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
            EditorGUILayout.HelpBox("FEO utilise le comportement natif VRChat Animator Tracking Control pour basculer Eye Look et le tracking des doigts.", MessageType.Info);
            if (GUILayout.Button("Configurer automatiquement Function > FEO", GUILayout.Height(38)))
                report = FEOAutoIntegration.Configure(avatar);
            if (GUILayout.Button("Diagnostiquer Eye Look / FrameEyeOSC", GUILayout.Height(32)))
                report = AvatarOperations.InspectTracking(avatar);
            if (GUILayout.Button("Désactiver les booléens Face Tracking du Descriptor"))
            {
                if (EditorUtility.DisplayDialog("Désactiver Face Tracking ?", "Modifie uniquement les champs booléens FaceTracking reconnus sur le Descriptor.", "Désactiver", "Annuler"))
                    report = AvatarOperations.DisableFaceTrackingFields(avatar);
            }
        }

        private void DrawPlatforms()
        {
            EditorGUILayout.HelpBox("Windows = PCVR et Steam Frame en PC streaming. Android = Quest et Steam Frame autonome. Deux cibles de build distinctes.", MessageType.Info);
            if (GUILayout.Button("Créer une copie Windows / PCVR")) MakeCopy("_Windows_PCVR");
            if (GUILayout.Button("Créer une copie Android / Quest / Steam Frame")) MakeCopy("_Android_Quest_SteamFrame");
            if (GUILayout.Button("Créer le pack CROSS-PLATFORM (Windows + Android)", GUILayout.Height(38))) MakeCrossPlatform();
            EditorGUILayout.HelpBox("Aucun composant n'est retiré automatiquement. Vérifie Windows et Android séparément avec VRChat SDK Builder.", MessageType.Warning);
        }

        private void MakeCopy(string suffix)
        {
            var copy = AvatarOperations.CreatePlatformCopy(avatar, suffix);
            Selection.activeGameObject = copy;
            avatar = copy;
            report = "Copie créée : " + copy.name + "\nAucun composant n'a été supprimé automatiquement. Lance VRChat SDK Builder > Build & Test.";
        }

        private void MakeCrossPlatform()
        {
            var source = avatar;
            if (!source) return;
            var windows = AvatarOperations.CreatePlatformCopy(source, "_Windows_PCVR");
            var android = AvatarOperations.CreatePlatformCopy(source, "_Android_Quest_SteamFrame");
            Selection.objects = new UnityEngine.Object[] { windows, android };
            report = "Pack cross-platform créé depuis la même source :\n" +
                "• " + windows.name + " (Windows / PCVR / Steam Frame PC streaming)\n" +
                "• " + android.name + " (Android / Quest / Steam Frame autonome)\n\n" +
                "Les deux copies sont indépendantes. Pour publier comme variantes de plateforme, utilise le même Blueprint ID et les overrides de plateforme VRChat, puis valide chaque build séparément.";
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            string path = EditorUtility.SaveFilePanel("Exporter le rapport", "", "ZyumieAvatarReport.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}
