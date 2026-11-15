using System;
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
            int removed = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                Undo.RegisterCompleteObjectUndo(t.gameObject, "Zyumie Remove Missing Scripts");
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            return removed;
        }

        public static GameObject CreatePlatformCopy(GameObject source, string suffix, bool prepareMobile)
        {
            var copy = UnityEngine.Object.Instantiate(source, source.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Zyumie Create Platform Copy");
            copy.name = source.name + suffix;

            if (prepareMobile)
                StripComponentsUnsupportedOnMobile(copy);

            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static void StripComponentsUnsupportedOnMobile(GameObject root)
        {
            // Only applied to a newly-created duplicate. VRChat mobile disables these component families.
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (!c) continue;
                bool remove = c is Light || c is Cloth || c is AudioSource ||
                              c is Rigidbody || c is Collider;
                if (remove)
                    Undo.DestroyObjectImmediate(c);
            }
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
                b.AppendLine("");
                b.AppendLine("Descriptor : " + descriptor.name);
                var p = so.GetIterator();
                bool enter = true;
                int found = 0;
                while (p.NextVisible(enter))
                {
                    enter = true;
                    string path = p.propertyPath.ToLowerInvariant();
                    if (!(path.Contains("eye") || path.Contains("eyelid") ||
                          path.Contains("lip") || path.Contains("face") ||
                          path.Contains("tracking"))) continue;

                    string value = Describe(p);
                    b.AppendLine("  " + p.propertyPath + " = " + value);
                    found++;
                }
                if (found == 0) b.AppendLine("  Aucun champ de tracking reconnu par le sérialiseur.");
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var left = transforms.FirstOrDefault(t => IsLikelyEye(t.name, true));
            var right = transforms.FirstOrDefault(t => IsLikelyEye(t.name, false));
            b.AppendLine("");
            b.AppendLine("Détection par nom (indicative seulement)");
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
            b.AppendLine("");
            b.AppendLine("FrameEyeOSC : les points OSC qui bougent prouvent que des données arrivent, mais pas qu'elles sont appliquées à l'avatar.");
            b.AppendLine("Vérifier l'option de sortie native VRChat built-in eye tracking dans FrameEyeOSC, si disponible dans cette version.");
            b.AppendLine("Activer OSC dans VRChat et éviter deux émetteurs concurrents (notamment la sortie OSC Steam Link).");
            b.AppendLine("VRChat repasse en auto-look / auto-blink après 10 secondes sans données de tracking.");
            b.AppendLine("");
            b.AppendLine("Bouche : FrameEyeOSC ne fournit pas un suivi optique des lèvres. Utiliser les visèmes audio VRChat ou une source de face tracking distincte.");
            b.AppendLine("Aucune assignation automatique d'os ou de blendshapes n'a été faite : elle dépend du rig et de l'orientation de chaque avatar.");
            return b.ToString();
        }

        public static string DisableFaceTrackingFields(GameObject root)
        {
            var b = new StringBuilder();
            int changed = 0;
            var descriptors = root.GetComponentsInChildren<Component>(true)
                .Where(c => c && c.GetType().Name == "VRCAvatarDescriptor").ToArray();

            foreach (var d in descriptors)
            {
                Undo.RegisterCompleteObjectUndo(d, "Zyumie Disable Face Tracking Fields");
                var so = new SerializedObject(d);
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
                EditorUtility.SetDirty(d);
            }
            if (descriptors.Length == 0) return "Aucun VRC Avatar Descriptor trouvé.";
            if (changed == 0) b.AppendLine("Aucun booléen sérialisé nommé FaceTracking n'a été trouvé/activé sur ce SDK.");
            b.AppendLine("Attention : les paramètres Animator, menus et blendshapes Face Tracking ne sont pas supprimés.");
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
                    return p.enumDisplayNames != null && p.enumDisplayNames.Length > p.enumValueIndex
                        ? p.enumDisplayNames[p.enumValueIndex] : p.enumValueIndex.ToString();
                case SerializedPropertyType.String: return p.stringValue;
                default: return p.propertyType.ToString();
            }
        }

        private static bool IsLikelyEye(string n, bool left)
        {
            n = n.ToLowerInvariant();
            string sideA = left ? "left" : "right";
            string sideB = left ? ".l" : ".r";
            string sideC = left ? "_l" : "_r";
            return n.Contains("eye") && (n.Contains(sideA) || n.EndsWith(sideB) || n.EndsWith(sideC));
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
