using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Read-only Animator / Avatar Mask audit for VRChat avatar playable layers.
    /// Nothing in the avatar or its controllers is modified.
    /// </summary>
    internal static class AvatarAnimatorDiagnostics
    {
        public static string Report(GameObject root)
        {
            if (!root) return "Sélectionne la racine de l'avatar.";

            var report = new StringBuilder();
            report.AppendLine("ZYUMIE AVATAR SUITE — DIAGNOSTIC ANIMATOR v0.6.0");
            report.AppendLine("Avatar : " + root.name);
            var descriptors = root.GetComponentsInChildren<VRCAvatarDescriptor>(true);
            if (descriptors.Length == 0)
                return report.AppendLine("Aucun VRC Avatar Descriptor trouvé.").ToString();

            foreach (var descriptor in descriptors)
            {
                report.AppendLine("\nDescriptor : " + descriptor.name);
                var serialized = new SerializedObject(descriptor);
                serialized.Update();
                var layers = serialized.FindProperty("baseAnimationLayers");
                if (layers == null || !layers.isArray)
                {
                    report.AppendLine("  baseAnimationLayers : propriété indisponible.");
                    continue;
                }

                for (int i = 0; i < layers.arraySize; i++)
                {
                    var element = layers.GetArrayElementAtIndex(i);
                    var type = element.FindPropertyRelative("type");
                    string typeName = ReadEnum(type);
                    var controller = element.FindPropertyRelative("animatorController")?.objectReferenceValue as RuntimeAnimatorController;
                    bool isDefault = element.FindPropertyRelative("isDefault")?.boolValue ?? false;
                    report.AppendLine("\n  Playable Layer : " + typeName + " | Controller : " + (controller ? controller.name : "non assigné") + " | Default : " + isDefault);

                    var animator = controller as AnimatorController;
                    if (!animator)
                    {
                        if (controller) report.AppendLine("    Type de controller non inspectable (AnimatorOverrideController ou contrôleur externe).");
                        continue;
                    }

                    var animatorLayers = animator.layers;
                    for (int layerIndex = 0; layerIndex < animatorLayers.Length; layerIndex++)
                    {
                        var layer = animatorLayers[layerIndex];
                        int stateCount = layer.stateMachine ? CountStates(layer.stateMachine) : 0;
                        string maskName = layer.avatarMask ? layer.avatarMask.name : "aucun";
                        report.AppendLine("    [" + layerIndex + "] " + layer.name +
                            " | poids=" + layer.defaultWeight.ToString("0.##") +
                            " | blend=" + layer.blendingMode +
                            " | mask=" + maskName +
                            " | états=" + stateCount +
                            " | sync=" + layer.syncedLayerIndex);

                        if (layer.avatarMask)
                        {
                            int activeTransforms = 0;
                            for (int maskIndex = 0; maskIndex < layer.avatarMask.transformCount; maskIndex++)
                                if (layer.avatarMask.GetTransformActive(maskIndex)) activeTransforms++;
                            report.AppendLine("       Avatar Mask : " + activeTransforms + "/" + layer.avatarMask.transformCount +
                                " transforms actifs; masque humanoïde Root=" + layer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root));
                            if (string.Equals(typeName, "Gesture", StringComparison.OrdinalIgnoreCase))
                                report.AppendLine("       À vérifier : le masque Gesture limite volontairement certaines parties du rig. Ce n'est pas automatiquement une erreur.");
                        }

                        if (layer.name.IndexOf("lowarms", StringComparison.OrdinalIgnoreCase) >= 0 && layerIndex != 0)
                            report.AppendLine("       AVERTISSEMENT : une couche nommée lowarms n'est pas en première position. Vérifie l'ordre attendu par ton contrôleur et ses transitions; le nom seul ne prouve pas une erreur.");
                    }

                    if (typeName == "FX" || typeName == "Gesture")
                    {
                        report.AppendLine("    Paramètres Animator : " + string.Join(", ", animator.parameters.Select(p => p.name).Take(50)));
                        if (animator.parameters.Length > 50) report.AppendLine("    ... autres paramètres masqués.");
                    }
                }
            }

            var lowArmsObjects = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.IndexOf("lowarms", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            report.AppendLine("\nHiérarchie — objets nommés lowarms : " + lowArmsObjects.Length);
            foreach (var transform in lowArmsObjects.Take(20))
            {
                report.AppendLine("  - " + PathOf(transform) + " | sibling index=" + transform.GetSiblingIndex());
                if (transform.parent == root.transform && transform.GetSiblingIndex() != 0)
                    report.AppendLine("    Note : objet enfant direct de la racine, mais pas premier enfant. Vérifie les conventions de ton rig avant de le déplacer.");
            }
            if (lowArmsObjects.Length == 0) report.AppendLine("  Aucun objet lowarms trouvé.");

            report.AppendLine("\nCe rapport est en lecture seule. Les Avatar Masks peuvent être intentionnels; compare-les avec les clips et le rig avant toute modification.");
            return report.ToString();
        }

        private static int CountStates(AnimatorStateMachine machine)
        {
            if (!machine) return 0;
            int count = machine.states.Length;
            foreach (var child in machine.stateMachines)
                count += CountStates(child.stateMachine);
            return count;
        }

        private static string ReadEnum(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum) return "inconnu";
            return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                ? property.enumNames[property.enumValueIndex] : "inconnu";
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            while (transform.parent)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
