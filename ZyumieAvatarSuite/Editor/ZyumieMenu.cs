using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    public static class ZyumieMenu
    {
        // Keep the eye preview directly below the main window entry.
        [MenuItem("Tools/Zyumie/Ouvrir Avatar Suite", priority = 0)]
        public static void OpenSuite()
        {
            ZyumieAvatarSuiteWindow.Open();
        }

        [MenuItem("Tools/Zyumie/Simulateur d'yeux (Souris)", priority = 1)]
        public static void OpenEyeSimulator()
        {
            if (!RequireAvatar())
                return;

            FEOMouseEyeSimulator.Open(Selection.activeGameObject);
        }

        // Diagnostics are read-only and grouped in one submenu.
        [MenuItem("Tools/Zyumie/Diagnostic/Analyse de performance complète", priority = 20)]
        public static void AnalyzeAvatar()
        {
            if (!RequireAvatar()) return;
            var avatar = Selection.activeGameObject;
            Debug.Log(AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar)));
        }

        [MenuItem("Tools/Zyumie/Diagnostic/Audit Skinning - Materials - PhysBones", priority = 21)]
        public static void AuditSkinning()
        {
            if (!RequireAvatar()) return;
            Debug.Log(AvatarAdvancedOptimizer.Report(Selection.activeGameObject));
        }

        [MenuItem("Tools/Zyumie/Diagnostic/Audit Animator - Avatar Masks", priority = 22)]
        public static void AuditAnimator()
        {
            if (!RequireAvatar()) return;
            Debug.Log(AvatarAnimatorDiagnostics.Report(Selection.activeGameObject));
        }

        [MenuItem("Tools/Zyumie/Diagnostic/Audit Eye Look", priority = 23)]
        public static void AuditEyeLook()
        {
            if (!RequireAvatar()) return;
            Debug.Log(AvatarEyeLookSetup.Audit(Selection.activeGameObject));
        }

        [MenuItem("Tools/Zyumie/Diagnostic/Face Tracking", priority = 24)]
        public static void AuditFaceTracking()
        {
            if (!RequireAvatar()) return;
            Debug.Log(FaceTrackingDiagnostic.Run(Selection.activeGameObject));
        }

        [MenuItem("Tools/Zyumie/Diagnostic/Tracking - OSC - Expression Parameters", priority = 25)]
        public static void AuditTracking()
        {
            if (!RequireAvatar()) return;
            Debug.Log(AvatarOperations.InspectTracking(Selection.activeGameObject));
        }

        // Mutating operations are grouped separately and require confirmation.
        [MenuItem("Tools/Zyumie/Actions Rapides/Nettoyer les Missing Scripts", priority = 40)]
        public static void CleanMissingScripts()
        {
            if (!RequireAvatar()) return;

            if (!EditorUtility.DisplayDialog(
                "Zyumie - Nettoyage",
                "Supprimer les Missing Scripts sur l'avatar sélectionné et ses enfants ?",
                "Nettoyer", "Annuler"))
                return;

            int removed = AvatarOperations.RemoveMissingScripts(Selection.activeGameObject);
            Debug.Log("[Zyumie] Missing Scripts supprimés : " + removed);
        }

        [MenuItem("Tools/Zyumie/Actions Rapides/Configurer Eye Look", priority = 41)]
        public static void ConfigureEyeLook()
        {
            if (!RequireAvatar()) return;

            if (!EditorUtility.DisplayDialog(
                "Zyumie - Eye Look",
                "Cette opération peut modifier le VRC Avatar Descriptor. Continuer ?",
                "Configurer", "Annuler"))
                return;

            Debug.Log(AvatarEyeLookSetup.Configure(Selection.activeGameObject));
        }

        [MenuItem("Tools/Zyumie/Actions Rapides/Configurer Face Tracking (FEO)", priority = 42)]
        public static void ConfigureFaceTracking()
        {
            if (!RequireAvatar()) return;

            if (!EditorUtility.DisplayDialog(
                "Zyumie - Face Tracking",
                "Cette opération peut créer des assets Animator et modifier le Descriptor, le menu d'expressions et les paramètres de l'avatar. Continuer ?",
                "Configurer", "Annuler"))
                return;

            Debug.Log(FEOAutoIntegration.Configure(Selection.activeGameObject));
        }

        private static bool RequireAvatar()
        {
            if (Selection.activeGameObject)
                return true;

            EditorUtility.DisplayDialog(
                "Zyumie Avatar Suite",
                "Sélectionne d'abord la racine de ton avatar dans la Hierarchy.",
                "OK");
            return false;
        }
    }
}
