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

        [MenuItem("Zyumie/Avatar Suite")]
        public static void Open()
        {
            var w = GetWindow<ZyumieAvatarSuiteWindow>("Zyumie Avatar Suite");
            w.minSize = new Vector2(580, 650);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ZYUMIE AVATAR SUITE", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("VRChat • PCVR • Android / Quest • Steam Frame", EditorStyles.miniLabel);
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
            EditorGUILayout.HelpBox("Travaille sur une copie du prefab ou sous contrôle Git. Les opérations destructives sont limitées aux copies de plateforme ou demandent une action explicite.", MessageType.Info);
        }

        private void DrawAnalysis()
        {
            if (GUILayout.Button("Analyser l'avatar", GUILayout.Height(36)))
            {
                var m = AvatarAnalyzer.Analyze(avatar);
                report = AvatarAnalyzer.Report(avatar, m);
            }
        }

        private void DrawOptimization()
        {
            EditorGUILayout.HelpBox("Les optimisations risquées (fusion de meshes, réduction polygonale, atlas, suppression de bones ou réécriture d'animations) ne sont pas appliquées à l'aveugle : elles peuvent casser le rig, les vêtements et les expressions.", MessageType.Warning);
            if (GUILayout.Button("Supprimer les Missing Scripts"))
            {
                int n = AvatarOperations.RemoveMissingScripts(avatar);
                report = "Composants Missing Script supprimés : " + n + "\nUne analyse complète est recommandée ensuite.";
            }
            if (GUILayout.Button("Analyser les performances"))
            {
                var m = AvatarAnalyzer.Analyze(avatar);
                report = AvatarAnalyzer.Report(avatar, m);
            }
        }

        private void DrawTracking()
        {
            EditorGUILayout.HelpBox("Ce module prépare et diagnostique l'avatar. Il ne fabrique pas lui-même le flux de tracking : FrameEyeOSC ou un autre fournisseur doit transmettre les données au client VRChat.", MessageType.Info);
            if (GUILayout.Button("Diagnostiquer Eye Look / FrameEyeOSC", GUILayout.Height(32)))
                report = AvatarOperations.InspectTracking(avatar);
            if (GUILayout.Button("Désactiver les booléens Face Tracking du Descriptor"))
            {
                if (EditorUtility.DisplayDialog("Désactiver Face Tracking ?",
                    "Cette action modifie les champs booléens FaceTracking reconnus sur le VRC Avatar Descriptor. Elle ne supprime pas les paramètres Animator ni les BlendShapes. Continuer ?",
                    "Désactiver", "Annuler"))
                    report = AvatarOperations.DisableFaceTrackingFields(avatar);
            }
        }

        private void DrawPlatforms()
        {
            EditorGUILayout.HelpBox("Quest et Steam Frame autonome sont des cibles Android/mobile. La copie Steam Frame est donc une copie Android préparée, pas une plateforme VRChat distincte. Le SDK VRChat reste responsable du build et de la validation.", MessageType.Warning);
            if (GUILayout.Button("Créer une copie PCVR"))
                MakeCopy("_PCVR", false);
            if (GUILayout.Button("Créer une copie Android / Quest"))
                MakeCopy("_Android_Quest", true);
            if (GUILayout.Button("Créer une copie Steam Frame (Android)"))
                MakeCopy("_SteamFrame_Android", true);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("La préparation mobile retire sur la copie les Lights, Cloth, AudioSource, Rigidbody et Collider Unity, composants non pris en charge ou désactivés côté mobile. Vérifie les dépendances de ton avatar après l'opération.", MessageType.None);
        }

        private void MakeCopy(string suffix, bool mobile)
        {
            var copy = AvatarOperations.CreatePlatformCopy(avatar, suffix, mobile);
            Selection.activeGameObject = copy;
            avatar = copy;
            report = "Copie créée : " + copy.name + "\nPréparation mobile appliquée : " + mobile +
                     "\nVérifie le résultat, puis utilise le SDK VRChat Builder pour sélectionner Android ou Windows et lancer Build & Test.";
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            string path = EditorUtility.SaveFilePanel("Exporter le rapport", "", "ZyumieAvatarReport.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}
