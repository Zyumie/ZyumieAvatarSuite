using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Safe, copy-based optimizations for skin weights, per-renderer material slots,
    /// PhysBone reference lists and unused renderer bone indices. It never deletes
    /// hierarchy transforms because those may still be used by Animator, constraints,
    /// contacts, PhysBones or custom scripts.
    /// </summary>
    internal static class AvatarAdvancedOptimizer
    {
        private const string PhysBoneTypeName = "VRCPhysBone";
        private const string PhysBoneColliderTypeName = "VRCPhysBoneCollider";

        public static AvatarMeshOperationResult OptimizeSkinningAndBonesOnAvatarCopy(GameObject avatarCopy,
            int maxInfluencesPerVertex = 4, float minimumWeight = 0.005f)
        {
            if (!avatarCopy) throw new ArgumentNullException(nameof(avatarCopy));
            maxInfluencesPerVertex = Mathf.Clamp(maxInfluencesPerVertex, 1, 8);
            minimumWeight = Mathf.Clamp(minimumWeight, 0f, 0.1f);

            var result = new AvatarMeshOperationResult();
            string folder = AvatarMeshAssetUtility.EnsureAvatarFolder(avatarCopy.name, "Meshes_SkinningOptimized");
            result.outputFolder = folder;
            var renderers = avatarCopy.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r && r.sharedMesh).ToArray();

            try
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    SkinnedMeshRenderer renderer = renderers[i];
                    EditorUtility.DisplayProgressBar("Zyumie — optimisation Skinning/Bones",
                        renderer.name + " (" + (i + 1) + "/" + renderers.Length + ")",
                        renderers.Length == 0 ? 1f : (float)i / renderers.Length);

                    Mesh source = renderer.sharedMesh;
                    Mesh optimized;
                    Transform[] optimizedBones;
                    int beforeInfluences, afterInfluences;
                    int beforeBoneSlots, afterBoneSlots;
                    string reason;
                    if (!TryBuildOptimizedSkinningMesh(source, renderer.bones, maxInfluencesPerVertex, minimumWeight,
                        out optimized, out optimizedBones, out beforeInfluences, out afterInfluences,
                        out beforeBoneSlots, out afterBoneSlots, out reason))
                    {
                        result.skipped++;
                        result.notes.Add(renderer.name + " : " + reason);
                        continue;
                    }

                    try
                    {
                        string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + AvatarMeshAssetUtility.Sanitize(renderer.name) + "_Skinning.asset");
                        AssetDatabase.CreateAsset(optimized, assetPath);
                        Undo.RecordObject(renderer, "Zyumie Optimize Skinning and Bones");
                        renderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                        renderer.bones = optimizedBones;
                        EditorUtility.SetDirty(renderer);
                        result.processed++;
                        result.notes.Add(renderer.name + " : influences " + beforeInfluences + " → " + afterInfluences +
                            " ; slots de bones du renderer " + beforeBoneSlots + " → " + afterBoneSlots +
                            ". Les Transform du rig sont conservés.");
                    }
                    catch
                    {
                        if (optimized) UnityEngine.Object.DestroyImmediate(optimized);
                        throw;
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            result.notes.Add("Le skinning garde jusqu'à " + maxInfluencesPerVertex + " influences par sommet ; les poids sous " + (minimumWeight * 100f).ToString("0.##") + "% sont supprimés puis les poids restants sont renormalisés.");
            result.notes.Add("Les entrées de bones non pondérées sont retirées des tableaux SkinnedMeshRenderer.bones et Mesh.bindposes. Aucun GameObject/Transform du squelette n'est supprimé : les animations, contraintes, contacts et PhysBones restent référencés.");
            result.notes.Add("Cette opération optimise les références de skinning et le nombre d'influences ; elle ne réduit pas à elle seule le nombre de Transform dans la hiérarchie du rig.");
            if (result.processed == 0) result.notes.Add("Aucun mesh n'a nécessité ou autorisé une modification du skinning.");
            return result;
        }

        private static bool TryBuildOptimizedSkinningMesh(Mesh source, Transform[] rendererBones,
            int maxInfluences, float minimumWeight, out Mesh output, out Transform[] outputBones,
            out int beforeInfluences, out int afterInfluences, out int beforeBoneSlots,
            out int afterBoneSlots, out string reason)
        {
            output = null;
            outputBones = null;
            beforeInfluences = afterInfluences = beforeBoneSlots = afterBoneSlots = 0;
            reason = null;
            if (!source) { reason = "mesh absent."; return false; }
            if (!source.isReadable) { reason = "mesh non lisible : active Read/Write sur le FBX."; return false; }
            if (rendererBones == null || rendererBones.Length == 0) { reason = "aucun bone assigné au SkinnedMeshRenderer."; return false; }
            Matrix4x4[] bindposes = source.bindposes;
            if (bindposes == null || bindposes.Length != rendererBones.Length)
            { reason = "le nombre de bindposes ne correspond pas au tableau bones : mesh laissé intact."; return false; }
            if (rendererBones.Any(b => !b)) { reason = "au moins une référence de bone est manquante : mesh laissé intact."; return false; }

            using (NativeArray<byte> sourceCounts = source.GetBonesPerVertex())
            using (NativeArray<BoneWeight1> sourceWeights = source.GetAllBoneWeights())
            {
                if (sourceCounts.Length != source.vertexCount || sourceWeights.Length == 0)
                { reason = "données de poids multi-bones absentes ou incohérentes."; return false; }

                var perVertex = new List<BoneWeight1>[source.vertexCount];
                var usedBones = new bool[rendererBones.Length];
                int cursor = 0;
                beforeBoneSlots = rendererBones.Length;
                for (int vertex = 0; vertex < source.vertexCount; vertex++)
                {
                    int amount = sourceCounts[vertex];
                    var influences = new List<BoneWeight1>(amount);
                    for (int j = 0; j < amount; j++)
                    {
                        if (cursor >= sourceWeights.Length) { reason = "tableau de poids tronqué."; return false; }
                        BoneWeight1 influence = sourceWeights[cursor++];
                        if (influence.boneIndex < 0 || influence.boneIndex >= rendererBones.Length ||
                            influence.weight < 0f || float.IsNaN(influence.weight) || float.IsInfinity(influence.weight))
                        { reason = "un poids référence un bone invalide : mesh laissé intact."; return false; }
                        if (influence.weight > 0f) influences.Add(influence);
                    }

                    influences = influences.OrderByDescending(w => w.weight).ToList();
                    beforeInfluences += influences.Count;
                    var kept = influences.Where(w => w.weight >= minimumWeight).Take(maxInfluences).ToList();
                    if (kept.Count == 0 && influences.Count > 0) kept.Add(influences[0]);
                    float total = kept.Sum(w => w.weight);
                    if (total > 0.000001f)
                    {
                        for (int j = 0; j < kept.Count; j++)
                        {
                            BoneWeight1 weight = kept[j];
                            weight.weight /= total;
                            kept[j] = weight;
                            usedBones[weight.boneIndex] = true;
                        }
                    }
                    perVertex[vertex] = kept;
                    afterInfluences += kept.Count;
                }
                if (cursor != sourceWeights.Length) { reason = "la longueur des données de poids ne correspond pas aux vertices."; return false; }

                var oldToNew = new int[rendererBones.Length];
                for (int i = 0; i < oldToNew.Length; i++) oldToNew[i] = -1;
                var newBones = new List<Transform>();
                var newBindposes = new List<Matrix4x4>();
                for (int i = 0; i < usedBones.Length; i++)
                {
                    if (!usedBones[i]) continue;
                    oldToNew[i] = newBones.Count;
                    newBones.Add(rendererBones[i]);
                    newBindposes.Add(bindposes[i]);
                }
                afterBoneSlots = newBones.Count;
                if (newBones.Count == 0) { reason = "aucun bone valide ne porte de poids."; return false; }

                bool changed = afterBoneSlots < beforeBoneSlots || afterInfluences < beforeInfluences;
                if (!changed) { reason = "déjà optimisé : aucune influence inutile ni entrée de bone non pondérée détectée."; return false; }

                var outputCounts = new byte[perVertex.Length];
                var outputWeights = new List<BoneWeight1>(afterInfluences);
                for (int vertex = 0; vertex < perVertex.Length; vertex++)
                {
                    List<BoneWeight1> influences = perVertex[vertex];
                    outputCounts[vertex] = (byte)influences.Count;
                    foreach (BoneWeight1 influence in influences)
                    {
                        int mapped = oldToNew[influence.boneIndex];
                        if (mapped < 0) { reason = "remappage interne de bone impossible."; return false; }
                        outputWeights.Add(new BoneWeight1 { boneIndex = mapped, weight = influence.weight });
                    }
                }

                output = UnityEngine.Object.Instantiate(source);
                output.name = source.name + "_ZyumieSkinningOptimized";
                output.bindposes = newBindposes.ToArray();
                var nativeCounts = new NativeArray<byte>(outputCounts, Allocator.Temp);
                var nativeWeights = new NativeArray<BoneWeight1>(outputWeights.ToArray(), Allocator.Temp);
                try { output.SetBoneWeights(nativeCounts, nativeWeights); }
                catch (Exception ex)
                {
                    UnityEngine.Object.DestroyImmediate(output);
                    output = null;
                    reason = "Unity a refusé les nouveaux poids de skinning : " + ex.Message;
                    return false;
                }
                finally
                {
                    if (nativeCounts.IsCreated) nativeCounts.Dispose();
                    if (nativeWeights.IsCreated) nativeWeights.Dispose();
                }
                outputBones = newBones.ToArray();
                return true;
            }
        }

        public static AvatarMeshOperationResult MergeDuplicateMaterialSlotsOnAvatarCopy(GameObject avatarCopy)
        {
            if (!avatarCopy) throw new ArgumentNullException(nameof(avatarCopy));
            var result = new AvatarMeshOperationResult();
            string folder = AvatarMeshAssetUtility.EnsureAvatarFolder(avatarCopy.name, "Meshes_MaterialSlots");
            result.outputFolder = folder;
            Renderer[] renderers = avatarCopy.GetComponentsInChildren<Renderer>(true);
            int slotsBefore = 0;
            int slotsAfter = 0;
            int assetsCreated = 0;

            foreach (Renderer renderer in renderers)
            {
                if (!renderer) continue;
                Mesh source;
                Action<Mesh> assignMesh;
                if (renderer is SkinnedMeshRenderer smr)
                {
                    source = smr.sharedMesh;
                    assignMesh = mesh => smr.sharedMesh = mesh;
                }
                else if (renderer is MeshRenderer mr)
                {
                    MeshFilter filter = mr.GetComponent<MeshFilter>();
                    source = filter ? filter.sharedMesh : null;
                    assignMesh = mesh => { if (filter) filter.sharedMesh = mesh; };
                }
                else continue;

                if (!source) continue;
                Material[] materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                slotsBefore += materials.Length;
                if (!source.isReadable || source.subMeshCount != materials.Length || source.subMeshCount < 2)
                { slotsAfter += materials.Length; result.skipped++; continue; }
                if (renderer.HasPropertyBlock())
                { slotsAfter += materials.Length; result.skipped++; result.notes.Add(renderer.name + " : MaterialPropertyBlock détecté, slots conservés pour ne pas perdre ses valeurs."); continue; }
                bool triangleMesh = true;
                for (int i = 0; i < source.subMeshCount; i++)
                    if (source.GetTopology(i) != MeshTopology.Triangles) { triangleMesh = false; break; }
                if (!triangleMesh) { slotsAfter += materials.Length; result.skipped++; continue; }

                var groups = new List<List<int>>();
                var uniqueMaterials = new List<Material>();
                var map = new Dictionary<int, int>();
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    int group;
                    if (material && map.TryGetValue(material.GetInstanceID(), out group))
                    {
                        groups[group].Add(slot);
                    }
                    else
                    {
                        group = groups.Count;
                        groups.Add(new List<int> { slot });
                        uniqueMaterials.Add(material);
                        if (material) map[material.GetInstanceID()] = group;
                    }
                }

                if (groups.Count == materials.Length)
                { slotsAfter += materials.Length; continue; }

                Mesh clone = UnityEngine.Object.Instantiate(source);
                clone.name = source.name + "_ZyumieMaterialSlots";
                clone.subMeshCount = groups.Count;
                for (int group = 0; group < groups.Count; group++)
                {
                    var combinedIndices = new List<int>();
                    foreach (int sourceSubmesh in groups[group])
                        combinedIndices.AddRange(source.GetIndices(sourceSubmesh, true));
                    clone.SetIndices(combinedIndices.ToArray(), MeshTopology.Triangles, group, false);
                }
                clone.bounds = source.bounds;

                try
                {
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + AvatarMeshAssetUtility.Sanitize(renderer.name) + "_Slots.asset");
                    AssetDatabase.CreateAsset(clone, assetPath);
                    Undo.RecordObject(renderer, "Zyumie Merge Duplicate Material Slots");
                    assignMesh(AssetDatabase.LoadAssetAtPath<Mesh>(assetPath));
                    renderer.sharedMaterials = uniqueMaterials.ToArray();
                    EditorUtility.SetDirty(renderer);
                    result.processed++;
                    assetsCreated++;
                    slotsAfter += groups.Count;
                    result.notes.Add(renderer.name + " : material slots " + materials.Length + " → " + groups.Count + " (" + (materials.Length - groups.Count) + " doublon(s) fusionné(s)).");
                }
                catch
                {
                    if (clone) UnityEngine.Object.DestroyImmediate(clone);
                    throw;
                }
            }

            result.notes.Insert(0, "Material Slots : " + slotsBefore + " → " + slotsAfter + " (" + (slotsBefore - slotsAfter) + " slot(s) supprimé(s)).");
            result.notes.Add("Seules les références exactes au même Material asset sont fusionnées. Les matériaux différents ne sont jamais fusionnés par simple ressemblance visuelle.");
            result.notes.Add("Les Mesh assets sources ne sont pas écrasés ; les nouveaux meshes conservent les vertices, poids, bindposes et BlendShapes et regroupent uniquement les sous-meshes utilisant le même Material.");
            if (assetsCreated == 0) result.notes.Add("Aucun renderer n'avait de slots de matériau strictement identiques à fusionner.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        public static AvatarMeshOperationResult OptimizePhysBonesOnAvatarCopy(GameObject avatarCopy, bool removeOrphanedNonGlobalColliders = false)
        {
            if (!avatarCopy) throw new ArgumentNullException(nameof(avatarCopy));
            var result = new AvatarMeshOperationResult();
            var components = avatarCopy.GetComponentsInChildren<Component>(true).Where(c => c).ToArray();
            var physBones = components.Where(c => c.GetType().Name == PhysBoneTypeName).ToArray();
            var colliders = components.Where(c => c.GetType().Name == PhysBoneColliderTypeName).ToArray();
            var referencedColliders = new HashSet<Component>();
            bool colliderListsFullyDiscovered = physBones.Length > 0;
            int affectedTransformsTotal = 0;
            int refsBefore = 0, refsAfter = 0, ignoreBefore = 0, ignoreAfter = 0;

            foreach (Component physBone in physBones)
            {
                var serialized = new SerializedObject(physBone);
                serialized.Update();
                SerializedProperty colliderArray = FindObjectReferenceArray(serialized, "collider");
                if (colliderArray == null) colliderListsFullyDiscovered = false;
                if (colliderArray != null)
                {
                    List<UnityEngine.Object> refs = ReadArrayReferences(colliderArray);
                    refsBefore += refs.Count;
                    foreach (var item in refs) if (item is Component component && component) referencedColliders.Add(component);
                    List<UnityEngine.Object> kept = refs.Where(item => item).Distinct().ToList();
                    refsAfter += kept.Count;
                    if (kept.Count != refs.Count)
                    {
                        Undo.RecordObject(physBone, "Zyumie Clean PhysBone Collider References");
                        RewriteObjectReferenceArray(colliderArray, kept);
                        serialized.ApplyModifiedProperties();
                        serialized.Update();
                        result.processed++;
                        result.notes.Add(physBone.name + " : références de colliders " + refs.Count + " → " + kept.Count + ".");
                    }
                }

                SerializedProperty ignoreArray = FindObjectReferenceArray(serialized, "ignore");
                if (ignoreArray != null)
                {
                    List<Transform> ignored = ReadArrayReferences(ignoreArray).OfType<Transform>().Where(t => t).Distinct().ToList();
                    ignoreBefore += ReadArrayReferences(ignoreArray).Count;
                    var minimal = new List<Transform>();
                    foreach (Transform candidate in ignored)
                    {
                        if (minimal.Any(parent => candidate == parent || candidate.IsChildOf(parent))) continue;
                        // The serialized list is not guaranteed to be parent-first. If a parent
                        // appears after one of its children, remove that now-redundant child entry.
                        minimal.RemoveAll(existing => existing && existing.IsChildOf(candidate));
                        minimal.Add(candidate);
                    }
                    ignoreAfter += minimal.Count;
                    if (minimal.Count != ignored.Count || minimal.Count != ignoreArray.arraySize)
                    {
                        Undo.RecordObject(physBone, "Zyumie Clean PhysBone Ignore Transforms");
                        RewriteObjectReferenceArray(ignoreArray, minimal.Cast<UnityEngine.Object>().ToList());
                        serialized.ApplyModifiedProperties();
                        serialized.Update();
                        result.processed++;
                        result.notes.Add(physBone.name + " : Ignore Transforms " + ignored.Count + " → " + minimal.Count + " ; les descendants d'un transform déjà ignoré sont redondants.");
                    }
                }

                serialized.Update();
                Transform rootTransform = serialized.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                if (!rootTransform) rootTransform = physBone.transform;
                SerializedProperty currentIgnoreArray = FindObjectReferenceArray(serialized, "ignore");
                List<Transform> excluded = currentIgnoreArray != null
                    ? ReadArrayReferences(currentIgnoreArray).OfType<Transform>().Where(t => t).ToList()
                    : new List<Transform>();
                int affected = CountAffectedTransforms(rootTransform, excluded);
                affectedTransformsTotal += affected;
                if (affected > 128)
                    result.notes.Add(physBone.name + " : environ " + affected + " transforms concernés. Au-delà de 128, vérifie si la chaîne peut être scindée ; la limite dure documentée est 256 par PhysBone.");
            }

            int duplicatePhysBonesRemoved = 0;
            foreach (var sameObjectGroup in physBones.Where(pb => pb).GroupBy(pb => pb.gameObject))
            {
                var signatures = new HashSet<string>();
                foreach (Component physBone in sameObjectGroup)
                {
                    if (!physBone) continue;
                    string signature = EditorJsonUtility.ToJson(physBone);
                    if (signatures.Add(signature)) continue;
                    Undo.DestroyObjectImmediate(physBone);
                    duplicatePhysBonesRemoved++;
                }
            }

            int orphaned = 0, removed = 0, unknownGlobalState = 0;
            foreach (Component collider in colliders)
            {
                if (!collider || referencedColliders.Contains(collider)) continue;
                bool global;
                if (!TryReadGlobalCollision(collider, out global)) { unknownGlobalState++; continue; }
                if (global) continue;
                if (!colliderListsFullyDiscovered)
                {
                    unknownGlobalState++;
                    continue;
                }
                orphaned++;
                if (removeOrphanedNonGlobalColliders)
                {
                    Undo.DestroyObjectImmediate(collider);
                    removed++;
                }
            }

            result.notes.Insert(0, "PhysBones : " + physBones.Length + " ; PhysBone Colliders : " + colliders.Length + " ; transforms parcourus (estimation) : " + affectedTransformsTotal + ".");
            result.notes.Add("Références colliders explicites : " + refsBefore + " → " + refsAfter + " ; Ignore Transforms : " + ignoreBefore + " → " + ignoreAfter + ".");
            result.notes.Add("PhysBones strictement identiques en double sur le même GameObject supprimés : " + duplicatePhysBonesRemoved + ". La comparaison porte sur la sérialisation complète du composant.");
            result.notes.Add("Colliders non globaux non référencés détectés : " + orphaned + " ; supprimés : " + removed + " ; état/listes non vérifiables : " + unknownGlobalState + ".");
            if (!colliderListsFullyDiscovered && removeOrphanedNonGlobalColliders)
                result.notes.Add("Suppression automatique des colliders désactivée : au moins une liste Colliders PhysBone n'a pas pu être identifiée avec certitude par le SDK installé.");
            result.notes.Add("Aucun Transform de PhysBone n'est supprimé ou réparenté. Les listes Ignore Transforms ne gardent que les racines nécessaires, car ignorer un parent inclut déjà ses enfants. Les chaînes avec trop de transforms doivent être simplifiées manuellement dans le rig pour ne pas changer leur mouvement.");
            if (removeOrphanedNonGlobalColliders)
                result.notes.Add("Les colliders supprimés étaient non globaux et n'étaient référencés par aucun tableau Colliders de PhysBone. Vérifie néanmoins les scripts personnalisés qui pourraient les rechercher par nom.");
            else
                result.notes.Add("Les colliders non référencés sont uniquement signalés. Active leur suppression uniquement après vérification de ton avatar.");
            if (physBones.Length == 0) result.notes.Add("Aucun composant VRCPhysBone détecté. Vérifie que le SDK Avatars est installé et que tu as sélectionné la bonne racine.");
            return result;
        }

        private static SerializedProperty FindObjectReferenceArray(SerializedObject serialized, string token)
        {
            SerializedProperty iterator = serialized.GetIterator();
            while (iterator.Next(true))
            {
                if (!iterator.isArray || iterator.propertyType != SerializedPropertyType.Generic) continue;
                if (iterator.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (iterator.arraySize > 0)
                {
                    SerializedProperty element = iterator.GetArrayElementAtIndex(0);
                    if (element.propertyType == SerializedPropertyType.ObjectReference) return iterator.Copy();
                    continue;
                }

                // Empty lists have no element type to inspect. Accept only known serialized field names.
                string normalizedName = iterator.name.Replace("m_", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
                if ((token == "collider" && normalizedName == "colliders") ||
                    (token == "ignore" && (normalizedName == "ignoretransforms" || normalizedName == "ignore")))
                    return iterator.Copy();
            }
            return null;
        }

        private static List<UnityEngine.Object> ReadArrayReferences(SerializedProperty array)
        {
            var result = new List<UnityEngine.Object>();
            if (array == null || !array.isArray) return result;
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                if (element.propertyType == SerializedPropertyType.ObjectReference)
                    result.Add(element.objectReferenceValue);
            }
            return result;
        }

        private static void RewriteObjectReferenceArray(SerializedProperty array, IList<UnityEngine.Object> values)
        {
            if (array == null || !array.isArray) return;
            while (array.arraySize > 0)
            {
                int previousSize = array.arraySize;
                array.DeleteArrayElementAtIndex(array.arraySize - 1);
                if (array.arraySize == previousSize && array.arraySize > 0)
                    array.DeleteArrayElementAtIndex(array.arraySize - 1);
            }
            for (int i = 0; i < values.Count; i++)
            {
                int index = array.arraySize;
                array.InsertArrayElementAtIndex(index);
                array.GetArrayElementAtIndex(index).objectReferenceValue = values[i];
            }
        }

        private static int CountAffectedTransforms(Transform root, List<Transform> ignored)
        {
            if (!root) return 0;
            int count = 0;
            foreach (Transform current in root.GetComponentsInChildren<Transform>(true))
            {
                bool excluded = ignored.Any(t => current == t || current.IsChildOf(t));
                if (!excluded) count++;
            }
            return count;
        }

        private static bool TryReadGlobalCollision(Component collider, out bool isGlobal)
        {
            isGlobal = false;
            if (!collider) return false;
            var serialized = new SerializedObject(collider);
            serialized.Update();
            SerializedProperty iterator = serialized.GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType == SerializedPropertyType.Boolean &&
                    iterator.name.IndexOf("global", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isGlobal = iterator.boolValue;
                    return true;
                }
            }
            return false;
        }
        internal static void MeasurePhysBoneStats(GameObject root, out int components, out int colliders,
            out int affectedTransforms, out int collisionChecks)
        {
            components = colliders = affectedTransforms = collisionChecks = 0;
            if (!root) return;
            Component[] all = root.GetComponentsInChildren<Component>(true).Where(c => c).ToArray();
            var physBones = all.Where(c => c.GetType().Name == PhysBoneTypeName).ToArray();
            var colliderComponents = all.Where(c => c.GetType().Name == PhysBoneColliderTypeName).ToArray();
            components = physBones.Length;
            colliders = colliderComponents.Length;
            foreach (Component physBone in physBones)
            {
                var serialized = new SerializedObject(physBone);
                serialized.Update();
                Transform rootTransform = serialized.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                if (!rootTransform) rootTransform = physBone.transform;
                SerializedProperty ignores = FindObjectReferenceArray(serialized, "ignore");
                List<Transform> ignored = ignores != null
                    ? ReadArrayReferences(ignores).OfType<Transform>().Where(t => t).ToList()
                    : new List<Transform>();
                int affected = CountAffectedTransforms(rootTransform, ignored);
                affectedTransforms += affected;
                SerializedProperty colliderArray = FindObjectReferenceArray(serialized, "collider");
                int explicitColliderCount = colliderArray != null
                    ? ReadArrayReferences(colliderArray).Count(item => item)
                    : 0;
                collisionChecks += affected * explicitColliderCount;
            }
        }

        internal static string Report(GameObject root)
        {
            if (!root) return "Sélectionne la racine de l'avatar.";
            var b = new System.Text.StringBuilder();
            b.AppendLine("ZYUMIE AVATAR SUITE — AUDIT SKINNING / MATERIALS / PHYSBONES");
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r && r.sharedMesh).ToArray();
            var uniqueBones = new HashSet<Transform>();
            int totalBoneSlots = 0, unusedBoneSlots = 0, overFourVertices = 0, maxInfluencesSeen = 0;
            int materialSlots = 0, duplicateMaterialSlots = 0;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Transform[] bones = renderer.bones ?? Array.Empty<Transform>();
                totalBoneSlots += bones.Length;
                foreach (Transform bone in bones) if (bone) uniqueBones.Add(bone);
                materialSlots += renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
                if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 1)
                    duplicateMaterialSlots += renderer.sharedMaterials.Where(m => m).GroupBy(m => m.GetInstanceID()).Sum(g => Math.Max(0, g.Count() - 1));
                if (!renderer.sharedMesh.isReadable) continue;
                using (NativeArray<byte> counts = renderer.sharedMesh.GetBonesPerVertex())
                using (NativeArray<BoneWeight1> weights = renderer.sharedMesh.GetAllBoneWeights())
                {
                    if (counts.Length != renderer.sharedMesh.vertexCount) continue;
                    var used = new HashSet<int>();
                    for (int i = 0; i < counts.Length; i++)
                    {
                        maxInfluencesSeen = Math.Max(maxInfluencesSeen, counts[i]);
                        if (counts[i] > 4) overFourVertices++;
                    }
                    foreach (BoneWeight1 weight in weights) used.Add(weight.boneIndex);
                    for (int i = 0; i < bones.Length; i++) if (!used.Contains(i)) unusedBoneSlots++;
                }
            }
            int physBoneCount, colliderCount, affectedCount, collisionChecks;
            MeasurePhysBoneStats(root, out physBoneCount, out colliderCount, out affectedCount, out collisionChecks);
            b.AppendLine("\nSKINNED MESHES");
            b.AppendLine("  Renderers : " + renderers.Length);
            b.AppendLine("  Bone slots cumulés : " + totalBoneSlots);
            b.AppendLine("  Bones Transform uniques référencés : " + uniqueBones.Count);
            b.AppendLine("  Entrées de bones sans poids : " + unusedBoneSlots);
            b.AppendLine("  Vertex avec plus de 4 influences : " + overFourVertices);
            b.AppendLine("  Maximum d'influences détecté sur un vertex : " + maxInfluencesSeen);
            b.AppendLine("\nMATERIAL SLOTS");
            b.AppendLine("  Slots des Skinned Meshes : " + materialSlots);
            b.AppendLine("  Slots en double (même Material asset sur un même renderer) : " + duplicateMaterialSlots);
            b.AppendLine("\nPHYSBONES");
            b.AppendLine("  Components : " + physBoneCount);
            b.AppendLine("  Colliders components : " + colliderCount);
            b.AppendLine("  Affected Transforms (estimation) : " + affectedCount);
            b.AppendLine("  Collision Check Count (estimation sur les listes explicites) : " + collisionChecks);
            b.AppendLine("\nLIMITES DE L'OPTIMISATION");
            b.AppendLine("  Le nettoyage Skinning retire les slots non pondérés et limite les influences ; les Transform du rig restent présents.");
            b.AppendLine("  Le nettoyage Material Slots fusionne uniquement les références strictement identiques ; les shaders/matériaux différents restent distincts.");
            b.AppendLine("  Le nettoyage PhysBone retire les références Colliders dupliquées/nulles et les Ignore Transforms enfants déjà couverts par un parent.");
            b.AppendLine("  La suppression de Transform de chaînes PhysBone n'est jamais automatique : cela pourrait casser animations, contraintes, rig ou mouvement secondaire.");
            b.AppendLine("  Les estimations PhysBone ne remplacent pas les statistiques du VRChat SDK Builder.");
            return b.ToString();
        }

    }
}
