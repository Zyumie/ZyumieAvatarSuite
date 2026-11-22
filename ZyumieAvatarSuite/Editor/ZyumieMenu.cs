using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    public static class ZyumieMenu
    {
        [MenuItem("Tools/Zyumie/🚀 Ouvrir Avatar Suite", priority = 0)]
        public static void OpenSuite() => ZyumieAvatarSuiteWindow.Open();

        [MenuItem("Tools/Zyumie/👁 Simulateur d'Yeux (Souris)", priority = 1)]
        public static void OpenEyeSimulator() => FEOMouseEyeSimulator.Open(Selection.activeGameObject);

        // --- Actions Rapides ---
        [MenuItem("Tools/Zyumie/Actions Rapides/Optimiser les paramètres (Fix Bits)", priority = 20)]
        public static void QuickFixBits()
        {
            var avatar = Selection.activeGameObject;
            if (!avatar) { EditorUtility.DisplayDialog("Erreur", "Sélectionne un avatar dans la scène.", "OK"); return; }
            AutoParameterOptimizer.OptimizeParameters();
        }

        [MenuItem("Tools/Zyumie/Actions Rapides/Nettoyer les Missing Scripts", priority = 21)]
        public static void QuickCleanScripts()
        {
            var avatar = Selection.activeGameObject;
            if (!avatar) return;
            int removed = AvatarOperations.RemoveMissingScripts(avatar);
            Debug.Log($"[Zyumie] {removed} Missing Scripts supprimés.");
        }

        // --- Diagnostics Rapides ---
        [MenuItem("Tools/Zyumie/Diagnostics/Analyse complète", priority = 40)]
        public static void QuickAnalyze()
        {
            var avatar = Selection.activeGameObject;
            if (!avatar) return;
            Debug.Log(AvatarAnalyzer.Report(avatar, AvatarAnalyzer.Analyze(avatar)));
        }

        [MenuItem("Tools/Zyumie/Diagnostics/Audit Skinning / PhysBones", priority = 41)]
        public static void QuickAuditSkin()
        {
            var avatar = Selection.activeGameObject;
            if (!avatar) return;
            Debug.Log(AvatarAdvancedOptimizer.Report(avatar));
        }

        [MenuItem("Tools/Zyumie/Diagnostics/Diagnostic Face Tracking", priority = 42)]
        public static void QuickDiagnoseFaceTracking()
        {
            var avatar = Selection.activeGameObject;
            if (!avatar) return;
            Debug.Log(FaceTrackingDiagnostic.Run(avatar));
        }
    }
}