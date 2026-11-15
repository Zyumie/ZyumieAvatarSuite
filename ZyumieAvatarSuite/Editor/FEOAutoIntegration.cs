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
    /// FEO integration for VRChat Avatars SDK 3.10.6+.
    /// - Configures the native Unified Expressions Face Tracking descriptor from matching blendshapes.
    /// - Keeps Eye Look tracking available through VRCAnimatorTrackingControl.
    /// - MainGestures changes only FX layers proven to be gesture-driven facial blendshape layers.
    /// - Removes the old FEO finger-neutralization layer from the Gesture controller.
    /// </summary>
    internal static class FEOAutoIntegration
    {
        private const string FunctionName = "Function";
        private const string FEOName = "FEO";
        private const string EyesParameter = "EyesTracking";
        private const string GesturesParameter = "MainGestures";
        private const string EyeLayerName = "Zyumie_FEO_EyesTracking";
        private const string GestureFaceLayerName = "Zyumie_FEO_FacialGestures";
        private const string LegacyGestureLayerName = "Zyumie_FEO_MainGestures";
        private const string OutputRoot = "Assets/ZyumieAvatarSuite/Generated/FEO";
        private const int MaxFaceTrackingMeshes = 16;

        // Official VRChat Unified Expressions input order (SDK 3.10.6 / OSC /tracking/face/v1).
        private static readonly string[] TrackingKeys =
        {
            "EyeLookOutRight", "EyeLookInRight", "EyeLookUpRight", "EyeLookDownRight",
            "EyeLookOutLeft", "EyeLookInLeft", "EyeLookUpLeft", "EyeLookDownLeft",
            "EyeClosedRight", "EyeClosedLeft", "EyeDilationRight", "EyeDilationLeft",
            "EyeConstrictRight", "EyeConstrictLeft", "EyeSquintRight", "EyeSquintLeft",
            "EyeWideRight", "EyeWideLeft", "BrowPinchRight", "BrowPinchLeft",
            "BrowLowererRight", "BrowLowererLeft", "BrowInnerUpRight", "BrowInnerUpLeft",
            "BrowOuterUpRight", "BrowOuterUpLeft", "NasalDilationRight", "NasalDilationLeft",
            "NasalConstrictRight", "NasalConstrictLeft", "CheekSquintRight", "CheekSquintLeft",
            "CheekPuffRight", "CheekPuffLeft", "CheekSuckRight", "CheekSuckLeft",
            "JawOpen", "JawRight", "JawLeft", "JawForward", "JawBackward", "JawClench",
            "JawMandibleRaise", "MouthClosed", "LipSuckUpperRight", "LipSuckUpperLeft",
            "LipSuckLowerRight", "LipSuckLowerLeft", "LipSuckCornerRight", "LipSuckCornerLeft",
            "LipFunnelUpperRight", "LipFunnelUpperLeft", "LipFunnelLowerRight", "LipFunnelLowerLeft",
            "LipPuckerUpperRight", "LipPuckerUpperLeft", "LipPuckerLowerRight", "LipPuckerLowerLeft",
            "MouthUpperUpRight", "MouthUpperUpLeft", "MouthUpperDeepenRight", "MouthUpperDeepenLeft",
            "NoseSneerRight", "NoseSneerLeft", "MouthLowerDownRight", "MouthLowerDownLeft",
            "MouthUpperRight", "MouthUpperLeft", "MouthLowerRight", "MouthLowerLeft",
            "MouthCornerPullRight", "MouthCornerPullLeft", "MouthCornerSlantRight", "MouthCornerSlantLeft",
            "MouthFrownRight", "MouthFrownLeft", "MouthStretchRight", "MouthStretchLeft",
            "MouthDimpleRight", "MouthDimpleLeft", "MouthRaiserUpper", "MouthRaiserLower",
            "MouthPressRight", "MouthPressLeft", "MouthTightenerRight", "MouthTightenerLeft",
            "TongueOut", "TongueUp", "TongueDown", "TongueRight", "TongueLeft", "TongueRoll",
            "TongueBendDown", "TongueCurlUp", "TongueSquish", "TongueFlat", "TongueTwistRight",
            "TongueTwistLeft", "SoftPalateClose", "ThroatSwallow", "NeckFlexRight", "NeckFlexLeft"
        };

        private static readonly Dictionary<string, string[]> ShapeAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "EyeClosedLeft", new[] { "EyeBlinkLeft", "BlinkLeft", "Blink_L", "EyeCloseLeft", "EyeClose_L", "CloseEye_L" } },
            { "EyeClosedRight", new[] { "EyeBlinkRight", "BlinkRight", "Blink_R", "EyeCloseRight", "EyeClose_R", "CloseEye_R" } },
            { "MouthClosed", new[] { "MouthClose", "MouthClosed" } },
            { "MouthRaiserUpper", new[] { "MouthShrugUpper", "MouthUpperRaiser" } },
            { "MouthRaiserLower", new[] { "MouthShrugLower", "MouthLowerRaiser" } },
            { "LipFunnelUpperRight", new[] { "MouthFunnel", "LipFunnel" } },
            { "LipPuckerUpperRight", new[] { "MouthPucker", "LipPucker" } }
        };

        private sealed class FaceTrackingResult
        {
            public int mappedKeys;
            public int mappedEyeLook;
            public bool leftBlink;
            public bool rightBlink;
            public int meshes;
            public bool nativeConfigured;
            public readonly List<string> warnings = new List<string>();
        }

        public static string Configure(GameObject root)
        {
            if (!root) return "ERREUR : sélectionne la racine de l'avatar.";
            VRCAvatarDescriptor descriptor = root.GetComponentsInChildren<VRCAvatarDescriptor>(true).FirstOrDefault();
            if (!descriptor) return "ERREUR : aucun VRC Avatar Descriptor trouvé sous " + root.name + ".";

            Type trackingControlType = FindBehaviourType("VRCAnimatorTrackingControl");
            if (trackingControlType == null)
                return "ERREUR : VRCAnimatorTrackingControl introuvable. Vérifie l'installation du VRChat SDK - Avatars.";

            List<Transform> eyes = FindEyeBones(root, descriptor);
            if (eyes.Count != 2)
                return "ERREUR : les deux yeux ne sont pas configurés. Renseigne Eye Look > Left Eye / Right Eye dans le Descriptor.";

            try
            {
                EnsureFolder("Assets/ZyumieAvatarSuite");
                EnsureFolder("Assets/ZyumieAvatarSuite/Generated");
                EnsureFolder(OutputRoot);
                string folder = OutputRoot + "/" + SafeName(root.name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                EnsureFolder(folder);

                Undo.RecordObject(descriptor, "Zyumie FEO Tracking Setup");
                var report = new List<string>();
                AnimatorController fxController = ClonePlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.FX,
                    folder, "vrc_AvatarV3FaceLayer", report);
                AnimatorController gestureController = ClonePlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.Gesture,
                    folder, "vrc_AvatarV3HandsLayer", report);

                // v0.3.9 put a neutral-pose layer in Gesture and stopped normal finger movement.
                // Remove that layer from the duplicated Gesture controller. Do not add any finger override.
                RemoveLayerIfPresent(gestureController, LegacyGestureLayerName);

                List<int> facialGestureLayers = FindGestureDrivenFacialLayers(fxController);
                Type layerControlType = FindBehaviourType("VRCAnimatorLayerControl");
                bool canToggleFacialGestures = facialGestureLayers.Count > 0 && layerControlType != null;
                if (facialGestureLayers.Count > 0 && layerControlType == null)
                    report.Add("AVERTISSEMENT : des couches faciales pilotées par les gestes ont été trouvées, mais VRCAnimatorLayerControl est introuvable. MainGestures ne sera pas ajouté.");

                EnsureBoolParameter(fxController, EyesParameter, true);
                if (canToggleFacialGestures) EnsureBoolParameter(fxController, GesturesParameter, true);
                RemoveLayerIfPresent(fxController, EyeLayerName);
                RemoveLayerIfPresent(fxController, GestureFaceLayerName);

                AddEyeTrackingToggleLayer(fxController, trackingControlType);
                if (canToggleFacialGestures)
                    AddFacialGestureToggleLayer(fxController, layerControlType, facialGestureLayers);

                // Prepare menu and synced parameters only after knowing whether a valid facial layer exists.
                VRCExpressionsMenu clonedMenu = BuildExpressionMenu(descriptor.expressionsMenu, folder, canToggleFacialGestures, report);
                VRCExpressionParameters clonedParameters = BuildExpressionParameters(
                    descriptor.expressionParameters, folder, canToggleFacialGestures, report);

                // Native Face Tracking descriptor support was added in Avatars SDK 3.10.6.
                // Bone-based Eye Look does not require facial BlendShapes or the native
                // Unified Expressions Face Tracking section. Configure it first so an
                // avatar with custom/non-standard BlendShape names can still use eye gaze.
                ConfigureLegacyEyeLook(descriptor, eyes);

                FaceTrackingResult faceTracking = ConfigureNativeFaceTracking(descriptor, root, eyes);
                report.Add("Face Tracking natif : " + (faceTracking.nativeConfigured ? "activé" : "non activé") + " — " +
                    faceTracking.mappedKeys + " clé(s) mappée(s) sur " + TrackingKeys.Length + ".");
                report.Add("Meshes Face Tracking : " + faceTracking.meshes + "/" + MaxFaceTrackingMeshes + ".");
                report.Add("Eye Look : " + faceTracking.mappedEyeLook + " mapping(s) EyeLook* détecté(s).");
                report.Add("Clignement natif : gauche=" + (faceTracking.leftBlink ? "mappé" : "non mappé") + ", droite=" + (faceTracking.rightBlink ? "mappé" : "non mappé") + ".");
                foreach (string warning in faceTracking.warnings) report.Add("AVERTISSEMENT : " + warning);

                // Keep the original Gesture controller's content; only strip the obsolete FEO finger layer.
                Undo.RecordObject(descriptor, "Zyumie FEO Tracking Setup");
                descriptor.customExpressions = true;
                descriptor.expressionsMenu = clonedMenu;
                descriptor.expressionParameters = clonedParameters;
                AssignPlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.FX, fxController);
                AssignPlayableController(descriptor, VRCAvatarDescriptor.AnimLayerType.Gesture, gestureController);
                EditorUtility.SetDirty(descriptor);
                EditorUtility.SetDirty(fxController);
                EditorUtility.SetDirty(gestureController);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                report.Insert(0, "Menu : Expressions > Function > FEO");
                report.Add("EyesTracking ON : laisse VRChat gérer le tracking des yeux. OFF : rend le contrôle des yeux aux animations.");
                if (canToggleFacialGestures)
                {
                    report.Add("MainGestures ON/OFF : contrôle uniquement les couches FX détectées comme pilotées par GestureLeft/GestureRight et animant des BlendShapes.");
                    report.Add("Le contrôleur Gesture n'est plus neutralisé : les mains et les doigts conservent leur tracking normal.");
                    report.Add("Couches faciales ciblées : " + string.Join(", ", facialGestureLayers.Select(i => fxController.layers[i].name)) + ".");
                    report.Add("Attention : une couche ciblée entière est masquée. Si une même couche mélange expressions de gestes et autres animations faciales, ces animations seront également masquées pendant OFF.");
                }
                else report.Add("MainGestures non ajouté : aucune couche FX faciale liée aux paramètres GestureLeft/GestureRight n'a pu être identifiée de façon sûre.");
                report.Add("UserEyeTracking et UserFaceTracking sont des paramètres intégrés VRChat : FEO ne les ajoute pas aux Expression Parameters.");
                if (faceTracking.nativeConfigured)
                {
                    report.Add("Pour FrameEyeOSC / Unified Expressions, la source doit envoyer le blob OSC /tracking/face/v1 (102 octets) et le Descriptor doit avoir Use All Tracking Keys activé.");
                    report.Add("Les messages /tracking/eye/* sont le protocole OSC historique et ne remplacent pas le blob /tracking/face/v1 pour les expressions faciales natives.");
                }
                else
                {
                    report.Add("Fallback utilisé : Eye Look est configuré en Bone Rotation avec les deux os du Descriptor. Les messages OSC historiques /tracking/eye/* peuvent piloter le regard si FrameEyeOSC les envoie correctement.");
                    report.Add("Les expressions faciales natives restent indisponibles tant que les BlendShapes de l'avatar ne sont pas associés manuellement aux clés Unified Expressions.");
                }
                report.Add("Assets générés : " + folder);
                report.Add("Contrôle final : utilise Build & Test, puis vérifie le statut OSC /avatar/avatarfullfacetracking et /avatar/parameters/UserFaceTracking.");
                return "ZYUMIE AVATAR SUITE — FEO v0.4.0\n\n" + string.Join("\n", report);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return "ERREUR FEO : " + ex.GetBaseException().Message + "\nAucune publication n'a été lancée. Vérifie le Descriptor et les assets générés dans Unity avant de relancer. Consulte la Console.";
            }
        }

        private static void ConfigureLegacyEyeLook(VRCAvatarDescriptor descriptor, List<Transform> eyes)
        {
            if (!descriptor || eyes == null || eyes.Count != 2 || !eyes[0] || !eyes[1])
                throw new InvalidOperationException("Les deux références d'yeux sont nécessaires pour configurer Eye Look.");

            var serialized = new SerializedObject(descriptor);
            serialized.Update();
            SerializedProperty enabled = serialized.FindProperty("enableEyeLook");
            SerializedProperty leftEye = serialized.FindProperty("customEyeLookSettings.leftEye");
            SerializedProperty rightEye = serialized.FindProperty("customEyeLookSettings.rightEye");
            if (enabled == null || leftEye == null || rightEye == null)
                throw new InvalidOperationException("Les champs Eye Look du VRC Avatar Descriptor sont absents. Vérifie la version du SDK Avatars.");

            enabled.boolValue = true;
            if (!leftEye.objectReferenceValue) leftEye.objectReferenceValue = eyes[0];
            if (!rightEye.objectReferenceValue) rightEye.objectReferenceValue = eyes[1];

            // BoneRotation is independent from native Face Tracking BlendShapes and
            // is the correct fallback for legacy /tracking/eye/* input.
            SerializedProperty gazeMode = serialized.FindProperty("customEyeLookSettings.eyeGazeMode");
            if (gazeMode != null) SetEnumValue(gazeMode, "BoneRotation");

            Undo.RecordObject(descriptor, "Zyumie Configure Bone Eye Look");
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(descriptor);
        }

        private static FaceTrackingResult ConfigureNativeFaceTracking(VRCAvatarDescriptor descriptor, GameObject root, List<Transform> eyes)
        {
            var result = new FaceTrackingResult();
            var serialized = new SerializedObject(descriptor);
            serialized.Update();

            SerializedProperty enabled = serialized.FindProperty("enableFaceTracking");
            SerializedProperty useAll = serialized.FindProperty("useAllTrackingKeys");
            SerializedProperty meshList = serialized.FindProperty("faceTrackingMeshes");
            SerializedProperty sets = serialized.FindProperty("faceTrackingBlendshapesSetList");
            if (enabled == null || useAll == null || meshList == null || sets == null)
                throw new InvalidOperationException("Les champs Face Tracking natifs sont absents. Installe VRChat Avatars SDK 3.10.6 ou une version plus récente.");
            if (!meshList.isArray || !sets.isArray)
                throw new InvalidOperationException("La structure Face Tracking du SDK n'a pas le format attendu. Aucun mapping n'a été écrit.");

            var allMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s && s.sharedMesh && s.sharedMesh.blendShapeCount > 0).ToList();
            Dictionary<string, string> keyToShape = BuildShapeMappings(allMeshes);
            result.mappedKeys = 0;
            result.mappedEyeLook = 0;
            result.leftBlink = false;
            result.rightBlink = false;

            var existingMeshes = new List<SkinnedMeshRenderer>();
            for (int i = 0; i < meshList.arraySize; i++)
            {
                var current = meshList.GetArrayElementAtIndex(i).objectReferenceValue as SkinnedMeshRenderer;
                if (current && !existingMeshes.Contains(current)) existingMeshes.Add(current);
            }
            if (existingMeshes.Count > MaxFaceTrackingMeshes)
                throw new InvalidOperationException("Le Descriptor contient déjà plus de 16 meshes Face Tracking. Réduis cette liste dans le Descriptor avant de relancer FEO.");

            var previouslyMappedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int setIndex = 0; setIndex < sets.arraySize; setIndex++)
            {
                var oldSet = sets.GetArrayElementAtIndex(setIndex);
                var oldNames = oldSet.FindPropertyRelative("faceTrackingBlendshapeNames");
                if (oldNames == null || !oldNames.isArray) continue;
                for (int keyIndex = 0; keyIndex < oldNames.arraySize; keyIndex++)
                {
                    string oldName = oldNames.GetArrayElementAtIndex(keyIndex).stringValue;
                    if (!string.IsNullOrEmpty(oldName)) previouslyMappedNames.Add(oldName);
                }
            }
            var allMappedNames = keyToShape.Values.Concat(previouslyMappedNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var usefulMeshes = allMeshes
                .Where(s => allMappedNames.Any(shape => HasBlendShape(s, shape)))
                .OrderByDescending(s => CountMappedShapes(s, allMappedNames))
                .ToList();
            var finalMeshes = new List<SkinnedMeshRenderer>(existingMeshes);
            foreach (var mesh in usefulMeshes)
            {
                if (finalMeshes.Count >= MaxFaceTrackingMeshes) break;
                if (!finalMeshes.Contains(mesh)) finalMeshes.Add(mesh);
            }
            // Native Face Tracking can only drive actual BlendShapes. Do not abort the
            // whole FEO setup when this avatar has no Unified Expressions-compatible
            // names: legacy Eye Look (bone rotation / eye OSC) remains a valid path.
            bool hasAnyMappedShape = allMappedNames.Any(shape =>
                allMeshes.Any(mesh => HasBlendShape(mesh, shape)));
            if (finalMeshes.Count == 0 || !hasAnyMappedShape)
            {
                result.nativeConfigured = false;
                result.warnings.Add("aucun BlendShape ne correspond aux noms Unified Expressions reconnus ou aux mappings déjà présents.");
                result.warnings.Add("Le Face Tracking natif n'a pas été activé. Le setup continue avec Eye Look par rotation des os (si le casque ou FrameEyeOSC fournit /tracking/eye/*). La bouche et les expressions faciales ne seront pas animées par /tracking/face/v1 tant que les BlendShapes ne seront pas mappés.");

                var candidates = allMeshes
                    .SelectMany(mesh => Enumerable.Range(0, mesh.sharedMesh.blendShapeCount)
                        .Select(index => mesh.name + ": " + mesh.sharedMesh.GetBlendShapeName(index)))
                    .Distinct().Take(30).ToArray();
                if (candidates.Length > 0)
                    result.warnings.Add("BlendShapes présents (30 premiers) : " + string.Join(" | ", candidates));
                else
                    result.warnings.Add("Aucun BlendShape n'a été trouvé sur les SkinnedMeshRenderer de cet avatar.");
                return result;
            }
            if (usefulMeshes.Count > MaxFaceTrackingMeshes)
                result.warnings.Add("plus de 16 meshes semblent contenir des BlendShapes compatibles. Les " + MaxFaceTrackingMeshes + " premiers (en privilégiant ceux ayant le plus de correspondances) ont été retenus.");

            bool creatingFirstSet = sets.arraySize == 0;
            if (creatingFirstSet) sets.arraySize = 1;
            SerializedProperty set = sets.GetArrayElementAtIndex(0);
            SerializedProperty setName = set.FindPropertyRelative("setName");
            SerializedProperty setWeight = set.FindPropertyRelative("setWeight");
            SerializedProperty names = set.FindPropertyRelative("faceTrackingBlendshapeNames");
            SerializedProperty multipliers = set.FindPropertyRelative("faceTrackingBlendshapeMultipliers");
            if (names == null || multipliers == null || !names.isArray || !multipliers.isArray)
                throw new InvalidOperationException("Impossible d'accéder aux tableaux de mappings de la Blendshapes Set du SDK.");

            if (setName != null && string.IsNullOrEmpty(setName.stringValue)) setName.stringValue = "Zyumie_FEO_Auto";
            if (setWeight != null && creatingFirstSet) setWeight.floatValue = 1f;
            names.arraySize = TrackingKeys.Length;
            multipliers.arraySize = TrackingKeys.Length;
            for (int i = 0; i < TrackingKeys.Length; i++)
            {
                SerializedProperty nameEntry = names.GetArrayElementAtIndex(i);
                SerializedProperty multiplierEntry = multipliers.GetArrayElementAtIndex(i);
                if (multiplierEntry.floatValue == 0f) multiplierEntry.floatValue = 1f;
                if (string.IsNullOrEmpty(nameEntry.stringValue) && keyToShape.TryGetValue(TrackingKeys[i], out string shape))
                    nameEntry.stringValue = shape;
                if (!string.IsNullOrEmpty(nameEntry.stringValue))
                {
                    result.mappedKeys++;
                    if (i <= 7) result.mappedEyeLook++;
                    if (i == 8) result.rightBlink = true;
                    if (i == 9) result.leftBlink = true;
                }
            }
            result.meshes = finalMeshes.Count;

            meshList.arraySize = finalMeshes.Count;
            for (int i = 0; i < finalMeshes.Count; i++)
                meshList.GetArrayElementAtIndex(i).objectReferenceValue = finalMeshes[i];

            enabled.boolValue = true;
            useAll.boolValue = true;

            SerializedProperty eyeLookEnabled = serialized.FindProperty("enableEyeLook");
            SerializedProperty leftEye = serialized.FindProperty("customEyeLookSettings.leftEye");
            SerializedProperty rightEye = serialized.FindProperty("customEyeLookSettings.rightEye");
            if (eyeLookEnabled != null) eyeLookEnabled.boolValue = true;
            if (leftEye != null && leftEye.objectReferenceValue == null) leftEye.objectReferenceValue = eyes[0];
            if (rightEye != null && rightEye.objectReferenceValue == null) rightEye.objectReferenceValue = eyes[1];

            SerializedProperty eyeGazeMode = serialized.FindProperty("customEyeLookSettings.eyeGazeMode");
            if (eyeGazeMode != null && result.mappedEyeLook > 0) SetEnumValue(eyeGazeMode, "Both");
            SerializedProperty eyelidType = serialized.FindProperty("customEyeLookSettings.eyelidType");
            if (eyelidType != null && result.leftBlink && result.rightBlink) SetEnumValue(eyelidType, "FaceTracking");

            Undo.RecordObject(descriptor, "Zyumie Configure Native Face Tracking");
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(descriptor);
            result.nativeConfigured = true;
            if (result.mappedEyeLook == 0)
                result.warnings.Add("aucun BlendShape EyeLook* reconnu. Les yeux peuvent encore utiliser les os Eye Look, mais le flux /tracking/face/v1 ne pourra pas déplacer visuellement le regard sans mapping d'yeux compatible.");
            if (!result.leftBlink || !result.rightBlink)
                result.warnings.Add("les deux BlendShapes de paupières (EyeClosedLeft / EyeClosedRight ou alias) n'ont pas été trouvés. Le clignement doit être mappé manuellement dans le Descriptor.");
            return result;
        }

        private static Dictionary<string, string> BuildShapeMappings(List<SkinnedMeshRenderer> meshes)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in TrackingKeys)
            {
                string[] candidates = ShapeAliases.ContainsKey(key)
                    ? new[] { key }.Concat(ShapeAliases[key]).ToArray()
                    : new[] { key };
                foreach (string candidate in candidates)
                {
                    string normalizedCandidate = Normalize(candidate);
                    string found = meshes.SelectMany(s => Enumerable.Range(0, s.sharedMesh.blendShapeCount)
                            .Select(i => s.sharedMesh.GetBlendShapeName(i)))
                        .FirstOrDefault(name => Normalize(name) == normalizedCandidate);
                    if (!string.IsNullOrEmpty(found))
                    {
                        result[key] = found;
                        break;
                    }
                }
            }
            return result;
        }

        private static int CountMappedShapes(SkinnedMeshRenderer renderer, IEnumerable<string> names)
        {
            if (!renderer || !renderer.sharedMesh) return 0;
            var existing = new HashSet<string>(Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                .Select(i => Normalize(renderer.sharedMesh.GetBlendShapeName(i))));
            return names.Count(name => existing.Contains(Normalize(name)));
        }

        private static bool HasBlendShape(SkinnedMeshRenderer renderer, string shape)
        {
            if (!renderer || !renderer.sharedMesh) return false;
            string target = Normalize(shape);
            for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                if (Normalize(renderer.sharedMesh.GetBlendShapeName(i)) == target) return true;
            return false;
        }

        private static string Normalize(string value)
        {
            return new string((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static bool SetEnumValue(SerializedProperty property, string value)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum) return false;
            int index = Array.FindIndex(property.enumNames, name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            property.enumValueIndex = index;
            return true;
        }

        private static List<int> FindGestureDrivenFacialLayers(AnimatorController controller)
        {
            var result = new List<int>();
            if (!controller) return result;
            var layers = controller.layers;
            for (int i = 1; i < layers.Length; i++)
            {
                if (layers[i].name == GestureFaceLayerName || layers[i].name == EyeLayerName) continue;
                bool hasGestureCondition = HasGestureCondition(layers[i].stateMachine);
                bool hasFaceBlendshapeMotion = HasFacialMotion(layers[i].stateMachine);
                if (hasGestureCondition && hasFaceBlendshapeMotion) result.Add(i);
            }
            return result;
        }

        private static bool HasGestureCondition(AnimatorStateMachine machine)
        {
            if (!machine) return false;
            foreach (var transition in machine.anyStateTransitions)
                if (TransitionHasGestureCondition(transition)) return true;
            foreach (var transition in machine.entryTransitions)
                if (TransitionHasGestureCondition(transition)) return true;
            foreach (var child in machine.states)
                foreach (var transition in child.state.transitions)
                    if (TransitionHasGestureCondition(transition)) return true;
            foreach (var childMachine in machine.stateMachines)
                if (HasGestureCondition(childMachine.stateMachine)) return true;
            return false;
        }

        private static bool TransitionHasGestureCondition(AnimatorTransitionBase transition)
        {
            if (transition == null) return false;
            return transition.conditions.Any(c =>
                c.parameter == "GestureLeft" || c.parameter == "GestureRight" ||
                c.parameter == "GestureLeftWeight" || c.parameter == "GestureRightWeight");
        }

        private static bool HasFacialMotion(AnimatorStateMachine machine)
        {
            if (!machine) return false;
            foreach (var child in machine.states)
                if (MotionHasFaceBlendshape(child.state.motion, new HashSet<Motion>())) return true;
            foreach (var childMachine in machine.stateMachines)
                if (HasFacialMotion(childMachine.stateMachine)) return true;
            return false;
        }

        private static bool MotionHasFaceBlendshape(Motion motion, HashSet<Motion> visited)
        {
            if (!motion || !visited.Add(motion)) return false;
            var clip = motion as AnimationClip;
            if (clip)
                return AnimationUtility.GetCurveBindings(clip).Any(binding =>
                    binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.OrdinalIgnoreCase));
            var tree = motion as BlendTree;
            return tree && tree.children.Any(child => MotionHasFaceBlendshape(child.motion, visited));
        }

        private static void AddEyeTrackingToggleLayer(AnimatorController controller, Type trackingType)
        {
            controller.AddLayer(EyeLayerName);
            var layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[index].defaultWeight = 1f;
            controller.layers = layers;
            var machine = controller.layers[index].stateMachine;
            var enabled = machine.AddState("Eyes_Tracking_On");
            var disabled = machine.AddState("Eyes_Tracking_Off");
            enabled.writeDefaultValues = false;
            disabled.writeDefaultValues = false;
            machine.defaultState = enabled;
            AddTrackingBehaviour(enabled, trackingType, "Tracking");
            AddTrackingBehaviour(disabled, trackingType, "Animation");
            ConfigureTransition(enabled.AddTransition(disabled), EyesParameter, false);
            ConfigureTransition(disabled.AddTransition(enabled), EyesParameter, true);
        }

        private static void AddFacialGestureToggleLayer(AnimatorController controller, Type layerControlType, List<int> targetLayers)
        {
            controller.AddLayer(GestureFaceLayerName);
            var layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[index].defaultWeight = 1f;
            controller.layers = layers;
            var machine = controller.layers[index].stateMachine;
            var enabled = machine.AddState("Facial_Gestures_On");
            var disabled = machine.AddState("Facial_Gestures_Off");
            enabled.writeDefaultValues = false;
            disabled.writeDefaultValues = false;
            machine.defaultState = enabled;
            foreach (int target in targetLayers)
            {
                AddLayerControlBehaviour(enabled, layerControlType, target, 1f, "Zyumie FEO MainGestures ON");
                AddLayerControlBehaviour(disabled, layerControlType, target, 0f, "Zyumie FEO MainGestures OFF");
            }
            ConfigureTransition(enabled.AddTransition(disabled), GesturesParameter, false);
            ConfigureTransition(disabled.AddTransition(enabled), GesturesParameter, true);
        }

        private static void AddTrackingBehaviour(AnimatorState state, Type type, string value)
        {
            var behaviour = state.AddStateMachineBehaviour(type);
            if (!behaviour) throw new InvalidOperationException("Impossible d'ajouter VRCAnimatorTrackingControl.");
            var serialized = new SerializedObject(behaviour);
            serialized.Update();
            var property = serialized.FindProperty("trackingEyes");
            if (!SetEnumValue(property, value))
                throw new InvalidOperationException("Le champ trackingEyes ou la valeur " + value + " est absente du SDK.");
            serialized.ApplyModifiedProperties();
        }

        private static void AddLayerControlBehaviour(AnimatorState state, Type type, int targetLayer, float weight, string debug)
        {
            var behaviour = state.AddStateMachineBehaviour(type);
            if (!behaviour) throw new InvalidOperationException("Impossible d'ajouter VRCAnimatorLayerControl.");
            var serialized = new SerializedObject(behaviour);
            serialized.Update();
            var playable = serialized.FindProperty("playable");
            var layer = serialized.FindProperty("layer");
            var goalWeight = serialized.FindProperty("goalWeight");
            var blendDuration = serialized.FindProperty("blendDuration");
            if (!SetEnumValue(playable, "FX") || layer == null || goalWeight == null || blendDuration == null)
                throw new InvalidOperationException("Les champs de VRCAnimatorLayerControl ne correspondent pas au SDK installé.");
            layer.intValue = targetLayer;
            goalWeight.floatValue = weight;
            blendDuration.floatValue = 0f;
            var debugString = serialized.FindProperty("debugString");
            if (debugString != null) debugString.stringValue = debug;
            serialized.ApplyModifiedProperties();
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, string parameter, bool toEnabled)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.02f;
            transition.offset = 0f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(toEnabled ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static VRCExpressionsMenu BuildExpressionMenu(VRCExpressionsMenu original, string folder, bool includeGestures, List<string> report)
        {
            var root = original
                ? CloneMenuTree(original, folder, new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>())
                : CreateMenu("Zyumie_Expressions_Root", folder);
            if (root.controls == null) throw new InvalidOperationException("Le menu Expressions racine ne contient pas de liste Controls initialisée.");

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
            if (function.controls == null) throw new InvalidOperationException("Le menu Function ne contient pas de liste Controls initialisée.");

            var feo = CreateMenu(FEOName, folder);
            feo.controls.Add(ToggleControl("EyesTracking", EyesParameter));
            if (includeGestures) feo.controls.Add(ToggleControl("MainGestures", GesturesParameter));
            int index = function.controls.FindIndex(c => string.Equals(c.name, FEOName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var existing = function.controls[index];
                existing.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
                existing.subMenu = feo;
                function.controls[index] = existing;
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
            if (!menu || !visited.Add(menu) || menu.controls == null) return null;
            if (string.Equals(menu.name, name, StringComparison.OrdinalIgnoreCase)) return menu;
            foreach (var control in menu.controls)
            {
                if (string.Equals(control.name, name, StringComparison.OrdinalIgnoreCase) && control.subMenu) return control.subMenu;
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
            copy.name = source.name + "_Zyumie040";
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

        private static VRCExpressionParameters BuildExpressionParameters(VRCExpressionParameters original, string folder, bool includeGestures, List<string> report)
        {
            var copy = original ? UnityEngine.Object.Instantiate(original) : ScriptableObject.CreateInstance<VRCExpressionParameters>();
            copy.name = "Zyumie_FEO_ExpressionParameters";
            var list = copy.parameters != null ? copy.parameters.ToList() : new List<VRCExpressionParameters.Parameter>();
            UpsertBool(list, EyesParameter);
            if (includeGestures) UpsertBool(list, GesturesParameter);
            int syncedCost = list.Where(p => p.networkSynced)
                .Sum(p => p.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8);
            report.Add("Expression Parameters : coût synchronisé " + syncedCost + "/256 bits.");
            if (syncedCost > 256)
                throw new InvalidOperationException("Budget Expression Parameters dépassé : " + syncedCost + "/256 bits. Réduis ou désynchronise des paramètres avant de relancer FEO.");
            copy.parameters = list.ToArray();
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/Zyumie_FEO_ExpressionParameters.asset"));
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

        private static AnimatorController ClonePlayableController(VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType layerType, string folder, string defaultSampleName, List<string> report)
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
                    string.Equals(type.enumNames[type.enumValueIndex], layerType.ToString(), StringComparison.OrdinalIgnoreCase))
                { layerElement = element; break; }
            }
            if (layerElement == null) throw new InvalidOperationException("Slot " + layerType + " introuvable.");
            var controllerProperty = layerElement.FindPropertyRelative("animatorController");
            if (controllerProperty == null) throw new InvalidOperationException("Référence Controller inaccessible pour " + layerType + ".");
            var current = controllerProperty.objectReferenceValue as RuntimeAnimatorController;
            if (current && !(current is AnimatorController))
                throw new InvalidOperationException("Le contrôleur " + layerType + " est un AnimatorOverrideController. Conversion manuelle nécessaire pour préserver les overrides.");
            string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/Zyumie_FEO_" + layerType + ".controller");
            AnimatorController result;
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
                    throw new InvalidOperationException("Sample " + defaultSampleName + " introuvable. Importe les AV3 Demo Assets du SDK VRChat.");
                result = AssetDatabase.LoadAssetAtPath<AnimatorController>(destination);
                report.Add("Controller " + layerType + " par défaut du SDK dupliqué.");
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
                    !string.Equals(type.enumNames[type.enumValueIndex], layerType.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                var controllerProperty = element.FindPropertyRelative("animatorController");
                var defaultProperty = element.FindPropertyRelative("isDefault");
                if (controllerProperty == null) throw new InvalidOperationException("Référence controller " + layerType + " inaccessible.");
                controllerProperty.objectReferenceValue = controller;
                if (defaultProperty != null) defaultProperty.boolValue = false;
                var customizeLayers = serialized.FindProperty("customizeAnimationLayers");
                if (customizeLayers != null && customizeLayers.propertyType == SerializedPropertyType.Boolean) customizeLayers.boolValue = true;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(descriptor);
                return;
            }
            throw new InvalidOperationException("Slot " + layerType + " non trouvé lors de l'assignation.");
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name, bool defaultValue)
        {
            var parameters = controller.parameters;
            int index = Array.FindIndex(parameters, p => p.name == name);
            if (index >= 0)
            {
                if (parameters[index].type != AnimatorControllerParameterType.Bool)
                    throw new InvalidOperationException("Le paramètre Animator « " + name + " » existe mais n'est pas Bool.");
                parameters[index].defaultBool = defaultValue;
                controller.parameters = parameters;
            }
            else controller.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Bool, defaultBool = defaultValue });
        }

        private static void RemoveLayerIfPresent(AnimatorController controller, string name)
        {
            for (int i = controller.layers.Length - 1; i >= 0; i--)
                if (controller.layers[i].name == name) controller.RemoveLayer(i);
        }

        private static Type FindBehaviourType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                Type found = types.FirstOrDefault(t => t != null && t.Name == typeName &&
                    typeof(StateMachineBehaviour).IsAssignableFrom(t) && !t.IsAbstract);
                if (found != null) return found;
            }
            return null;
        }

        private static List<Transform> FindEyeBones(GameObject root, VRCAvatarDescriptor descriptor)
        {
            var so = new SerializedObject(descriptor);
            so.Update();
            Transform left = so.FindProperty("customEyeLookSettings.leftEye")?.objectReferenceValue as Transform;
            Transform right = so.FindProperty("customEyeLookSettings.rightEye")?.objectReferenceValue as Transform;
            var all = root.GetComponentsInChildren<Transform>(true);
            if (!left) left = all.FirstOrDefault(t => Eq(t.name, "Eye.L") || Eq(t.name, "LeftEye") || Eq(t.name, "Eye_Left") || Eq(t.name, "eye_l"));
            if (!right) right = all.FirstOrDefault(t => Eq(t.name, "Eye.R") || Eq(t.name, "RightEye") || Eq(t.name, "Eye_Right") || Eq(t.name, "eye_r"));
            var result = new List<Transform>();
            if (left && left != root.transform && left.IsChildOf(root.transform)) result.Add(left);
            if (right && right != root.transform && right.IsChildOf(root.transform) && right != left) result.Add(right);
            return result;
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
