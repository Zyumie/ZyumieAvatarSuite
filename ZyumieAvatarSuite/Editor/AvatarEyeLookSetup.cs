using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Configuration du MODE B : Eye Look natif VRChat.
    /// FrameEyeOSC (Steam Frame → streaming PC) envoie /tracking/eye/* que VRChat
    /// traite en interne quand enableEyeLook = true. Aucun layer Animator, aucun
    /// paramètre FT, aucun BlendShape EyeLook n'est requis.
    ///
    /// Limites Mode B :
    ///  - Le clignement ferme les DEUX yeux (pas de clin d'œil indépendant).
    ///  - EyeWide (yeux écarquillés) n'est PAS transmis.
    ///  - Pas de pupilles/sourcils dynamiques.
    /// Pour un contrôle complet, utiliser VRCFaceTracking (Mode A).
    /// </summary>
    internal static class AvatarEyeLookSetup
    {
        private static readonly string[] LeftEyeTokens = {
            "eye_l", "eyeleft", "eye.left", "l_eye", "lefteye", "eye-left", "j_eye_l", "eye l"
        };
        private static readonly string[] RightEyeTokens = {
            "eye_r", "eyeright", "eye.right", "r_eye", "righteye", "eye-right", "j_eye_r", "eye r"
        };
        private static readonly string[] BlinkTokens = {
            "blink", "eyeclose", "eyesclose", "eyesclosed", "eyes_close", "eyelid",
            "まばたき", "瞬き", "めばたき", "clignement"
        };

        /// <summary>
        /// Applique la configuration Eye Look sur le descriptor de l'avatar.
        /// Retourne un rapport textuel détaillé.
        /// </summary>
        public static string Configure(GameObject avatar)
        {
            if (!avatar) return "Sélectionne la racine de l'avatar.";
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>() ??
                             avatar.GetComponentsInChildren<VRCAvatarDescriptor>(true).FirstOrDefault();
            if (!descriptor) return "Aucun VRC Avatar Descriptor trouvé.";

            var sb = new StringBuilder();
            sb.AppendLine("ZYUMIE — MODE B : EYE LOOK NATIF (FRAMEEYEOSC)");
            sb.AppendLine("Avatar : " + avatar.name);
            sb.AppendLine();

            var so = new SerializedObject(descriptor);
            so.Update();

            // 1. Activer Eye Look
            var enable = so.FindProperty("enableEyeLook");
            if (enable == null)
                return "Propriété 'enableEyeLook' introuvable. SDK VRChat Avatars >= 3.0.0 requis.";
            bool wasEnabled = enable.boolValue;
            enable.boolValue = true;
            sb.AppendLine("1. Eye Look : " + (wasEnabled ? "déjà activé" : "ACTIVÉ ✔"));

            // 2. Os des yeux
            var leftProp = so.FindProperty("customEyeLookSettings.leftEye");
            var rightProp = so.FindProperty("customEyeLookSettings.rightEye");
            if (leftProp == null || rightProp == null)
                return "'customEyeLookSettings' introuvable dans le Descriptor.";

            var currentLeft = leftProp.objectReferenceValue as Transform;
            var currentRight = rightProp.objectReferenceValue as Transform;
            Transform detectedLeft = currentLeft ? currentLeft : FindEyeTransform(avatar, true);
            Transform detectedRight = currentRight ? currentRight : FindEyeTransform(avatar, false);

            sb.AppendLine();
            sb.AppendLine("2. Os des yeux :");
            if (currentLeft) sb.AppendLine("   - Left  : déjà assigné → " + PathOf(currentLeft));
            else if (detectedLeft) { leftProp.objectReferenceValue = detectedLeft; sb.AppendLine("   - Left  : DÉTECTÉ → " + PathOf(detectedLeft)); }
            else sb.AppendLine("   - Left  : ❌ NON TROUVÉ, à assigner manuellement.");

            if (currentRight) sb.AppendLine("   - Right : déjà assigné → " + PathOf(currentRight));
            else if (detectedRight) { rightProp.objectReferenceValue = detectedRight; sb.AppendLine("   - Right : DÉTECTÉ → " + PathOf(detectedRight)); }
            else sb.AppendLine("   - Right : ❌ NON TROUVÉ, à assigner manuellement.");

            // 3. Paupières
            var eyelidTypeProp = so.FindProperty("customEyeLookSettings.eyelidType");
            sb.AppendLine();
            sb.AppendLine("3. Paupières :");
            if (eyelidTypeProp == null)
            {
                sb.AppendLine("   - Champ eyelidType introuvable, configuration manuelle requise.");
            }
            else
            {
                bool alreadySet = eyelidTypeProp.enumValueIndex > 0;
                if (alreadySet)
                {
                    sb.AppendLine("   - Type déjà configuré : " + eyelidTypeProp.enumNames[eyelidTypeProp.enumValueIndex]);
                }
                else
                {
                    var blink = FindBlinkBlendshape(avatar);
                    var eyelidMeshProp = so.FindProperty("customEyeLookSettings.eyelidsSkinnedMesh");
                    var blinkIndicesProp = so.FindProperty("customEyeLookSettings.eyelidsBlendshapes");

                    if (blink.found && eyelidMeshProp != null && blinkIndicesProp != null)
                    {
                        int blendshapesIndex = FindEnumIndex(eyelidTypeProp, "Blendshapes");
                        if (blendshapesIndex >= 0)
                        {
                            eyelidTypeProp.enumValueIndex = blendshapesIndex;
                            eyelidMeshProp.objectReferenceValue = blink.renderer;
                            blinkIndicesProp.arraySize = 1;
                            blinkIndicesProp.GetArrayElementAtIndex(0).intValue = blink.index;
                            sb.AppendLine("   - Type : Blendshapes ✔");
                            sb.AppendLine("   - Mesh  : " + blink.renderer.name);
                            sb.AppendLine("   - BlendShape : " + blink.name + " [index " + blink.index + "]");
                        }
                    }
                    else
                    {
                        sb.AppendLine("   - ❌ Aucun BlendShape de clignement détecté. Assignation manuelle requise.");
                        sb.AppendLine("     Candidats examinés : " + string.Join(", ", BlinkTokens));
                    }
                }
            }

            // 4. Rotations par défaut si non configurées
            var eyeRotations = so.FindProperty("customEyeLookSettings.eyeRotations");
            sb.AppendLine();
            if (eyeRotations != null)
            {
                var stateProp = eyeRotations.FindPropertyRelative("state");
                var angleProp = eyeRotations.FindPropertyRelative("angle");
                int enabledIndex = stateProp != null ? FindEnumIndex(stateProp, "Enabled") : -1;
                if (stateProp != null && enabledIndex >= 0 && stateProp.enumValueIndex == 0)
                {
                    stateProp.enumValueIndex = enabledIndex;
                    if (angleProp != null) angleProp.floatValue = 45f;
                    sb.AppendLine("4. Rotations : ACTIVÉES, angle 45° (par défaut).");
                    sb.AppendLine("   → Ajuste l'angle dans le Descriptor si les yeux débordent.");
                }
                else
                {
                    sb.AppendLine("4. Rotations : déjà configurées.");
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(descriptor);
            AssetDatabase.SaveAssets();

            sb.AppendLine();
            sb.AppendLine("=== LIMITES DU MODE B (à connaître) ===");
            sb.AppendLine(" ✔ Gaze (regard) : transmis");
            sb.AppendLine(" ✔ Clignement : transmis, mais les DEUX yeux ensemble (pas de clin d'œil séparé)");
            sb.AppendLine(" ✘ EyeWide (yeux écarquillés) : non transmis");
            sb.AppendLine(" ✘ Pupilles / sourcils dynamiques : non transmis");
            sb.AppendLine();
            sb.AppendLine("=== CÔTÉ CASQUE / PC ===");
            sb.AppendLine("1. FrameEyeOSC (Steam Frame) → Basic → VRChat");
            sb.AppendLine("2. FrameEyeOSC → Output → ✔ 'Send to Face Tracking too'");
            sb.AppendLine("3. VRChat PC → Action Menu → Options → OSC → Enabled");
            sb.AppendLine("4. SteamVR → Paramètres → Steam Link → OSC → DÉSACTIVÉ");
            sb.AppendLine("   (sinon deux sources se battent pour les yeux)");
            sb.AppendLine("5. Réuploade l'avatar.");

            return sb.ToString();
        }

        /// <summary>Audit lecture seule, sans rien modifier.</summary>
        public static string Audit(GameObject avatar)
        {
            if (!avatar) return "Sélectionne la racine de l'avatar.";
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>() ??
                             avatar.GetComponentsInChildren<VRCAvatarDescriptor>(true).FirstOrDefault();
            if (!descriptor) return "Aucun VRC Avatar Descriptor trouvé.";

            var so = new SerializedObject(descriptor);
            so.Update();
            var sb = new StringBuilder();
            sb.AppendLine("ZYUMIE — AUDIT MODE B (Eye Look natif)");
            sb.AppendLine("Avatar : " + avatar.name);
            sb.AppendLine();

            var enable = so.FindProperty("enableEyeLook");
            sb.AppendLine("Eye Look activé : " + (enable != null ? enable.boolValue.ToString() : "champ introuvable"));

            var left = so.FindProperty("customEyeLookSettings.leftEye")?.objectReferenceValue as Transform;
            var right = so.FindProperty("customEyeLookSettings.rightEye")?.objectReferenceValue as Transform;
            sb.AppendLine("Left Eye  : " + (left ? PathOf(left) : "❌ NON ASSIGNÉ"));
            sb.AppendLine("Right Eye : " + (right ? PathOf(right) : "❌ NON ASSIGNÉ"));

            var eyelidType = so.FindProperty("customEyeLookSettings.eyelidType");
            if (eyelidType != null)
                sb.AppendLine("Eyelid Type : " + (eyelidType.enumValueIndex < eyelidType.enumNames.Length ? eyelidType.enumNames[eyelidType.enumValueIndex] : "?"));
            else
                sb.AppendLine("Eyelid Type : champ introuvable");

            var eyelidMesh = so.FindProperty("customEyeLookSettings.eyelidsSkinnedMesh")?.objectReferenceValue as SkinnedMeshRenderer;
            sb.AppendLine("Eyelid Mesh : " + (eyelidMesh ? eyelidMesh.name : "aucun"));

            var blinkIndices = so.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
            if (blinkIndices != null && blinkIndices.arraySize > 0 && eyelidMesh && eyelidMesh.sharedMesh)
            {
                sb.Append("BlendShapes configurés : ");
                for (int i = 0; i < blinkIndices.arraySize; i++)
                {
                    int idx = blinkIndices.GetArrayElementAtIndex(i).intValue;
                    if (idx >= 0 && idx < eyelidMesh.sharedMesh.blendShapeCount)
                        sb.Append("[" + eyelidMesh.sharedMesh.GetBlendShapeName(idx) + "] ");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("BlendShapes configurés : aucun");
            }

            var rotations = so.FindProperty("customEyeLookSettings.eyeRotations");
            if (rotations != null)
            {
                var state = rotations.FindPropertyRelative("state");
                var angle = rotations.FindPropertyRelative("angle");
                if (state != null)
                    sb.AppendLine("Rotations : " + state.enumNames[state.enumValueIndex] +
                                  (angle != null ? " (" + angle.floatValue + "°)" : ""));
            }

            sb.AppendLine();
            sb.AppendLine("Rappel : en Mode B, VRChat applique /tracking/eye/* sans aucune couche Animator.");
            sb.AppendLine("Si l'audit passe mais que le tracking ne marche pas dans VRChat, le problème est côté");
            sb.AppendLine("OSC (FrameEyeOSC pas lancé, OSC désactivé dans VRChat, Steam Link OSC conflictuel).");
            return sb.ToString();
        }

        private static Transform FindEyeTransform(GameObject avatar, bool left)
        {
            var tokens = left ? LeftEyeTokens : RightEyeTokens;
            Transform best = null;
            int bestScore = 0;
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                string n = Normalize(t.name);
                foreach (var token in tokens)
                {
                    string nt = Normalize(token);
                    if (nt.Length < 3) continue;
                    int score = 0;
                    if (n == nt) score = 100;
                    else if (n.Contains(nt)) score = 80;
                    if (score > 0 && n.Length > nt.Length + 8) score -= 15;
                    if (score > bestScore) { bestScore = score; best = t; }
                }
            }
            return best;
        }

        private struct BlinkInfo
        {
            public bool found;
            public SkinnedMeshRenderer renderer;
            public int index;
            public string name;
        }

        private static BlinkInfo FindBlinkBlendshape(GameObject avatar)
        {
            var result = new BlinkInfo();
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr || !smr.sharedMesh) continue;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string name = smr.sharedMesh.GetBlendShapeName(i);
                    string n = Normalize(name);
                    foreach (var token in BlinkTokens)
                    {
                        string nt = Normalize(token);
                        if (n == nt || (nt.Length >= 4 && n.Contains(nt)))
                        {
                            if (!result.found || n == "blink" || n == Normalize("まばたき"))
                            {
                                result.found = true;
                                result.renderer = smr;
                                result.index = i;
                                result.name = name;
                            }
                            break;
                        }
                    }
                }
            }
            return result;
        }

        private static int FindEnumIndex(SerializedProperty prop, string name)
        {
            if (prop == null || prop.enumNames == null) return -1;
            for (int i = 0; i < prop.enumNames.Length; i++)
                if (string.Equals(prop.enumNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static string PathOf(Transform t)
        {
            if (!t) return "(null)";
            string p = t.name;
            while (t.parent) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}