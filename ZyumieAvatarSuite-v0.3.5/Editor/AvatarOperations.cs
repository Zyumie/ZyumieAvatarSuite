using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

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
            b.AppendLine("ZYUMIE — EYE / FACE TRACKING CHECK");
            var descriptors = root.GetComponentsInChildren<Component>(true)
                .Where(c => c && c.GetType().Name == "VRCAvatarDescriptor").ToArray();
            b.AppendLine("VRC Avatar Descriptor(s) : " + descriptors.Length);
            foreach (var descriptor in descriptors)
            {
                var so = new SerializedObject(descriptor);
                so.Update();
                b.AppendLine("\nDescriptor : " + descriptor.name);
                var p = so.GetIterator();
                bool enter = true;
                while (p.NextVisible(enter))
                {
                    enter = true;
                    string path = p.propertyPath.ToLowerInvariant();
                    if (path.Contains("eye") || path.Contains("eyelid") || path.Contains("lip") || path.Contains("face") || path.Contains("tracking"))
                        b.AppendLine("  " + p.propertyPath + " = " + Describe(p));
                }
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var left = transforms.FirstOrDefault(t => IsLikelyEye(t.name, true));
            var right = transforms.FirstOrDefault(t => IsLikelyEye(t.name, false));
            b.AppendLine("\nDétection par nom (indicative)");
            b.AppendLine("  Œil gauche : " + (left ? PathOf(left) : "non détecté"));
            b.AppendLine("  Œil droit : " + (right ? PathOf(right) : "non détecté"));

            int eye = 0, lids = 0, mouth = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.sharedMesh) continue;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string n = smr.sharedMesh.GetBlendShapeName(i).ToLowerInvariant();
                    if (n.Contains("eye") || n.Contains("iris") || n.Contains("look")) eye++;
                    if (n.Contains("lid") || n.Contains("blink") || n.Contains("close")) lids++;
                    if (n.Contains("mouth") || n.Contains("jaw") || n.Contains("viseme")) mouth++;
                }
            }
            b.AppendLine("BlendShapes regard : " + eye);
            b.AppendLine("BlendShapes paupières : " + lids);
            b.AppendLine("BlendShapes bouche/visèmes : " + mouth);
            b.AppendLine("\nFrameEyeOSC doit envoyer les messages OSC natifs /tracking/eye/... vers VRChat.");
            b.AppendLine("Des messages OSC visibles ne prouvent pas à eux seuls que le suivi est appliqué à l'avatar.");
            b.AppendLine("Aucun os ni BlendShape n'a été modifié par ce diagnostic.");
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
