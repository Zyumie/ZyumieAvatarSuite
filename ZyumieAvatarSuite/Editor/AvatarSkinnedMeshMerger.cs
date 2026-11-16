using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zyumie.AvatarSuite
{
    /// <summary>
    /// Merges only renderers that share an identical transform, ordered skeleton,
    /// root bone, bindposes and renderer state. BlendShape-bearing meshes are
    /// intentionally excluded because Animator/FX animation bindings target the
    /// original renderer paths and would need a separate controller remapper.
    /// </summary>
    internal static class AvatarSkinnedMeshMerger
    {
        private const int UvChannelCount = 8;

        private sealed class MeshPart
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Color[] colors;
            public readonly List<Vector4>[] uvs = new List<Vector4>[UvChannelCount];
            public bool hasNormals;
            public bool hasTangents;
            public bool hasColors;
            public bool[] hasUv = new bool[UvChannelCount];
            public byte[] bonesPerVertex;
            public BoneWeight1[] boneWeights;
        }

        public static AvatarMeshOperationResult MergeCompatibleOnAvatarCopy(GameObject avatarCopy)
        {
            if (!avatarCopy) throw new ArgumentNullException(nameof(avatarCopy));
            var result = new AvatarMeshOperationResult();
            string folder = AvatarMeshAssetUtility.EnsureAvatarFolder(avatarCopy.name, "Meshes_Merged");
            result.outputFolder = folder;

            var renderers = avatarCopy.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r && r.sharedMesh).ToList();
            var consumed = new HashSet<SkinnedMeshRenderer>();
            var animationAudit = new AvatarAnimationBindingAudit(avatarCopy);

            try
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    SkinnedMeshRenderer first = renderers[i];
                    if (!first || consumed.Contains(first)) continue;
                    var group = new List<SkinnedMeshRenderer> { first };
                    for (int j = i + 1; j < renderers.Count; j++)
                    {
                        SkinnedMeshRenderer candidate = renderers[j];
                        if (!candidate || consumed.Contains(candidate)) continue;
                        string reason;
                        if (CanMergePair(avatarCopy, first, candidate, animationAudit, out reason)) group.Add(candidate);
                    }

                    if (group.Count < 2) continue;
                    EditorUtility.DisplayProgressBar("Zyumie — fusion des Skinned Meshes",
                        "Préparation : " + first.name + " (" + group.Count + " renderers)",
                        renderers.Count == 0 ? 1f : (float)i / renderers.Count);

                    Mesh combined;
                    Material[] combinedMaterials;
                    string failure;
                    if (!TryBuildCombined(group, out combined, out combinedMaterials, out failure))
                    {
                        result.skipped += group.Count;
                        result.notes.Add(first.name + " : fusion annulée — " + failure);
                        foreach (var item in group) consumed.Add(item);
                        continue;
                    }

                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + AvatarMeshAssetUtility.Sanitize(first.name) + "_Merged.asset");
                    AssetDatabase.CreateAsset(combined, assetPath);
                    Undo.RecordObject(first, "Zyumie Assign Merged Skinned Mesh");
                    first.sharedMesh = combined;
                    first.sharedMaterials = combinedMaterials;
                    Bounds mergedBounds = group[0].localBounds;
                    for (int boundsIndex = 1; boundsIndex < group.Count; boundsIndex++)
                    {
                        Bounds otherBounds = group[boundsIndex].localBounds;
                        mergedBounds.Encapsulate(otherBounds.min);
                        mergedBounds.Encapsulate(otherBounds.max);
                    }
                    first.localBounds = mergedBounds;
                    EditorUtility.SetDirty(first);

                    for (int k = 1; k < group.Count; k++)
                    {
                        SkinnedMeshRenderer redundant = group[k];
                        if (redundant)
                        {
                            Undo.DestroyObjectImmediate(redundant);
                            result.renderersMerged++;
                        }
                    }

                    result.processed++;
                    foreach (var item in group) consumed.Add(item);
                    result.notes.Add("Groupe " + first.name + " : " + group.Count + " renderers → 1, " + combined.vertexCount.ToString("N0") + " vertices, " + combined.subMeshCount + " submeshes, " + AvatarMeshOptimizer.CountTriangles(combined).ToString("N0") + " triangles.");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            result.skipped += renderers.Count(r => r && !consumed.Contains(r));
            if (result.processed == 0)
                result.notes.Add("Aucun groupe ne remplit les critères stricts. La fusion est volontairement conservatrice.");
            result.notes.Add("Fusion uniquement sur une copie : les assets de meshes sources restent inchangés.");
            result.notes.Add("Les meshes avec BlendShapes sont exclus : leurs courbes Animator/FX et chemins de renderer nécessitent un remappage spécifique non réalisé ici.");
            result.notes.Add("Les GameObjects vides restent en place pour ne pas casser les chemins hiérarchiques, scripts ou références d'avatar.");
            return result;
        }

        internal static bool CanMergePair(GameObject avatarRoot, SkinnedMeshRenderer a, SkinnedMeshRenderer b,
            AvatarAnimationBindingAudit animationAudit, out string reason)
        {
            reason = null;
            if (!avatarRoot || !a || !b || !a.sharedMesh || !b.sharedMesh) { reason = "avatar, renderer ou mesh manquant."; return false; }
            if ((a.transform != avatarRoot.transform && !a.transform.IsChildOf(avatarRoot.transform)) ||
                (b.transform != avatarRoot.transform && !b.transform.IsChildOf(avatarRoot.transform)))
            { reason = "renderer situé hors de la racine d'avatar sélectionnée."; return false; }
            if (!a.sharedMesh.isReadable || !b.sharedMesh.isReadable) { reason = "mesh non lisible (Read/Write désactivé)."; return false; }
            if (a.sharedMesh.blendShapeCount > 0 || b.sharedMesh.blendShapeCount > 0) { reason = "BlendShapes présents : remappage Animator/FX requis."; return false; }
            if (a.enabled != b.enabled || a.gameObject.activeInHierarchy != b.gameObject.activeInHierarchy || a.gameObject.layer != b.gameObject.layer)
            { reason = "état enabled, activation ou layer différent."; return false; }
            if (a.rootBone != b.rootBone) { reason = "rootBone différent."; return false; }
            if (!SameReferences(a.bones, b.bones)) { reason = "liste ou ordre des bones différent."; return false; }
            if (a.bones == null || a.bones.Length == 0) { reason = "rig sans bones assignés."; return false; }
            if (a.sharedMesh.bindposes == null || a.sharedMesh.bindposes.Length != a.bones.Length ||
                b.sharedMesh.bindposes == null || b.sharedMesh.bindposes.Length != b.bones.Length)
            { reason = "nombre de bindposes différent du nombre de bones."; return false; }
            if (!SameMatrixArray(a.sharedMesh.bindposes, b.sharedMesh.bindposes)) { reason = "bindposes différentes."; return false; }
            if (!MatrixApproximately(a.transform.localToWorldMatrix, b.transform.localToWorldMatrix))
            { reason = "transforms monde différents ; une fusion modifierait le repère de skinning."; return false; }
            if (a.quality != b.quality || a.updateWhenOffscreen != b.updateWhenOffscreen ||
                a.skinnedMotionVectors != b.skinnedMotionVectors ||
                a.forceMatrixRecalculationPerRender != b.forceMatrixRecalculationPerRender)
            { reason = "paramètres SkinnedMeshRenderer différents."; return false; }
            if (a.HasPropertyBlock() || b.HasPropertyBlock())
            { reason = "MaterialPropertyBlock présent sur un renderer."; return false; }
            if (!RendererSettingsMatch(a, b)) { reason = "paramètres de rendu, probes, ombres ou tri différents."; return false; }
            if (!IsTriangleMesh(a.sharedMesh) || !IsTriangleMesh(b.sharedMesh)) { reason = "une topologie n'est pas triangulaire."; return false; }
            if (a.sharedMaterials == null || b.sharedMaterials == null || a.sharedMaterials.Length != a.sharedMesh.subMeshCount || b.sharedMaterials.Length != b.sharedMesh.subMeshCount)
            { reason = "nombre de matériaux différent du nombre de submeshes."; return false; }
            if (!VertexLayoutMatches(a.sharedMesh, b.sharedMesh)) { reason = "canaux vertices / UV / couleurs incompatibles."; return false; }
            if (!HasValidSkinWeights(a.sharedMesh) || !HasValidSkinWeights(b.sharedMesh))
            { reason = "poids de skinning absents ou incohérents."; return false; }
            if (animationAudit != null && animationAudit.HasUnsafeBindings(a, b, out reason)) return false;
            return true;
        }

        private static bool TryBuildCombined(List<SkinnedMeshRenderer> renderers, out Mesh combined, out Material[] materials, out string failure)
        {
            combined = null;
            materials = null;
            failure = null;
            if (renderers == null || renderers.Count < 2) { failure = "groupe insuffisant."; return false; }

            var parts = new List<MeshPart>(renderers.Count);
            foreach (var renderer in renderers)
            {
                MeshPart part;
                if (!TryReadPart(renderer, out part, out failure)) return false;
                if (!VertexLayoutMatches(parts.Count == 0 ? part.mesh : parts[0].mesh, part.mesh))
                { failure = "les layouts de vertices ne correspondent plus dans le groupe."; return false; }
                parts.Add(part);
            }

            long totalVerticesLong = parts.Sum(p => (long)p.vertices.Length);
            if (totalVerticesLong > int.MaxValue) { failure = "limite d'index de mesh dépassée."; return false; }
            int totalVertices = (int)totalVerticesLong;
            int totalSubMeshes = parts.Sum(p => p.mesh.subMeshCount);
            var vertices = new List<Vector3>(totalVertices);
            bool hasNormals = parts[0].hasNormals;
            bool hasTangents = parts[0].hasTangents;
            bool hasColors = parts[0].hasColors;
            var normals = hasNormals ? new List<Vector3>(totalVertices) : null;
            var tangents = hasTangents ? new List<Vector4>(totalVertices) : null;
            var colors = hasColors ? new List<Color>(totalVertices) : null;
            var uvOutput = new List<Vector4>[UvChannelCount];
            for (int channel = 0; channel < UvChannelCount; channel++)
                if (parts[0].hasUv[channel]) uvOutput[channel] = new List<Vector4>(totalVertices);
            var allCounts = new List<byte>(totalVertices);
            var allWeights = new List<BoneWeight1>();
            var materialList = new List<Material>(totalSubMeshes);
            var subMeshIndices = new List<int[]>(totalSubMeshes);
            int baseVertex = 0;

            foreach (var part in parts)
            {
                vertices.AddRange(part.vertices);
                if (hasNormals) normals.AddRange(part.normals);
                if (hasTangents) tangents.AddRange(part.tangents);
                if (hasColors) colors.AddRange(part.colors);
                for (int channel = 0; channel < UvChannelCount; channel++)
                    if (part.hasUv[channel]) uvOutput[channel].AddRange(part.uvs[channel]);
                allCounts.AddRange(part.bonesPerVertex);
                allWeights.AddRange(part.boneWeights);

                for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                {
                    int[] sourceIndices = part.mesh.GetIndices(sub);
                    var offsetIndices = new int[sourceIndices.Length];
                    for (int i = 0; i < sourceIndices.Length; i++) offsetIndices[i] = sourceIndices[i] + baseVertex;
                    subMeshIndices.Add(offsetIndices);
                    materialList.Add(part.renderer.sharedMaterials[sub]);
                }
                baseVertex += part.vertices.Length;
            }

            combined = new Mesh { name = parts[0].mesh.name + "_ZyumieMerged" };
            combined.indexFormat = totalVertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            combined.vertices = vertices.ToArray();
            if (hasNormals) combined.normals = normals.ToArray();
            if (hasTangents) combined.tangents = tangents.ToArray();
            if (hasColors) combined.colors = colors.ToArray();
            for (int channel = 0; channel < UvChannelCount; channel++)
                if (uvOutput[channel] != null) combined.SetUVs(channel, uvOutput[channel]);
            combined.bindposes = parts[0].mesh.bindposes;
            SetCombinedBoneWeights(combined, allCounts, allWeights);
            combined.subMeshCount = totalSubMeshes;
            for (int sub = 0; sub < totalSubMeshes; sub++)
                combined.SetIndices(subMeshIndices[sub], MeshTopology.Triangles, sub, false);
            combined.RecalculateBounds();
            materials = materialList.ToArray();
            return true;
        }

        private static bool TryReadPart(SkinnedMeshRenderer renderer, out MeshPart part, out string reason)
        {
            part = null;
            reason = null;
            Mesh mesh = renderer.sharedMesh;
            if (!mesh || !mesh.isReadable) { reason = renderer.name + " : mesh non lisible."; return false; }
            if (mesh.blendShapeCount != 0) { reason = renderer.name + " : BlendShapes non pris en charge par la fusion automatique."; return false; }
            if (!IsTriangleMesh(mesh)) { reason = renderer.name + " : topologie non triangulaire."; return false; }

            byte[] counts;
            BoneWeight1[] weights;
            if (!ReadSkinWeights(mesh, out counts, out weights)) { reason = renderer.name + " : poids de skinning invalides."; return false; }
            var data = new MeshPart
            {
                renderer = renderer,
                mesh = mesh,
                vertices = mesh.vertices,
                normals = mesh.normals,
                tangents = mesh.tangents,
                colors = mesh.colors,
                bonesPerVertex = counts,
                boneWeights = weights
            };
            data.hasNormals = data.normals != null && data.normals.Length == mesh.vertexCount;
            data.hasTangents = data.tangents != null && data.tangents.Length == mesh.vertexCount;
            data.hasColors = data.colors != null && data.colors.Length == mesh.vertexCount;
            for (int channel = 0; channel < UvChannelCount; channel++)
            {
                data.uvs[channel] = new List<Vector4>(mesh.vertexCount);
                mesh.GetUVs(channel, data.uvs[channel]);
                data.hasUv[channel] = data.uvs[channel].Count == mesh.vertexCount;
                if (!data.hasUv[channel]) data.uvs[channel].Clear();
            }
            part = data;
            return true;
        }

        private static bool ReadSkinWeights(Mesh mesh, out byte[] counts, out BoneWeight1[] weights)
        {
            counts = null;
            weights = null;
            var countArray = mesh.GetBonesPerVertex();
            if (countArray.Length != mesh.vertexCount) return false;
            var weightArray = mesh.GetAllBoneWeights();
            counts = new byte[countArray.Length];
            weights = new BoneWeight1[weightArray.Length];
            int total = 0;
            for (int i = 0; i < countArray.Length; i++)
            {
                counts[i] = countArray[i];
                if (counts[i] == 0) return false;
                total += counts[i];
            }
            if (total != weightArray.Length) return false;
            int bindposeCount = mesh.bindposes != null ? mesh.bindposes.Length : 0;
            for (int i = 0; i < weightArray.Length; i++)
            {
                weights[i] = weightArray[i];
                if (weights[i].boneIndex < 0 || weights[i].boneIndex >= bindposeCount) return false;
            }
            return true;
        }

        private static void SetCombinedBoneWeights(Mesh mesh, List<byte> counts, List<BoneWeight1> weights)
        {
            var nativeCounts = new NativeArray<byte>(counts.ToArray(), Allocator.Temp);
            var nativeWeights = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp);
            try { mesh.SetBoneWeights(nativeCounts, nativeWeights); }
            finally { if (nativeCounts.IsCreated) nativeCounts.Dispose(); if (nativeWeights.IsCreated) nativeWeights.Dispose(); }
        }

        private static bool HasValidSkinWeights(Mesh mesh)
        {
            if (!mesh || mesh.vertexCount == 0) return false;
            var counts = mesh.GetBonesPerVertex();
            if (counts.Length != mesh.vertexCount) return false;
            for (int i = 0; i < counts.Length; i++) if (counts[i] == 0) return false;
            return true;
        }

        private static bool VertexLayoutMatches(Mesh a, Mesh b)
        {
            if (!a || !b) return false;
            if ((a.normals != null && a.normals.Length == a.vertexCount) != (b.normals != null && b.normals.Length == b.vertexCount)) return false;
            if ((a.tangents != null && a.tangents.Length == a.vertexCount) != (b.tangents != null && b.tangents.Length == b.vertexCount)) return false;
            if ((a.colors != null && a.colors.Length == a.vertexCount) != (b.colors != null && b.colors.Length == b.vertexCount)) return false;
            for (int channel = 0; channel < UvChannelCount; channel++)
            {
                var uvA = new List<Vector4>();
                var uvB = new List<Vector4>();
                a.GetUVs(channel, uvA);
                b.GetUVs(channel, uvB);
                if ((uvA.Count == a.vertexCount) != (uvB.Count == b.vertexCount)) return false;
            }
            return true;
        }

        private static bool IsTriangleMesh(Mesh mesh)
        {
            if (!mesh || mesh.subMeshCount == 0) return false;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) != MeshTopology.Triangles) return false;
            return true;
        }

        private static bool RendererSettingsMatch(SkinnedMeshRenderer a, SkinnedMeshRenderer b)
        {
            return a.shadowCastingMode == b.shadowCastingMode &&
                   a.receiveShadows == b.receiveShadows &&
                   a.lightProbeUsage == b.lightProbeUsage &&
                   a.reflectionProbeUsage == b.reflectionProbeUsage &&
                   a.probeAnchor == b.probeAnchor &&
                   a.lightProbeProxyVolumeOverride == b.lightProbeProxyVolumeOverride &&
                   a.lightmapIndex == b.lightmapIndex &&
                   a.lightmapScaleOffset == b.lightmapScaleOffset &&
                   a.realtimeLightmapIndex == b.realtimeLightmapIndex &&
                   a.realtimeLightmapScaleOffset == b.realtimeLightmapScaleOffset &&
                   a.motionVectorGenerationMode == b.motionVectorGenerationMode &&
                   a.allowOcclusionWhenDynamic == b.allowOcclusionWhenDynamic &&
                   a.renderingLayerMask == b.renderingLayerMask &&
                   a.sortingLayerID == b.sortingLayerID &&
                   a.sortingOrder == b.sortingOrder;
        }

        private static bool SameReferences<T>(T[] a, T[] b) where T : UnityEngine.Object
        {
            if (a == null || b == null || a.Length != b.Length) return a == b;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SameMatrixArray(Matrix4x4[] a, Matrix4x4[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!MatrixApproximately(a[i], b[i])) return false;
            return true;
        }

        private static bool MatrixApproximately(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 0.00001f) return false;
            return true;
        }
    }
}
