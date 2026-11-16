using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Reads clips referenced by the avatar's Animator components and VRChat playable layers.
    /// Used to prevent merging renderers whose separate transforms or renderer component are animated.
    /// </summary>
    internal sealed class AvatarAnimationBindingAudit
    {
        private readonly GameObject root;
        private readonly List<AnimationClip> clips = new List<AnimationClip>();
        private readonly Dictionary<AnimationClip, EditorCurveBinding[]> curveBindings = new Dictionary<AnimationClip, EditorCurveBinding[]>();
        private readonly Dictionary<AnimationClip, EditorCurveBinding[]> objectBindings = new Dictionary<AnimationClip, EditorCurveBinding[]>();

        public AvatarAnimationBindingAudit(GameObject avatarRoot)
        {
            root = avatarRoot;
            if (!root) return;
            var found = new HashSet<AnimationClip>();

            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                AddController(animator ? animator.runtimeAnimatorController : null, found);

            foreach (var descriptor in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
            {
                var serialized = new SerializedObject(descriptor);
                serialized.Update();
                AddLayerControllers(serialized.FindProperty("baseAnimationLayers"), found);
                AddLayerControllers(serialized.FindProperty("specialAnimationLayers"), found);
            }

            foreach (var legacy in root.GetComponentsInChildren<Animation>(true))
            {
                if (!legacy) continue;
                foreach (AnimationState state in legacy)
                    if (state != null && state.clip) found.Add(state.clip);
            }

            clips.AddRange(found);
        }

        private static void AddLayerControllers(SerializedProperty layers, HashSet<AnimationClip> found)
        {
            if (layers == null || !layers.isArray) return;
            for (int i = 0; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                var controller = element.FindPropertyRelative("animatorController")?.objectReferenceValue as RuntimeAnimatorController;
                AddController(controller, found);
            }
        }

        private static void AddController(RuntimeAnimatorController controller, HashSet<AnimationClip> found)
        {
            if (!controller) return;
            foreach (var clip in controller.animationClips)
                if (clip) found.Add(clip);
        }

        public bool HasUnsafeBindings(SkinnedMeshRenderer a, SkinnedMeshRenderer b, out string reason)
        {
            reason = null;
            if (!root || !a || !b) return false;
            string pathA = AnimationUtility.CalculateTransformPath(a.transform, root.transform);
            string pathB = AnimationUtility.CalculateTransformPath(b.transform, root.transform);
            if (HasRendererBinding(pathA) || HasRendererBinding(pathB))
            {
                reason = "un clip anime directement un SkinnedMeshRenderer de ce groupe.";
                return true;
            }

            Transform common = FindLowestCommonAncestor(a.transform, b.transform);
            if (HasAnimatedBranch(a.transform, common) || HasAnimatedBranch(b.transform, common))
            {
                reason = "un clip anime un transform propre à l'un des meshes ; leur fusion ferait perdre ce mouvement relatif.";
                return true;
            }
            return false;
        }

        private bool HasRendererBinding(string path)
        {
            foreach (var clip in clips)
            {
                foreach (var binding in GetCurveBindings(clip))
                {
                    if (binding.path != path || binding.type == null) continue;
                    if (typeof(Renderer).IsAssignableFrom(binding.type) || binding.type == typeof(GameObject)) return true;
                }
                foreach (var binding in GetObjectBindings(clip))
                    if (binding.path == path) return true;
            }
            return false;
        }

        private bool HasAnimatedBranch(Transform branch, Transform common)
        {
            for (Transform current = branch; current && current != common; current = current.parent)
            {
                string path = AnimationUtility.CalculateTransformPath(current, root.transform);
                foreach (var clip in clips)
                    foreach (var binding in GetCurveBindings(clip))
                        if (binding.path == path && (binding.type == typeof(Transform) || binding.type == typeof(GameObject))) return true;
            }
            return false;
        }

        private EditorCurveBinding[] GetCurveBindings(AnimationClip clip)
        {
            EditorCurveBinding[] result;
            if (!curveBindings.TryGetValue(clip, out result))
            {
                result = AnimationUtility.GetCurveBindings(clip);
                curveBindings.Add(clip, result);
            }
            return result;
        }

        private EditorCurveBinding[] GetObjectBindings(AnimationClip clip)
        {
            EditorCurveBinding[] result;
            if (!objectBindings.TryGetValue(clip, out result))
            {
                result = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                objectBindings.Add(clip, result);
            }
            return result;
        }

        private static Transform FindLowestCommonAncestor(Transform a, Transform b)
        {
            var ancestors = new HashSet<Transform>();
            for (Transform current = a; current; current = current.parent) ancestors.Add(current);
            for (Transform current = b; current; current = current.parent)
                if (ancestors.Contains(current)) return current;
            return null;
        }
    }
}
