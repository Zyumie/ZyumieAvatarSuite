using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Zyumie.AvatarSuite
{
    public static class FaceTrackingDiagnostic
    {
        public static string Run(GameObject avatar)
        {
            if (!avatar) return "Sélectionne ton avatar dans la hiérarchie d'abord !";

            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>() ?? avatar.GetComponentInChildren<VRCAvatarDescriptor>();
            if (!descriptor) return "Aucun VRC Avatar Descriptor trouvé sur " + avatar.name;

            var sb = new StringBuilder();
            sb.AppendLine("=== DIAGNOSTIC FACE TRACKING : " + avatar.name + " ===");

            // 1. Descriptor
            var so = new SerializedObject(descriptor);
            so.Update();
            var ftEnabled = so.FindProperty("enableFaceTracking");
            var useAll = so.FindProperty("useAllTrackingKeys");
            var meshesProp = so.FindProperty("faceTrackingMeshes"); // Correction ici

            sb.AppendLine("\n[1] Configuration du Descriptor :");
            sb.AppendLine("  - Face Tracking Activé : " + (ftEnabled != null ? ftEnabled.boolValue.ToString() : "Champ introuvable"));
            sb.AppendLine("  - Use All Tracking Keys : " + (useAll != null ? useAll.boolValue.ToString() : "Champ introuvable"));

            int meshCount = meshesProp != null ? meshesProp.arraySize : 0;
            sb.AppendLine("  - Meshes Face Tracking assignés : " + meshCount + (meshCount == 0 ? " (Problème !)" : ""));

            // 2. BlendShapes
            sb.AppendLine("\n[2] BlendShapes de Tracking (sur le Body) :");
            var smr = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(s => s.name.ToLower().Contains("body") || s.name.ToLower().Contains("face"));

            if (smr && smr.sharedMesh)
            {
                int count = 0;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string shapeName = smr.sharedMesh.GetBlendShapeName(i);
                    if (shapeName.ToLower().Contains("vrc.v_") || shapeName.ToLower().Contains("eyelook") ||
                        shapeName.ToLower().Contains("facial") || shapeName.ToLower().Contains("v2/"))
                    {
                        sb.AppendLine("  ✔ " + shapeName);
                        count++;
                    }
                }
                if (count == 0)
                    sb.AppendLine("  ❌ AUCUN BlendShape de Face Tracking trouvé. Le patch FBX n'a pas fonctionné !");
                else
                    sb.AppendLine("  => " + count + " BlendShapes de tracking trouvés. Le FBX semble patché !");
            }
            else
            {
                sb.AppendLine("  ❌ Impossible de trouver le mesh du corps (Body).");
            }

            // 3. Animator FX
            sb.AppendLine("\n[3] Vérification de l'Animator FX (est-ce qu'il bloque les yeux ?) :");
            if (descriptor.baseAnimationLayers != null)
            {
                var fxLayer = descriptor.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
                if (fxLayer.animatorController is AnimatorController fxController)
                {
                    bool foundEyeLayer = false;
                    foreach (var layer in fxController.layers)
                    {
                        if (layer.name.ToLower().Contains("eye") || layer.name.ToLower().Contains("face") || layer.name.ToLower().Contains("blink"))
                        {
                            sb.AppendLine("  ⚠ Couche FX trouvée : '" + layer.name + "' (Weight: " + layer.defaultWeight + ")");
                            foundEyeLayer = true;
                        }
                    }
                    if (!foundEyeLayer)
                        sb.AppendLine("  ✔ Aucune couche FX évidente ne semble écraser les yeux.");
                }
            }

            // 4. Paramètres VRCFT
            sb.AppendLine("\n[4] Paramètres Animator (Recherche de VRCFT) :");
            if (descriptor.expressionParameters)
            {
                int vrcftCount = descriptor.expressionParameters.parameters.Count(p => p.name.Contains("FT/") || p.name.Contains("Face") || p.name.Contains("v2/"));
                sb.AppendLine("  - Paramètres FT/v2 trouvés : " + vrcftCount + " (s'il y en a 0, c'est que VRCFT n'est pas configuré).");
            }

            sb.AppendLine("\n=== FIN DU DIAGNOSTIC ===");
            return sb.ToString();
        }
    }
}