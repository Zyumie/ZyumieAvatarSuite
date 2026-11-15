using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Temporary Editor-only preview. Rotates descriptor eye transforms and drives one
    /// explicitly selected blink blendshape. It does not emulate VRChat OSC/runtime tracking.
    /// </summary>
    public sealed class FEOMouseEyeSimulator : EditorWindow
    {
        private GameObject avatarRoot;
        private Transform leftEye;
        private Transform rightEye;
        private Vector2 gaze;
        private float maxYaw = 25f;
        private float maxPitch = 18f;
        private float blinkWeight;
        private bool isSimulating;
        private Rect gazeRect;
        private int gazeControlId;
        private int selectedBlinkIndex = -1;
        private readonly List<BlendshapeTarget> blinkTargets = new List<BlendshapeTarget>();
        private readonly Dictionary<Transform, Quaternion> originalRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<BlendshapeTarget, float> originalWeights = new Dictionary<BlendshapeTarget, float>();

        private sealed class BlendshapeTarget
        {
            public SkinnedMeshRenderer renderer;
            public int index;
            public string name;
            public string Label => renderer.name + " / " + name + " [" + index + "]";
        }

        [MenuItem("Zyumie/Tools/FEO Desktop Eye Simulator")]
        public static void Open() { Open(Selection.activeGameObject); }

        public static void Open(GameObject target)
        {
            var window = GetWindow<FEOMouseEyeSimulator>("FEO Eye Simulator");
            window.minSize = new Vector2(390, 540);
            if (target) window.SetAvatar(target);
            window.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            EditorApplication.update += EditorUpdate;
            if (!avatarRoot && Selection.activeGameObject) SetAvatar(Selection.activeGameObject);
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            RestorePreview();
        }

        private void OnSelectionChange()
        {
            if (!isSimulating && Selection.activeGameObject)
            {
                avatarRoot = Selection.activeGameObject;
                ResolveAvatar();
                Repaint();
            }
        }

        private void EditorUpdate()
        {
            if (!isSimulating) return;
            // Re-apply while previewing so the values remain visible if an editor preview
            // or another editor callback updates the avatar between GUI events.
            ApplyGaze();
            ApplyBlink();
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField("FEO — DESKTOP EYE SIMULATOR", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Simulation locale Unity • ne reproduit pas le flux OSC VRChat", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);

            EditorGUI.BeginChangeCheck();
            avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatarRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && !isSimulating) ResolveAvatar();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField("Œil gauche", leftEye, typeof(Transform), true);
                EditorGUILayout.ObjectField("Œil droit", rightEye, typeof(Transform), true);
            }

            if (!leftEye || !rightEye)
            {
                EditorGUILayout.HelpBox("Les deux références d'yeux sont nécessaires. Vérifie Eye Look > Left Eye / Right Eye dans le Descriptor.", MessageType.Warning);
                if (GUILayout.Button("Rechercher les références")) ResolveAvatar();
                return;
            }

            EditorGUILayout.Space(5);
            maxYaw = EditorGUILayout.Slider("Amplitude horizontale", maxYaw, 1f, 45f);
            maxPitch = EditorGUILayout.Slider("Amplitude verticale", maxPitch, 1f, 35f);

            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField("Zone de contrôle — déplace ou glisse la souris", EditorStyles.boldLabel);
            gazeRect = GUILayoutUtility.GetRect(100, 190, GUILayout.ExpandWidth(true));
            DrawGazePad(gazeRect, Event.current);
            EditorGUILayout.LabelField($"Horizontal : {gaze.x:0.00}    Vertical : {gaze.y:0.00}", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regard neutre")) { gaze = Vector2.zero; ApplyGaze(); }
                if (GUILayout.Button("Cligner"))
                {
                    blinkWeight = 100f;
                    ApplyBlink();
                    Repaint();
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("BlendShape de clignement", EditorStyles.boldLabel);
            if (blinkTargets.Count == 0)
            {
                EditorGUILayout.HelpBox("Aucun nom de BlendShape évocateur détecté. Le clignement manuel ne peut pas fonctionner sans sélectionner/configurer une forme compatible.", MessageType.Warning);
            }
            else
            {
                string[] labels = blinkTargets.Select(t => t.Label).ToArray();
                selectedBlinkIndex = Mathf.Clamp(selectedBlinkIndex, 0, blinkTargets.Count - 1);
                using (new EditorGUI.DisabledScope(isSimulating))
                    selectedBlinkIndex = EditorGUILayout.Popup("BlendShape ciblé", selectedBlinkIndex, labels);
                blinkWeight = EditorGUILayout.Slider("Fermeture", blinkWeight, 0f, 100f);
                if (isSimulating) ApplyBlink();
                EditorGUILayout.HelpBox("Une seule BlendShape est pilotée à la fois. Si ton avatar utilise deux formes (gauche/droite), teste-les séparément pour identifier le bon mapping.", MessageType.Info);
            }

            EditorGUILayout.Space(7);
            GUI.backgroundColor = isSimulating ? new Color(0.88f, 0.55f, 0.55f) : new Color(0.55f, 0.85f, 0.65f);
            if (GUILayout.Button(isSimulating ? "ARRÊTER ET RESTAURER" : "DÉMARRER LA SIMULATION", GUILayout.Height(34)))
            {
                if (isSimulating) RestorePreview();
                else StartPreview();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.HelpBox("Le simulateur modifie temporairement les Transform des yeux et la BlendShape sélectionnée. Il restaure les valeurs capturées à l'arrêt/fermeture. Si les yeux ne bougent pas ici, vérifie que les références du Descriptor sont les os qui pilotent réellement les globes oculaires. Ce test ne valide pas le tracking runtime ni OSC.", MessageType.None);
        }

        private void DrawGazePad(Rect rect, Event evt)
        {
            gazeControlId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.14f, 0.18f));
            Handles.color = new Color(1f, 1f, 1f, 0.2f);
            Handles.DrawLine(new Vector3(rect.center.x, rect.y), new Vector3(rect.center.x, rect.yMax));
            Handles.DrawLine(new Vector3(rect.x, rect.center.y), new Vector3(rect.xMax, rect.center.y));
            Vector2 dot = new Vector2(rect.center.x + gaze.x * rect.width * 0.42f, rect.center.y - gaze.y * rect.height * 0.42f);
            Handles.color = Color.cyan;
            Handles.DrawSolidDisc(dot, Vector3.forward, 6f);
            GUI.Label(new Rect(rect.x + 6, rect.y + 4, 110, 18), "HAUT");
            GUI.Label(new Rect(rect.x + 6, rect.yMax - 22, 110, 18), "BAS");

            if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) &&
                rect.Contains(evt.mousePosition) && (evt.button == 0 || evt.type == EventType.MouseMove))
            {
                if (evt.type == EventType.MouseDown) GUIUtility.hotControl = gazeControlId;
                UpdateGaze(evt.mousePosition, rect);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == gazeControlId)
            {
                UpdateGaze(evt.mousePosition, rect);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == gazeControlId)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }

        private void UpdateGaze(Vector2 mouse, Rect rect)
        {
            gaze.x = Mathf.Clamp((mouse.x - rect.center.x) / Mathf.Max(1f, rect.width * 0.42f), -1f, 1f);
            gaze.y = Mathf.Clamp((rect.center.y - mouse.y) / Mathf.Max(1f, rect.height * 0.42f), -1f, 1f);
            ApplyGaze();
            Repaint();
        }

        private void SetAvatar(GameObject target)
        {
            if (isSimulating) RestorePreview();
            avatarRoot = target;
            ResolveAvatar();
        }

        private void ResolveAvatar()
        {
            leftEye = null;
            rightEye = null;
            blinkTargets.Clear();
            selectedBlinkIndex = -1;
            if (!avatarRoot) return;

            var descriptor = avatarRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor)
            {
                leftEye = descriptor.customEyeLookSettings.leftEye;
                rightEye = descriptor.customEyeLookSettings.rightEye;
            }
            var all = avatarRoot.GetComponentsInChildren<Transform>(true);
            if (!leftEye) leftEye = FindByNames(all, "Eye.L", "LeftEye", "Eye_Left", "eye_l");
            if (!rightEye) rightEye = FindByNames(all, "Eye.R", "RightEye", "Eye_Right", "eye_r");

            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr || !smr.sharedMesh) continue;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string original = smr.sharedMesh.GetBlendShapeName(i);
                    string n = original.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
                    bool candidate = n.Contains("blink") || n.Contains("eyelid") || n.Contains("eyeclose") ||
                                     n.Contains("closeeye") || n.Contains("まばたき") || n.Contains("瞬き") ||
                                     n.Contains("目閉じ") || n.Contains("閉じ");
                    if (candidate)
                        blinkTargets.Add(new BlendshapeTarget { renderer = smr, index = i, name = original });
                }
            }
            if (blinkTargets.Count > 0) selectedBlinkIndex = 0;
        }

        private static Transform FindByNames(Transform[] all, params string[] names)
        {
            foreach (var t in all)
                foreach (var name in names)
                    if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        private void StartPreview()
        {
            if (!leftEye || !rightEye) return;
            originalRotations.Clear();
            originalWeights.Clear();
            originalRotations[leftEye] = leftEye.localRotation;
            originalRotations[rightEye] = rightEye.localRotation;
            if (selectedBlinkIndex >= 0 && selectedBlinkIndex < blinkTargets.Count)
            {
                var target = blinkTargets[selectedBlinkIndex];
                if (target.renderer) originalWeights[target] = target.renderer.GetBlendShapeWeight(target.index);
            }
            isSimulating = true;
            ApplyGaze();
            ApplyBlink();
        }

        private void ApplyGaze()
        {
            if (!isSimulating) return;
            ApplyEye(leftEye, maxPitch, maxYaw);
            ApplyEye(rightEye, maxPitch, maxYaw);
        }

        private void ApplyEye(Transform eye, float pitch, float yaw)
        {
            if (!eye || !originalRotations.TryGetValue(eye, out Quaternion original)) return;
            eye.localRotation = original * Quaternion.Euler(-gaze.y * pitch, gaze.x * yaw, 0f);
        }

        private void ApplyBlink()
        {
            if (!isSimulating || selectedBlinkIndex < 0 || selectedBlinkIndex >= blinkTargets.Count) return;
            var target = blinkTargets[selectedBlinkIndex];
            if (target.renderer) target.renderer.SetBlendShapeWeight(target.index, blinkWeight);
        }

        private void RestorePreview()
        {
            foreach (var pair in originalRotations)
                if (pair.Key) pair.Key.localRotation = pair.Value;
            foreach (var pair in originalWeights)
                if (pair.Key.renderer) pair.Key.renderer.SetBlendShapeWeight(pair.Key.index, pair.Value);
            originalRotations.Clear();
            originalWeights.Clear();
            isSimulating = false;
            gaze = Vector2.zero;
            blinkWeight = 0f;
            GUIUtility.hotControl = 0;
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
