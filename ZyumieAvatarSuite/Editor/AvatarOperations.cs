using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Zyumie.AvatarSuite
{
    internal static class AvatarOperations
    {
        public static int RemoveMissingScripts(GameObject root)
        {
            if (!root) return 0;
            int removed = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                Undo.RegisterCompleteObjectUndo(transform.gameObject, "Zyumie Remove Missing Scripts");
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            }
            return removed;
        }

        public static GameObject CreatePlatformCopy(GameObject source, string suffix)
        {
            if (!source) return null;
            var copy = UnityEngine.Object.Instantiate(source, source.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Zyumie Create Platform Copy");
            copy.name = source.name + suffix;
            // Do not remove physics, contacts or PhysBones here. Quest compatibility is audited separately.
            EditorUtility.SetDirty(copy);
            return copy;
        }

        public static string InspectTracking(GameObject root)
        {
            var b = new StringBuilder();
            b.AppendLine("ZYUMIE AVATAR SUITE — TRACKING DIAGNOSTIC v0.4.0");
            if (!root) return "Sélectionne la racine de l'avatar.";

            var descriptors = root.GetComponentsInChildren<VRCAvatarDescriptor>(true);
            b.AppendLine("VRC Avatar Descriptor(s) : " + descriptors.Length);
            if (descriptors.Length == 0) return b.Append("Aucun Descriptor trouvé.").ToString();

            foreach (var descriptor in descriptors)
            {
                var so = new SerializedObject(descriptor);
                so.Update();
                b.AppendLine("\nDescriptor : " + descriptor.name);
                var eyeEnabled = so.FindProperty("enableEyeLook");
                b.AppendLine("  Eye Look activé : " + (eyeEnabled != null ? eyeEnabled.boolValue.ToString() : "propriété introuvable"));
                var left = so.FindProperty("customEyeLookSettings.leftEye")?.objectReferenceValue as Transform;
                var right = so.FindProperty("customEyeLookSettings.rightEye")?.objectReferenceValue as Transform;
                b.AppendLine("  Left Eye : " + (left ? PathOf(left) : "NON ASSIGNÉ"));
                b.AppendLine("  Right Eye : " + (right ? PathOf(right) : "NON ASSIGNÉ"));
                b.AppendLine("  Références distinctes : " + (left && right && left != right));
                var gazeMode = so.FindProperty("customEyeLookSettings.eyeGazeMode");
                var eyelidType = so.FindProperty("customEyeLookSettings.eyelidType");
                b.AppendLine("  Eye Gaze Mode : " + ReadEnum(gazeMode));
                b.AppendLine("  Eyelids Type : " + ReadEnum(eyelidType));

                var faceEnabled = so.FindProperty("enableFaceTracking");
                var useAll = so.FindProperty("useAllTrackingKeys");
                var meshes = so.FindProperty("faceTrackingMeshes");
                var sets = so.FindProperty("faceTrackingBlendshapesSetList");
                if (faceEnabled == null || useAll == null || meshes == null || sets == null)
                {
                    b.AppendLine("  Face Tracking natif : champs absents (Avatars SDK 3.10.6+ requis pour la configuration native complète).");
                }
                else
                {
                    b.AppendLine("  Face Tracking activé : " + faceEnabled.boolValue);
                    b.AppendLine("  Use All Tracking Keys : " + useAll.boolValue);
                    b.AppendLine("  Face Tracking Meshes : " + meshes.arraySize + "/16");
                    b.AppendLine("  Blendshapes Sets : " + sets.arraySize);
                    int mapped = 0;
                    int mappedEye = 0;
                    bool blinkLeft = false, blinkRight = false;
                    for (int i = 0; i < sets.arraySize; i++)
                    {
                        var set = sets.GetArrayElementAtIndex(i);
                        var name = set.FindPropertyRelative("setName");
                        var shapes = set.FindPropertyRelative("faceTrackingBlendshapeNames");
                        b.AppendLine("    Set " + i + " : " + (name != null ? name.stringValue : "(sans nom)"));
                        if (shapes == null || !shapes.isArray) continue;
                        for (int key = 0; key < shapes.arraySize; key++)
                        {
                            string value = shapes.GetArrayElementAtIndex(key).stringValue;
                            if (string.IsNullOrEmpty(value)) continue;
                            mapped++;
                            if (key <= 7) mappedEye++;
                            if (key == 8) blinkRight = true;
                            if (key == 9) blinkLeft = true;
                        }
                    }
                    b.AppendLine("  Mappings Face Tracking : " + mapped + " (dont EyeLook* : " + mappedEye + ")");
                    b.AppendLine("  Clignement mappé : gauche=" + blinkLeft + ", droite=" + blinkRight);
                    b.AppendLine("  OSC Unified Expressions : " + (faceEnabled.boolValue && useAll.boolValue
                        ? "configuration avatar annoncée comme compatible avec /tracking/face/v1"
                        : "non activé : /tracking/face/v1 ne pilotera pas les mappings complets"));
                }

                var parameters = so.FindProperty("expressionParameters")?.objectReferenceValue as VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;
                if (parameters)
                {
                    int bits = (parameters.parameters ?? Array.Empty<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter>())
                        .Where(p => p.networkSynced)
                        .Sum(p => p.valueType == VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool ? 1 : 8);
                    b.AppendLine("  Expression Parameters synchronisés : " + bits + "/256 bits");
                }
                else b.AppendLine("  Expression Parameters : asset non assigné.");

                InspectPlayableLayer(so, VRCAvatarDescriptor.AnimLayerType.FX, b);
                InspectPlayableLayer(so, VRCAvatarDescriptor.AnimLayerType.Gesture, b);
            }

            b.AppendLine("\nBlendShapes candidats présents dans les meshes :");
            int shown = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr || !smr.sharedMesh) continue;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string name = smr.sharedMesh.GetBlendShapeName(i);
                    string normalized = Normalize(name);
                    bool candidate = normalized.Contains("eye") || normalized.Contains("blink") || normalized.Contains("mouth") ||
                                     normalized.Contains("jaw") || normalized.Contains("brow") || normalized.Contains("lip") ||
                                     normalized.Contains("viseme") || normalized.Contains("まばたき") || normalized.Contains("瞬き");
                    if (!candidate) continue;
                    b.AppendLine("  - " + smr.name + " / " + name + " [" + i + "]");
                    if (++shown >= 80) break;
                }
                if (shown >= 80) break;
            }
            if (shown == 0) b.AppendLine("  Aucun candidat basé sur le nom. Le mapping devra être configuré manuellement.");

            b.AppendLine("\nOSC : /tracking/face/v1 attend un unique blob de 102 octets Unified Expressions.");
            b.AppendLine("Les anciennes adresses /tracking/eye/* ne confirment pas la réception du flux /tracking/face/v1.");
            b.AppendLine("Statuts utiles : /avatar/avatarfullfacetracking, /avatar/parameters/UserEyeTracking, /avatar/parameters/UserFaceTracking.");
            b.AppendLine("Ce diagnostic ne lit pas le trafic OSC et ne garantit pas le résultat visuel en jeu.");
            b.AppendLine("Aucune donnée du Descriptor, aucun os et aucun BlendShape n'a été modifié.");
            return b.ToString();
        }

        private static void InspectPlayableLayer(SerializedObject descriptor, VRCAvatarDescriptor.AnimLayerType target, StringBuilder b)
        {
            var layers = descriptor.FindProperty("baseAnimationLayers");
            if (layers == null || !layers.isArray) return;
            for (int i = 0; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                var type = element.FindPropertyRelative("type");
                if (type == null || type.enumValueIndex < 0 || type.enumValueIndex >= type.enumNames.Length ||
                    !string.Equals(type.enumNames[type.enumValueIndex], target.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                var controller = element.FindPropertyRelative("animatorController")?.objectReferenceValue as RuntimeAnimatorController;
                b.AppendLine("  " + target + " Controller : " + (controller ? controller.name : "NON ASSIGNÉ"));
                var animator = controller as AnimatorController;
                if (animator)
                {
                    foreach (var layer in animator.layers)
                    {
                        if (layer.name.StartsWith("Zyumie_FEO_", StringComparison.Ordinal))
                            b.AppendLine("    Couche FEO : " + layer.name);
                    }
                    if (target == VRCAvatarDescriptor.AnimLayerType.FX)
                    {
                        var names = animator.parameters.Select(p => p.name).ToArray();
                        b.AppendLine("    Paramètre EyesTracking : " + names.Contains("EyesTracking"));
                        b.AppendLine("    Paramètre MainGestures : " + names.Contains("MainGestures"));
                    }
                }
                return;
            }
        }

        private static string ReadEnum(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum) return "non disponible";
            return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                ? property.enumDisplayNames[property.enumValueIndex] : property.enumValueIndex.ToString();
        }

        private static string Normalize(string value)
        {
            return new string((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
