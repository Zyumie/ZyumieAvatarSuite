using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

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
            var copy = Object.Instantiate(source, source.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Zyumie Create Platform Copy");
            copy.name = source.name + suffix;
            // Do not strip colliders/rigidbodies: they can be used by PhysBones, Contacts and custom interactions.
            EditorUtility.SetDirty(copy);
            return copy;
        }

        public static string InspectTracking(GameObject root)
        {
            var b = new StringBuilder();
            b.AppendLine("ZYUMIE AVATAR SUITE — FEO TRACKING DIAGNOSTIC v0.3.9");
            if (!root) return "Sélectionne la racine de l'avatar.";
            var descriptors = root.GetComponentsInChildren<Component>(true)
                .Where(c => c && c.GetType().Name == "VRCAvatarDescriptor").ToArray();
            b.AppendLine("VRC Avatar Descriptor(s) : " + descriptors.Length);

            foreach (var descriptor in descriptors)
            {
                var so = new SerializedObject(descriptor);
                so.Update();
                b.AppendLine("\\nDescriptor : " + descriptor.name);
                var enabled = so.FindProperty("enableEyeLook");
                b.AppendLine("  Eye Look activé : " + (enabled != null ? enabled.boolValue.ToString() : "propriété introuvable"));
                var left = so.FindProperty("customEyeLookSettings.leftEye")?.objectReferenceValue as Transform;
                var right = so.FindProperty("customEyeLookSettings.rightEye")?.objectReferenceValue as Transform;
                b.AppendLine("  Left Eye : " + (left ? PathOf(left) : "NON ASSIGNÉ"));
                b.AppendLine("  Right Eye : " + (right ? PathOf(right) : "NON ASSIGNÉ"));
                b.AppendLine("  Références distinctes : " + (left && right && left != right));
                b.AppendLine("  Même hiérarchie que l'avatar : " +
                    (left && right && left.IsChildOf(root.transform) && right.IsChildOf(root.transform)));
                var descriptorProperty = so.GetIterator();
                bool enterDescriptor = true;
                while (descriptorProperty.NextVisible(enterDescriptor))
                {
                    enterDescriptor = true;
                    string propertyName = descriptorProperty.propertyPath.ToLowerInvariant();
                    if (propertyName.Contains("eyelid") || propertyName.Contains("eye") || propertyName.Contains("tracking"))
                        b.AppendLine("  Descriptor." + descriptorProperty.propertyPath + " = " + Describe(descriptorProperty));
                }

                var expressionParameters = so.FindProperty("expressionParameters")?.objectReferenceValue as VRCExpressionParameters;
                if (expressionParameters)
                {
                    int syncedBits = (expressionParameters.parameters ?? System.Array.Empty<VRCExpressionParameters.Parameter>())
                        .Where(parameter => parameter.networkSynced)
                        .Sum(parameter => parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8);
                    b.AppendLine("  Expression Parameters synchronisés : " + syncedBits + "/256 bits");
                    b.AppendLine("  Budget synchronisé : " + (syncedBits <= 256 ? "OK" : "DÉPASSÉ"));
                }
                else b.AppendLine("  Expression Parameters : asset non assigné.");

                var layers = so.FindProperty("baseAnimationLayers");
                bool fxFound = false;
                if (layers != null && layers.isArray)
                {
                    for (int i = 0; i < layers.arraySize; i++)
                    {
                        var element = layers.GetArrayElementAtIndex(i);
                        var type = element.FindPropertyRelative("type");
                        if (type == null || type.enumValueIndex < 0 || type.enumValueIndex >= type.enumNames.Length ||
                            !type.enumNames[type.enumValueIndex].Equals("FX", System.StringComparison.OrdinalIgnoreCase)) continue;
                        fxFound = true;
                        var controller = element.FindPropertyRelative("animatorController")?.objectReferenceValue as RuntimeAnimatorController;
                        b.AppendLine("  FX Controller : " + (controller ? controller.name : "NON ASSIGNÉ"));
                        var animatorController = controller as UnityEditor.Animations.AnimatorController;
                        if (animatorController)
                        {
                            var parameters = animatorController.parameters;
                            var names = parameters.Select(x => x.name).ToArray();
                            b.AppendLine("  Paramètre UserEyeTracking présent : " + names.Contains("UserEyeTracking"));
                            b.AppendLine("  Paramètre EyesTracking présent : " + names.Contains("EyesTracking"));
                            foreach (var parameter in parameters.Where(x => x.name == "UserEyeTracking" || x.name == "EyesTracking"))
                                b.AppendLine("    " + parameter.name + " : " + parameter.type + " (défaut " +
                                    (parameter.type == AnimatorControllerParameterType.Bool ? parameter.defaultBool.ToString() : parameter.defaultFloat.ToString("0.###")) + ")");
                        }
                        else b.AppendLine("  Paramètres FX : non inspectables automatiquement (controller absent ou override).");
                    }
                }
                if (!fxFound) b.AppendLine("  Slot FX : introuvable.");
            }

            int eye = 0, lids = 0, mouth = 0;
            var lidNames = new System.Collections.Generic.List<string>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr || !smr.sharedMesh) continue;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string originalName = smr.sharedMesh.GetBlendShapeName(i);
                    string n = originalName.ToLowerInvariant();
                    if (n.Contains("eye") || n.Contains("iris") || n.Contains("look")) eye++;
                    if (n.Contains("lid") || n.Contains("blink") || n.Contains("close") ||
                        n.Contains("まばたき") || n.Contains("瞬き") || n.Contains("目閉じ") || n.Contains("閉じ"))
                    {
                        lids++;
                        lidNames.Add(smr.name + " / " + originalName + " [index " + i + "]");
                    }
                    if (n.Contains("mouth") || n.Contains("jaw") || n.Contains("viseme")) mouth++;
                }
            }
            b.AppendLine("\\nBlendShapes regard détectées (noms indicatifs) : " + eye);
            b.AppendLine("BlendShapes paupières candidates : " + lids);
            b.AppendLine("Les candidats sont listés avec leur index exact ; vérifie-les un par un dans le simulateur avant de les utiliser comme mapping de clignement.");
            foreach (var name in lidNames.Take(30)) b.AppendLine("  - " + name);
            b.AppendLine("BlendShapes bouche/visèmes : " + mouth);
            b.AppendLine("\\nOSC natif Eye Tracking attendu : /tracking/eye/EyesClosedAmount + UNE adresse de regard (ex. /tracking/eye/CenterVec).");
            b.AppendLine("UserEyeTracking est un Bool intégré à ajouter au FX Animator pour piloter des états ; ne pas l'ajouter aux Expression Parameters.");
            b.AppendLine("La réception OSC et le mouvement réel doivent être confirmés dans VRChat. Ce diagnostic ne peut pas lire le flux OSC depuis Unity.");
            b.AppendLine("Aucun os, BlendShape, menu ou contrôleur n'a été modifié par ce diagnostic.");
            return b.ToString();
        }

        public static string DisableFaceTrackingFields(GameObject root)
        {
            var b = new StringBuilder();
            int changed = 0;
            var descriptors = root.GetComponentsInChildren<Component>(true)
                .Where(c => c && c.GetType().Name == "VRCAvatarDescriptor").ToArray();
            foreach (var descriptor in descriptors)
            {
                Undo.RegisterCompleteObjectUndo(descriptor, "Zyumie Disable Face Tracking Fields");
                var so = new SerializedObject(descriptor);
                so.Update();
                var p = so.GetIterator();
                bool enter = true;
                while (p.NextVisible(enter))
                {
                    enter = true;
                    if (p.propertyType == SerializedPropertyType.Boolean &&
                        p.propertyPath.ToLowerInvariant().Contains("facetracking") && p.boolValue)
                    {
                        p.boolValue = false;
                        changed++;
                        b.AppendLine("Désactivé : " + p.propertyPath);
                    }
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(descriptor);
            }
            if (descriptors.Length == 0) return "Aucun VRC Avatar Descriptor trouvé.";
            if (changed == 0) b.AppendLine("Aucun booléen FaceTracking actif n'a été trouvé.");
            b.AppendLine("Les menus, Animator Controllers, visèmes et BlendShapes ne sont pas supprimés.");
            return b.ToString();
        }

        private static string Describe(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: return p.boolValue.ToString();
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Float: return p.floatValue.ToString("0.###");
                case SerializedPropertyType.ObjectReference: return p.objectReferenceValue ? p.objectReferenceValue.name : "None";
                case SerializedPropertyType.Enum:
                    return p.enumDisplayNames != null && p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                        ? p.enumDisplayNames[p.enumValueIndex] : p.enumValueIndex.ToString();
                case SerializedPropertyType.String: return p.stringValue;
                default: return p.propertyType.ToString();
            }
        }

        private static bool IsLikelyEye(string name, bool left)
        {
            string n = name.ToLowerInvariant();
            string side = left ? "left" : "right";
            string suffixA = left ? ".l" : ".r";
            string suffixB = left ? "_l" : "_r";
            return n.Contains("eye") && (n.Contains(side) || n.EndsWith(suffixA) || n.EndsWith(suffixB));
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
