using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Adds Expressions > Function > FEO.
    /// EyesTracking uses VRChat's VRCAnimatorTrackingControl trackingEyes field.
    /// MainGestures uses a dedicated masked Gesture layer and Tracking Control for both finger groups.
    /// FrameEyeOSC itself remains an external OSC sender; this does not stop the headset application.
    /// </summary>
    internal static class FEOAutoIntegration
    {
        private const string FunctionName = "Function";
        private const string FEOName = "FEO";
        private const string EyesParameter = "EyesTracking";
        private const string GesturesParameter = "MainGestures";
        private const string EyeLayerName = "Zyumie_FEO_EyesTracking";
        private const string GestureLayerName = "Zyumie_FEO_MainGestures";
        private const string OutputRoot = "Assets/ZyumieAvatarSuite/Generated/FEO";

        private static readonly HumanBodyBones[] FingerBones =
        {
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
        };

        public static string Configure(GameObject root)
        {
            if (!root) return "ERREUR : sélectionne la racine de l'avatar.";
            var descriptor = root.GetComponentsInChildren<VRCAvatarDescriptor>(true).FirstOrDefault();
            if (!descriptor) return "ERREUR : aucun VRC Avatar Descriptor trouvé sous " + root.name + ".";

            Type trackingBehaviourType = FindTrackingControlType();
            if (trackingBehaviourType == null)
                return "ERREUR : VRCAnimatorTrackingControl introuvable. Vérifie que VRChat SDK - Avatars est installé et compilé.";

            var eyes = FindEyeBones(root, descriptor);
            var fingers = FindFingerBones(root);
            if (eyes.Count != 2)
                return "ERREUR : les deux os oculaires ne sont pas configurés dans le Descriptor. Aucun asset n'a été assigné. Vérifie Eye Look > Left Eye / Right Eye.";
            if (fingers.Count < 4)
                return "ERREUR : moins de 4 os de doigts ont été détectés. MainGestures n'a pas été généré pour éviter de verrouiller une main incomplète.";

            try
            {
                EnsureFolder("Assets/ZyumieAvatarSuite");
                EnsureFolder("Assets/ZyumieAvatarSuite/Generated");
                EnsureFolder(OutputRoot);
                string folder = OutputRoot + "/" + Sanitize(root.name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                EnsureFolder(folder);

                var report = new List<string>();
                var clonedMenu = BuildExpressionMenu(descriptor.expressionsMenu, folder, report);
                var clonedParameters = BuildExpressionParameters(descriptor.expressionParameters, folder, report);
                var fxController = ClonePlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.FX, folder, "vrc_AvatarV3FaceLayer", report);
                var gestureController = ClonePlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.Gesture, folder, "vrc_AvatarV3HandsLayer", report);

                EnsureBoolParameter(fxController, EyesParameter);
                EnsureBoolParameter(gestureController, GesturesParameter);
                RemoveLayerIfPresent(fxController, EyeLayerName);
                RemoveLayerIfPresent(gestureController, GestureLayerName);

                AddTrackingToggleLayer(fxController, EyeLayerName, EyesParameter, trackingBehaviourType,
                    new Dictionary<string, string> { { "trackingEyes", "Animation" } },
                    new Dictionary<string, string> { { "trackingEyes", "Tracking" } },
                    false, null);

                var fingerMask = CreateFingerMask(fingers, root.transform, folder);
                var neutralFingerPose = CreateFingerPoseClip(fingers, root.transform, folder);
                AddTrackingToggleLayer(gestureController, GestureLayerName, GesturesParameter, trackingBehaviourType,
                    new Dictionary<string, string> {
                        { "trackingLeftFingers", "Animation" }, { "trackingRightFingers", "Animation" }
                    },
                    new Dictionary<string, string> {
                        { "trackingLeftFingers", "Tracking" }, { "trackingRightFingers", "Tracking" }
                    },
                    true, new GesturePoseData { mask = fingerMask, disabledMotion = neutralFingerPose });

                // Only assign generated copies after both controllers and all assets have been built.
                descriptor.customExpressions = true;
                descriptor.expressionsMenu = clonedMenu;
                descriptor.expressionParameters = clonedParameters;
                AssignPlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.FX, fxController);
                AssignPlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.Gesture, gestureController);
                EditorUtility.SetDirty(descriptor);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                report.Add("Menu : Expressions > Function > FEO");
                report.Add("EyesTracking ON : VRChat/OSC eye look + eyelid tracking is restored.");
                report.Add("EyesTracking OFF : VRChat eye/eyelid tracking is set to Animation by VRCAnimatorTrackingControl.");
                report.Add("MainGestures ON : left/right finger tracking is restored.");
                report.Add("MainGestures OFF : finger tracking is switched to Animation and a neutral finger clip overrides controller gestures.");
                report.Add("Menus, parameters et controllers source conservés ; les copies sont assignées au Descriptor.");
                report.Add("FrameEyeOSC continue d'envoyer ses données : le toggle agit sur le comportement de suivi dans l'avatar/VRChat, pas sur l'application externe.");
                report.Add("Paupières : le trackingEyes natif de VRChat regroupe Eye Look et le clignement. OFF désactive les deux.");
                report.Add("Assets générés : " + folder);
                report.Add("Teste les quatre états (ON/OFF) avec VRChat Build & Test avant publication.");
                EditorUtility.SetDirty(fxController);
                EditorUtility.SetDirty(gestureController);
                return "ZYUMIE AVATAR SUITE — FEO v0.3.5\n\n" + string.Join("\n", report);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return "ERREUR FEO : " + ex.Message + "\nLe Descriptor n'a été assigné qu'après la génération des assets. Vérifie la Console Unity.";
            }
        }

        private sealed class GesturePoseData
        {
            public AvatarMask mask;
            public AnimationClip disabledMotion;
        }

        private static Type FindTrackingControlType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                var type = types.FirstOrDefault(t => t != null && t.Name == "VRCAnimatorTrackingControl" &&
                                                      typeof(StateMachineBehaviour).IsAssignableFrom(t) && !t.IsAbstract);
                if (type != null) return type;
            }
            return null;
        }

        private static List<Transform> FindEyeBones(GameObject root, VRCAvatarDescriptor descriptor)
        {
            var found = new List<Transform>();
            var so = new SerializedObject(descriptor);
            so.Update();
            AddTransform(found, so.FindProperty("customEyeLookSettings.leftEye"));
            AddTransform(found, so.FindProperty("customEyeLookSettings.rightEye"));
            if (found.Count < 2)
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var left = all.FirstOrDefault(t => Eq(t.name, "Eye.L") || Eq(t.name, "LeftEye"));
                var right = all.FirstOrDefault(t => Eq(t.name, "Eye.R") || Eq(t.name, "RightEye"));
                if (left && !found.Contains(left)) found.Add(left);
                if (right && !found.Contains(right)) found.Add(right);
            }
            return found.Where(t => t && t != root.transform && t.IsChildOf(root.transform)).Distinct().Take(2).ToList();
        }

        private static void AddTransform(List<Transform> list, SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return;
            var value = property.objectReferenceValue as Transform;
            if (value && !list.Contains(value)) list.Add(value);
        }

        private static List<Transform> FindFingerBones(GameObject root)
        {
            var found = new HashSet<Transform>();
            var animator = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.avatar && a.isHuman);
            if (animator)
            {
                foreach (var bone in FingerBones)
                {
                    try { var transform = animator.GetBoneTransform(bone); if (transform) found.Add(transform); }
                    catch { }
                }
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                string name = transform.name.ToLowerInvariant();
                bool isFinger = name.Contains("thumb") || name.Contains("index") || name.Contains("middle") ||
                                name.Contains("ring") || name.Contains("little") || name.Contains("pinky") || name.Contains("finger");
                if (!isFinger) continue;
                for (var parent = transform.parent; parent && parent != root.transform; parent = parent.parent)
                {
                    string p = parent.name.ToLowerInvariant();
                    if (p.Contains("hand") || p.Contains("wrist")) { found.Add(transform); break; }
                }
            }
            return found.Where(t => t && t != root.transform).OrderBy(t => AnimationUtility.CalculateTransformPath(t, root.transform)).ToList();
        }

        private static VRCExpressionsMenu BuildExpressionMenu(VRCExpressionsMenu original, string folder, List<string> report)
        {
            var root = original
                ? CloneMenuTree(original, folder, new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>())
                : CreateMenu("Zyumie_Expressions_Root", folder);

            var function = FindMenuByNameOrControl(root, FunctionName, new HashSet<VRCExpressionsMenu>());
            if (!function)
            {
                function = CreateMenu(FunctionName, folder);
                if (root.controls.Count < 8) root.controls.Add(SubMenuControl(FunctionName, function));
                else
                {
                    var wrapper = CreateMenu("Zyumie_Expressions_Root", folder);
                    wrapper.controls.Add(SubMenuControl(FunctionName, function));
                    wrapper.controls.Add(SubMenuControl("Menu avatar existant", root));
                    root = wrapper;
                    report.Add("Menu racine plein : wrapper créé sans supprimer les contrôles existants.");
                }
            }

            var feo = CreateMenu(FEOName, folder);
            feo.controls.Add(ToggleControl("EyesTracking", EyesParameter));
            feo.controls.Add(ToggleControl("MainGestures", GesturesParameter));

            int index = function.controls.FindIndex(c => Eq(c.name, FEOName));
            if (index >= 0)
            {
                var control = function.controls[index];
                control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
                control.subMenu = feo;
                function.controls[index] = control;
            }
            else if (function.controls.Count < 8) function.controls.Add(SubMenuControl(FEOName, feo));
            else
            {
                var overflow = CreateMenu("Function_Other_Options", folder);
                foreach (var control in function.controls) overflow.controls.Add(control);
                function.controls.Clear();
                function.controls.Add(SubMenuControl("Autres fonctions", overflow));
                function.controls.Add(SubMenuControl(FEOName, feo));
                report.Add("Function plein : les contrôles existants sont conservés dans « Autres fonctions ».");
            }

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(function);
            return root;
        }

        private static VRCExpressionsMenu FindMenuByNameOrControl(VRCExpressionsMenu menu, string name, HashSet<VRCExpressionsMenu> visited)
        {
            if (!menu || !visited.Add(menu)) return null;
            if (Eq(menu.name, name)) return menu;
            if (menu.controls == null) return null;
            foreach (var control in menu.controls)
            {
                if (Eq(control.name, name) && control.subMenu) return control.subMenu;
                var nested = FindMenuByNameOrControl(control.subMenu, name, visited);
                if (nested) return nested;
            }
            return null;
        }

        private static VRCExpressionsMenu CloneMenuTree(VRCExpressionsMenu source, string folder, Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> clones)
        {
            if (!source) return null;
            if (clones.TryGetValue(source, out var known)) return known;
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name + "_Zyumie035";
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(copy.name) + ".asset"));
            clones[source] = copy;
            if (copy.controls != null)
            {
                for (int i = 0; i < copy.controls.Count; i++)
                {
                    var control = copy.controls[i];
                    if (control.subMenu) control.subMenu = CloneMenuTree(control.subMenu, folder, clones);
                    copy.controls[i] = control;
                }
            }
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static VRCExpressionsMenu CreateMenu(string name, string folder)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = name;
            AssetDatabase.CreateAsset(menu, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(name) + ".asset"));
            return menu;
        }

        private static VRCExpressionsMenu.Control SubMenuControl(string name, VRCExpressionsMenu target)
        {
            return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = target };
        }

        private static VRCExpressionsMenu.Control ToggleControl(string name, string parameter)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = 1f
            };
        }

        private static VRCExpressionParameters BuildExpressionParameters(VRCExpressionParameters original, string folder, List<string> report)
        {
            var copy = original ? UnityEngine.Object.Instantiate(original) : ScriptableObject.CreateInstance<VRCExpressionParameters>();
            copy.name = "Zyumie_FEO_ExpressionParameters";
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/Zyumie_FEO_ExpressionParameters.asset"));
            var list = copy.parameters != null ? copy.parameters.ToList() : new List<VRCExpressionParameters.Parameter>();
            UpsertBool(list, EyesParameter);
            UpsertBool(list, GesturesParameter);
            copy.parameters = list.ToArray();

            int cost = list.Sum(p => p.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8);
            report.Add("Expression Parameters : coût estimé " + cost + "/256 bits" + (cost > 256 ? " — dépassement à corriger" : ""));
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static void UpsertBool(List<VRCExpressionParameters.Parameter> list, string name)
        {
            int index = list.FindIndex(p => p.name == name);
            if (index >= 0 && list[index].valueType != VRCExpressionParameters.ValueType.Bool)
                throw new InvalidOperationException("Le paramètre « " + name + " » existe déjà mais n'est pas de type Bool.");
            var item = index >= 0 ? list[index] : new VRCExpressionParameters.Parameter { name = name };
            item.name = name;
            item.valueType = VRCExpressionParameters.ValueType.Bool;
            item.defaultValue = 1f;
            item.saved = true;
            item.networkSynced = true;
            if (index >= 0) list[index] = item; else list.Add(item);
        }

        private static AnimatorController ClonePlayableController(
            VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType layerType,
            string folder,
            string defaultSampleName,
            List<string> report)
        {
            var serialized = new SerializedObject(descriptor);
            serialized.Update();
            var layers = serialized.FindProperty("baseAnimationLayers");
            if (layers == null || !layers.isArray) throw new InvalidOperationException("baseAnimationLayers introuvable sur le Descriptor.");

            SerializedProperty layerElement = null;
            for (int i = 0; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                var type = element.FindPropertyRelative("type");
                if (type != null && type.enumValueIndex >= 0 && type.enumValueIndex < type.enumNames.Length &&
                    Eq(type.enumNames[type.enumValueIndex], layerType.ToString()))
                { layerElement = element; break; }
            }
            if (layerElement == null) throw new InvalidOperationException("Slot " + layerType + " introuvable.");

            var controllerProperty = layerElement.FindPropertyRelative("animatorController");
            if (controllerProperty == null) throw new InvalidOperationException("Référence Controller inaccessible pour " + layerType + ".");
            var current = controllerProperty.objectReferenceValue as RuntimeAnimatorController;
            if (current && !(current is AnimatorController))
                throw new InvalidOperationException("Le contrôleur " + layerType + " est un AnimatorOverrideController. Conversion manuelle nécessaire pour préserver les overrides.");

            string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/Zyumie_FEO_" + layerType + ".controller");
            AnimatorController result = null;
            if (current is AnimatorController custom)
            {
                string path = AssetDatabase.GetAssetPath(custom);
                if (string.IsNullOrEmpty(path) || !AssetDatabase.CopyAsset(path, destination))
                    throw new InvalidOperationException("Impossible de dupliquer le contrôleur " + layerType + ".");
                result = AssetDatabase.LoadAssetAtPath<AnimatorController>(destination);
                report.Add("Controller " + layerType + " personnalisé dupliqué.");
            }
            else
            {
                string sample = AssetDatabase.FindAssets(defaultSampleName + " t:AnimatorController")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(defaultSampleName, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(sample) || !AssetDatabase.CopyAsset(sample, destination))
                    throw new InvalidOperationException("Sample " + defaultSampleName + " introuvable. Importe les AV3 Demo Assets du SDK VRChat. Aucun controller vide ne sera assigné.");
                result = AssetDatabase.LoadAssetAtPath<AnimatorController>(destination);
                report.Add("Controller " + layerType + " par défaut du SDK dupliqué (" + defaultSampleName + ").");
            }
            if (!result) throw new InvalidOperationException("Controller " + layerType + " généré introuvable.");
            return result;
        }

        private static void AssignPlayableController(VRCAvatarDescriptor descriptor, VRCAvatarDescriptor.AnimLayerType layerType, AnimatorController controller)
        {
            var serialized = new SerializedObject(descriptor);
            serialized.Update();
            var layers = serialized.FindProperty("baseAnimationLayers");
            if (layers == null || !layers.isArray) throw new InvalidOperationException("baseAnimationLayers inaccessible.");
            for (int i = 0; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                var type = element.FindPropertyRelative("type");
                if (type == null || type.enumValueIndex < 0 || type.enumValueIndex >= type.enumNames.Length ||
                    !Eq(type.enumNames[type.enumValueIndex], layerType.ToString())) continue;
                var controllerProperty = element.FindPropertyRelative("animatorController");
                var defaultProperty = element.FindPropertyRelative("isDefault");
                if (controllerProperty == null) throw new InvalidOperationException("Référence controller " + layerType + " inaccessible.");
                controllerProperty.objectReferenceValue = controller;
                if (defaultProperty != null) defaultProperty.boolValue = false;
                var customizeLayers = serialized.FindProperty("customizeAnimationLayers");
                if (customizeLayers != null && customizeLayers.propertyType == SerializedPropertyType.Boolean)
                    customizeLayers.boolValue = true;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(descriptor);
                return;
            }
            throw new InvalidOperationException("Slot " + layerType + " non trouvé lors de l'assignation.");
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name)
        {
            var parameters = controller.parameters;
            int index = Array.FindIndex(parameters, p => p.name == name);
            if (index >= 0)
            {
                if (parameters[index].type != AnimatorControllerParameterType.Bool)
                    throw new InvalidOperationException("Le paramètre Animator « " + name + " » existe mais n'est pas Bool.");
                parameters[index].defaultBool = true;
                controller.parameters = parameters;
            }
            else controller.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Bool, defaultBool = true });
        }

        private static void RemoveLayerIfPresent(AnimatorController controller, string name)
        {
            for (int i = controller.layers.Length - 1; i >= 0; i--)
                if (controller.layers[i].name == name) controller.RemoveLayer(i);
        }

        private static void AddTrackingToggleLayer(
            AnimatorController controller,
            string layerName,
            string parameter,
            Type trackingBehaviourType,
            Dictionary<string, string> disabledFields,
            Dictionary<string, string> enabledFields,
            bool isGestureLayer,
            GesturePoseData gesturePose)
        {
            controller.AddLayer(layerName);
            var layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[index].defaultWeight = 1f;
            if (isGestureLayer && gesturePose != null) layers[index].avatarMask = gesturePose.mask;
            controller.layers = layers;

            var machine = controller.layers[index].stateMachine;
            var enabled = machine.AddState("Enabled_Tracking");
            enabled.writeDefaultValues = false;
            var disabled = machine.AddState("Disabled_Animation");
            disabled.writeDefaultValues = false;
            if (isGestureLayer && gesturePose != null) disabled.motion = gesturePose.disabledMotion;
            machine.defaultState = enabled;

            SetTrackingBehaviour(enabled, trackingBehaviourType, enabledFields);
            SetTrackingBehaviour(disabled, trackingBehaviourType, disabledFields);

            var toDisabled = enabled.AddTransition(disabled);
            ConfigureTransition(toDisabled, parameter, false);
            var toEnabled = disabled.AddTransition(enabled);
            ConfigureTransition(toEnabled, parameter, true);
        }

        private static void SetTrackingBehaviour(AnimatorState state, Type type, Dictionary<string, string> fields)
        {
            var behaviour = state.AddStateMachineBehaviour(type);
            if (!behaviour) throw new InvalidOperationException("Impossible d'ajouter VRCAnimatorTrackingControl à l'état " + state.name + ".");
            var serialized = new SerializedObject(behaviour);
            serialized.Update();
            foreach (var pair in fields)
            {
                var property = serialized.FindProperty(pair.Key);
                if (property == null || property.propertyType != SerializedPropertyType.Enum)
                    throw new InvalidOperationException("Champ " + pair.Key + " absent de VRCAnimatorTrackingControl.");
                int index = Array.FindIndex(property.enumNames, n => string.Equals(n, pair.Value, StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw new InvalidOperationException("Valeur " + pair.Value + " introuvable pour " + pair.Key + ".");
                property.enumValueIndex = index;
            }
            serialized.ApplyModifiedProperties();
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, string parameter, bool toEnabled)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.offset = 0f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(toEnabled ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static AvatarMask CreateFingerMask(List<Transform> fingers, Transform root, string folder)
        {
            var mask = new AvatarMask();
            mask.name = "Zyumie_FEO_Fingers_Mask";
            AvatarMaskBodyPart[] all = {
                AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
                AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
                AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK,
                AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK
            };
            foreach (var part in all) mask.SetHumanoidBodyPartActive(part, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            foreach (var finger in fingers.Where(t => t && t != root && t.IsChildOf(root)))
            {
                mask.AddTransformPath(finger, false);
                int idx = mask.transformCount - 1;
                if (idx >= 0) mask.SetTransformActive(idx, true);
            }
            AssetDatabase.CreateAsset(mask, AssetDatabase.GenerateUniqueAssetPath(folder + "/Zyumie_FEO_Fingers.mask"));
            return mask;
        }

        private static AnimationClip CreateFingerPoseClip(List<Transform> fingers, Transform root, string folder)
        {
            var clip = new AnimationClip { name = "Zyumie_FEO_Fingers_Neutral", frameRate = 60f, wrapMode = WrapMode.Loop };
            foreach (var finger in fingers.Where(t => t && t != root && t.IsChildOf(root)))
            {
                string path = AnimationUtility.CalculateTransformPath(finger, root);
                Quaternion q = finger.localRotation;
                SetCurve(clip, path, "m_LocalRotation.x", q.x);
                SetCurve(clip, path, "m_LocalRotation.y", q.y);
                SetCurve(clip, path, "m_LocalRotation.z", q.z);
                SetCurve(clip, path, "m_LocalRotation.w", q.w);
            }
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/Zyumie_FEO_Fingers_Neutral.anim"));
            return clip;
        }

        private static void SetCurve(AnimationClip clip, string path, string property, float value)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0f, value), new Keyframe(1f, value)));
        }

        private static void EnsureFolder(string path)
        {
            path = path.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        private static string SafeName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return string.IsNullOrEmpty(value) ? "Avatar" : value.Replace("/", "_").Replace("\\", "_");
        }

        private static bool Eq(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
