using System.Linq;
using System.Collections.Generic;
using VRC.SDK3A.Editor;
using VRC.SDK3.Avatars.Components;
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
            window.minSize = new Vector2(600, 680);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ZYUMIE AVATAR SUITE", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("v0.3.9 • VRChat • PCVR • Android / Quest • Steam Frame", EditorStyles.miniLabel);
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
            EditorGUILayout.HelpBox("FEO vérifie Eye Look, ajoute UserEyeTracking au FX Animator et configure les contrôles natifs VRChat. MainGestures agit sur les doigts ; les expressions faciales du FX Animator restent indépendantes.", MessageType.Info);
            if (GUILayout.Button("Configurer automatiquement Function > FEO", GUILayout.Height(38)))
                report = FEOAutoIntegration.Configure(avatar);
            if (GUILayout.Button("Ouvrir le simulateur des yeux à la souris", GUILayout.Height(34)))
                FEOMouseEyeSimulator.Open(avatar);
            if (GUILayout.Button("Diagnostiquer Eye Look / OSC / Animator", GUILayout.Height(32)))
                report = AvatarOperations.InspectTracking(avatar);
            if (GUILayout.Button("Désactiver les booléens Face Tracking du Descriptor"))
            {
                if (EditorUtility.DisplayDialog("Désactiver Face Tracking ?", "Modifie uniquement les champs booléens FaceTracking reconnus sur le Descriptor.", "Désactiver", "Annuler"))
                    report = AvatarOperations.DisableFaceTrackingFields(avatar);
            }
        }

        private void DrawPlatforms()
        {
            EditorGUILayout.HelpBox("Nécessite VRChat SDK - Avatars 3.8.1+. Le bouton garde cet avatar comme base PC, crée une variante Android et l’enregistre avec l’API officielle PerPlatformOverrides.", MessageType.Info);
            if (GUILayout.Button("Créer une copie Windows / PCVR")) MakeCopy("_Windows_PCVR");
            if (GUILayout.Button("Créer une copie Android / Quest / Steam Frame")) MakeCopy("_Android_Quest_SteamFrame");
            if (GUILayout.Button("Créer le pack CROSS-PLATFORM (Windows + Android)", GUILayout.Height(38))) MakeCrossPlatform();
            EditorGUILayout.HelpBox("Le bouton CROSS-PLATFORM ne publie rien : vérifie les deux variantes puis utilise Multi-Platform Build & Publish dans le SDK.", MessageType.Warning);
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
            GameObject baseAvatar = avatar;
            if (!baseAvatar) return;

            try
            {
                VRCAvatarDescriptor baseDescriptor = FindAvatarDescriptor(baseAvatar);
                if (!baseDescriptor)
                {
                    report = "Aucun VRCAvatarDescriptor trouvé sur l'avatar sélectionné. Sélectionne sa racine.";
                    return;
                }

                const string suffix = "_Android_Quest_SteamFrame";
                string androidName = baseAvatar.name + suffix;
                GameObject androidAvatar = FindSiblingOrRoot(baseAvatar, androidName);
                bool created = androidAvatar == null;

                if (created)
                    androidAvatar = AvatarOperations.CreatePlatformCopy(baseAvatar, suffix);

                VRCAvatarDescriptor androidDescriptor = FindAvatarDescriptor(androidAvatar);
                if (!androidDescriptor)
                {
                    if (created) Undo.DestroyObjectImmediate(androidAvatar);
                    report = "La copie Android ne contient pas de VRCAvatarDescriptor. Override non configuré.";
                    return;
                }

                SetAndroidOverride(baseAvatar, androidAvatar, androidDescriptor);
                avatar = baseAvatar;
                Selection.activeGameObject = baseAvatar;

                report = "CONFIGURATION CROSS-PLATFORM TERMINÉE\n\n" +
                    "Avatar principal (PC / Windows) : " + baseAvatar.name + "\n" +
                    "Variante Android : " + androidAvatar.name + "\n\n" +
                    "La variante Android est associée à l'avatar principal via l'API officielle Per-Platform Overrides du SDK VRChat.\n" +
                    "Les deux variantes sont destinées à être publiées sous le même Avatar ID avec Multi-Platform Build & Publish.\n\n" +
                    (created ? "Copie Android créée." : "Copie Android existante réutilisée.") +
                    "\nAucun Blueprint ID n'a été modifié. Vérifie les deux versions dans le SDK avant publication.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                report = "Échec de la configuration CROSS-PLATFORM :\n" + ex.GetBaseException().Message +
                    "\nVérifie que VRChat SDK - Avatars 3.8.1 ou plus récent est installé. Aucune publication n'a été lancée.";
            }
        }

        private static VRCAvatarDescriptor FindAvatarDescriptor(GameObject root)
        {
            if (!root) return null;
            return root.GetComponents<VRCAvatarDescriptor>().FirstOrDefault();
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

        private static void SetAndroidOverride(GameObject baseAvatar, GameObject androidAvatar, VRCAvatarDescriptor androidDescriptor)
        {
            // Official VRChat SDK API (Avatars SDK 3.8.1+).
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
            if (!found)
                throw new InvalidOperationException("Le SDK n'a pas confirmé l'enregistrement de la variante Android.");

            EditorUtility.SetDirty(baseAvatar);
            EditorUtility.SetDirty(androidAvatar);
            EditorUtility.SetDirty(androidDescriptor);
        }

        private void ExportReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            string path = EditorUtility.SaveFilePanel("Exporter le rapport", "", "ZyumieAvatarReport.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, report);
        }
    }
}
