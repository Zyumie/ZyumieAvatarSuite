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
    internal sealed class AvatarMeshOperationResult
    {
        public int processed;
        public int skipped;
        public int beforeTriangles;
        public int afterTriangles;
        public int renderersMerged;
        public int targetReductionPercent;
        public string outputFolder;
        public readonly List<string> notes = new List<string>();

        public string Summary(string title)
        {
            var b = new System.Text.StringBuilder();
            b.AppendLine("ZYUMIE AVATAR SUITE — " + title);
            b.AppendLine("Opérations / groupes traités : " + processed);
            b.AppendLine("Renderers ignorés : " + skipped);
            if (targetReductionPercent > 0) b.AppendLine("Réduction cible demandée : " + targetReductionPercent + "% (par mesh, résultat réel mesuré ci-dessous)");
            if (beforeTriangles > 0 || afterTriangles > 0)
            {
                b.AppendLine("Triangles avant : " + beforeTriangles.ToString("N0"));
                b.AppendLine("Triangles après : " + afterTriangles.ToString("N0"));
                if (beforeTriangles > 0)
                    b.AppendLine("Réduction réelle : " + (100.0 * (beforeTriangles - afterTriangles) / beforeTriangles).ToString("0.0") + "%");
            }
            if (renderersMerged > 0) b.AppendLine("SkinnedMeshRenderers supprimés par fusion : " + renderersMerged);
            if (!string.IsNullOrEmpty(outputFolder)) b.AppendLine("Assets générés : " + outputFolder);
            foreach (string note in notes) b.AppendLine("- " + note);
            return b.ToString();
        }
    }

    /// <summary>
    /// Adaptive spatial vertex-clustering decimator for readable skinned and static triangle meshes.
    /// It copies bindposes, arbitrary Unity bone weights, UV channels, colors and BlendShape
    /// frames into a new Mesh asset. Some per-vertex attributes are averaged and are therefore lossy.
    /// </summary>
    internal static class AvatarMeshOptimizer
    {
        private const int UvChannelCount = 8;
        private const float SignificantSecondaryBoneWeight = 0.20f;

        private sealed class SkinData
        {
            public byte[] counts;
            public BoneWeight1[] weights;
            public int[] starts;
            public int maxInfluences;

            public bool HasWeights { get { return counts != null && weights != null && starts != null && weights.Length > 0; } }
        }

        private sealed class VertexAccumulator
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector4 tangent;
            public Color color;
            public readonly Vector4[] uvs = new Vector4[UvChannelCount];
            public readonly Dictionary<int, float> boneWeights = new Dictionary<int, float>();
            public int count;

            public void Add(MeshData data, int vertex)
            {
                position += data.vertices[vertex];
                if (data.hasNormals) normal += data.normals[vertex];
                if (data.hasTangents) tangent += data.tangents[vertex];
                if (data.hasColors) color += data.colors[vertex];
                for (int channel = 0; channel < UvChannelCount; channel++)
                    if (data.hasUv[channel]) uvs[channel] += data.uvs[channel][vertex];

                if (data.skin.HasWeights)
                {
                    int start = data.skin.starts[vertex];
                    int amount = data.skin.counts[vertex];
                    for (int i = 0; i < amount; i++)
                    {
                        BoneWeight1 weight = data.skin.weights[start + i];
                        float previous;
                        boneWeights.TryGetValue(weight.boneIndex, out previous);
                        boneWeights[weight.boneIndex] = previous + weight.weight;
                    }
                }
                count++;
            }
        }

        private sealed class MeshData
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Color[] colors;
            public bool hasNormals;
            public bool hasTangents;
            public bool hasColors;
            public readonly List<Vector4>[] uvs = new List<Vector4>[UvChannelCount];
            public readonly bool[] hasUv = new bool[UvChannelCount];
            public SkinData skin;
            public bool[] protectedVertices;
        }

        private struct ClusterKey : IEquatable<ClusterKey>
        {
            public int x, y, z;
            public int u0x, u0y, u1x, u1y;
            public int n0, n1, n2;
            public int b0, b1;
            public int protectedVertex;

            public bool Equals(ClusterKey other)
            {
                return x == other.x && y == other.y && z == other.z &&
                       u0x == other.u0x && u0y == other.u0y && u1x == other.u1x && u1y == other.u1y &&
                       n0 == other.n0 && n1 == other.n1 && n2 == other.n2 &&
                       b0 == other.b0 && b1 == other.b1 && protectedVertex == other.protectedVertex;
            }

            public override bool Equals(object obj) { return obj is ClusterKey && Equals((ClusterKey)obj); }
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x;
                    hash = hash * 31 + y;
                    hash = hash * 31 + z;
                    hash = hash * 31 + u0x;
                    hash = hash * 31 + u0y;
                    hash = hash * 31 + u1x;
                    hash = hash * 31 + u1y;
                    hash = hash * 31 + n0;
                    hash = hash * 31 + n1;
                    hash = hash * 31 + n2;
                    hash = hash * 31 + b0;
                    hash = hash * 31 + b1;
                    hash = hash * 31 + protectedVertex;
                    return hash;
                }
            }
        }

        private struct ClusterProfile
        {
            public int uvBuckets;
            public int normalBuckets;
            public bool preserveSecondaryBone;
            public string label;

            public ClusterProfile(int uv, int normal, bool preserveSecond, string profileLabel)
            {
                uvBuckets = uv;
                normalBuckets = normal;
                preserveSecondaryBone = preserveSecond;
                label = profileLabel;
            }
        }

        private sealed class ClusterBuild
        {
            public int[] map;
            public readonly List<VertexAccumulator> vertices = new List<VertexAccumulator>();
            public int triangleCount;
        }

        private struct TriangleKey : IEquatable<TriangleKey>
        {
            public int a, b, c;
            public TriangleKey(int x, int y, int z)
            {
                if (x > y) { int t = x; x = y; y = t; }
                if (y > z) { int t = y; y = z; z = t; }
                if (x > y) { int t = x; x = y; y = t; }
                a = x; b = y; c = z;
            }
            public bool Equals(TriangleKey other) { return a == other.a && b == other.b && c == other.c; }
            public override bool Equals(object obj) { return obj is TriangleKey && Equals((TriangleKey)obj); }
            public override int GetHashCode() { unchecked { return (a * 397) ^ (b * 31) ^ c; } }
        }

        public static AvatarMeshOperationResult ReduceOnAvatarCopy(GameObject avatarCopy, int targetReductionPercent)
        {
            if (!avatarCopy) throw new ArgumentNullException(nameof(avatarCopy));
            targetReductionPercent = Mathf.Clamp(targetReductionPercent, 5, 80);
            var result = new AvatarMeshOperationResult { targetReductionPercent = targetReductionPercent };
            string folder = AvatarMeshAssetUtility.EnsureAvatarFolder(avatarCopy.name, "Meshes_Decimated");
            result.outputFolder = folder;

            var skinned = avatarCopy.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r && r.sharedMesh).Cast<Renderer>().ToArray();
            var skinnedObjects = new HashSet<GameObject>(skinned.Select(r => r.gameObject));
            var basic = avatarCopy.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f && f.sharedMesh && f.GetComponent<MeshRenderer>() && !skinnedObjects.Contains(f.gameObject))
                .Select(f => new { Filter = f, Renderer = (Renderer)f.GetComponent<MeshRenderer>() })
                .Where(x => x.Renderer != null).ToArray();

            int total = skinned.Length + basic.Length;
            int operationIndex = 0;
            try
            {
                foreach (Renderer renderer in skinned)
                {
                    SkinnedMeshRenderer smr = (SkinnedMeshRenderer)renderer;
                    ProcessRendererMesh(renderer, smr.sharedMesh, mesh => smr.sharedMesh = mesh,
                        "SkinnedMeshRenderer", ++operationIndex, total, folder, targetReductionPercent, result);
                }

                foreach (var entry in basic)
                {
                    MeshFilter filter = entry.Filter;
                    ProcessRendererMesh(entry.Renderer, filter.sharedMesh, mesh => filter.sharedMesh = mesh,
                        "MeshRenderer", ++operationIndex, total, folder, targetReductionPercent, result);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (result.processed == 0 && result.skipped == 0) result.notes.Add("Aucun MeshRenderer lisible trouvé.");
            if (result.beforeTriangles > 0)
            {
                double totalReduction = 100.0 * (result.beforeTriangles - result.afterTriangles) / result.beforeTriangles;
                if (totalReduction + 1.0 < targetReductionPercent)
                    result.notes.Add("ATTENTION : la réduction globale est inférieure à la cible. Les lignes par renderer indiquent les protections et limites restantes.");
            }
            result.notes.Add("Le total inclut maintenant les SkinnedMeshRenderer ET les MeshRenderer statiques. Les meshes sources ne sont jamais écrasés.");
            result.notes.Add("La protection par nom examine le nom du Renderer et ses parents, jamais le nom générique du Mesh asset importé : un FBX qui nomme tous ses sous-meshes 'Skin' ne bloque donc plus tous les vêtements.");
            result.notes.Add("Les renderers explicitement nommés Face/Head/Body/Eye/Hair restent protégés. Dans les autres meshes, les vertices modifiés de façon significative par BlendShapes, fortement pondérés par des bones sensibles, ou appartenant à un sous-mesh de matériau sensible restent verrouillés.");
            result.notes.Add("Le profil peut devenir plus agressif sur les vêtements/accessoires. UV, normales et poids des os sont moyennés dans les clusters : inspecte les textures, coutures, silhouettes et articulations avant publication.");
            result.notes.Add("Cible indicative, pas garantie : les zones protégées et les limites topologiques peuvent empêcher d'atteindre le pourcentage demandé. Le Builder VRChat reste la référence pour le total final.");
            return result;
        }

        private static void ProcessRendererMesh(Renderer renderer, Mesh source, Action<Mesh> assignMesh,
            string rendererType, int index, int total, string folder, int targetReductionPercent,
            AvatarMeshOperationResult result)
        {
            EditorUtility.DisplayProgressBar("Zyumie — réduction polygonale",
                renderer.name + " (" + index + "/" + total + ")", total == 0 ? 1f : (float)(index - 1) / total);

            int originalTriangles = CountTriangles(source);
            result.beforeTriangles += originalTriangles;
            string protectionReason;
            if (IsProtectedRenderer(renderer, source, out protectionReason))
            {
                result.skipped++;
                result.afterTriangles += originalTriangles;
                result.notes.Add(renderer.name + " [" + rendererType + "] : protégé — " + protectionReason);
                return;
            }

            Mesh decimated = null;
            try
            {
                int finalTriangles;
                int protectedVertexCount;
                string reason;
                string profileLabel;
                if (!TryDecimate(renderer, source, targetReductionPercent, out decimated, out finalTriangles,
                    out protectedVertexCount, out reason, out profileLabel))
                {
                    result.skipped++;
                    result.afterTriangles += originalTriangles;
                    result.notes.Add(renderer.name + " [" + rendererType + "] : " + reason);
                    return;
                }

                string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Sanitize(renderer.name) + "_Decimated.asset");
                Bounds newMeshBounds = decimated.bounds;
                AssetDatabase.CreateAsset(decimated, assetPath);
                decimated = null;
                Undo.RecordObject(renderer, "Zyumie Assign Decimated Mesh");
                assignMesh(AssetDatabase.LoadAssetAtPath<Mesh>(assetPath));
                if (renderer is SkinnedMeshRenderer smr)
                {
                    Bounds merged = smr.localBounds;
                    merged.Encapsulate(newMeshBounds.min);
                    merged.Encapsulate(newMeshBounds.max);
                    smr.localBounds = merged;
                }
                EditorUtility.SetDirty(renderer);
                result.processed++;
                result.afterTriangles += finalTriangles;
                double achieved = 100.0 * (originalTriangles - finalTriangles) / Math.Max(1, originalTriangles);
                result.notes.Add(renderer.name + " [" + rendererType + "] : " + originalTriangles.ToString("N0") + " → " + finalTriangles.ToString("N0") +
                    " triangles (" + achieved.ToString("0.0") + "% réel ; profil " + profileLabel + " ; " + protectedVertexCount.ToString("N0") + " vertices protégés).");
            }
            catch (Exception ex)
            {
                if (decimated) UnityEngine.Object.DestroyImmediate(decimated);
                result.skipped++;
                result.afterTriangles += originalTriangles;
                result.notes.Add(renderer.name + " [" + rendererType + "] : erreur pendant la réduction — " + ex.Message);
            }
        }

        private static bool IsProtectedRenderer(Renderer renderer, Mesh mesh, out string reason)
        {
            reason = null;
            if (!renderer) { reason = "renderer manquant."; return true; }

            string[] ownNameTokens =
            {
                "face", "head", "skin", "body", "eye", "eyelid", "lash", "brow", "mouth", "teeth", "tongue",
                "hair", "bang", "fringe", "scalp", "cheveux", "frange", "visage", "yeux", "cils", "sourcil", "bouche", "tete"
            };
            string ownNames = renderer.name ?? string.Empty;
            string match = ownNameTokens.FirstOrDefault(token => ContainsNameToken(ownNames, token));
            if (!string.IsNullOrEmpty(match))
            {
                reason = "nom sensible du Renderer (" + match + ") : mesh entier protégé.";
                return true;
            }

            string[] parentNameTokens = { "face", "head", "hair", "bang", "fringe", "scalp", "cheveux", "frange", "visage", "tete" };
            for (Transform current = renderer.transform.parent; current != null; current = current.parent)
            {
                match = parentNameTokens.FirstOrDefault(token => ContainsNameToken(current.name, token));
                if (!string.IsNullOrEmpty(match))
                {
                    reason = "parent sensible (" + current.name + ") : mesh entier protégé.";
                    return true;
                }
            }

            if (mesh.vertexCount < 250 || CountTriangles(mesh) < 200)
            {
                reason = "mesh petit/détaillé : laissé intact pour éviter de supprimer des accessoires fins.";
                return true;
            }
            return false;
        }

        private static bool ContainsNameToken(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) return false;
            string normalized = new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            string normalizedToken = new string(token.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (normalized == normalizedToken) return true;

            string[] parts = value.Split(new[] { ' ', '_', '-', '.', '/', '\\', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(part => string.Equals(part, token, StringComparison.OrdinalIgnoreCase)) ||
                   (token.Length >= 4 && normalized.Contains(normalizedToken));
        }

        private static bool TryDecimate(Renderer renderer, Mesh source, int targetPercent,
            out Mesh output, out int outputTriangles, out int protectedVertexCount, out string reason, out string profileLabel)
        {
            output = null;
            outputTriangles = 0;
            protectedVertexCount = 0;
            reason = null;
            profileLabel = null;
            if (!source.isReadable) { reason = "mesh non lisible (active Read/Write dans les réglages d'import du FBX)."; return false; }
            if (source.vertexCount < 4) { reason = "moins de 4 vertices."; return false; }
            if (source.subMeshCount == 0) { reason = "aucun submesh."; return false; }
            for (int i = 0; i < source.subMeshCount; i++)
                if (source.GetTopology(i) != MeshTopology.Triangles) { reason = "topologie non triangulaire détectée ; mesh laissé intact."; return false; }

            MeshData data;
            if (!TryReadMeshData(source, out data, out reason)) return false;

            data.protectedVertices = BuildProtectedVertexMask(renderer, source, data, out protectedVertexCount);

            // Patch C : tout triangle contenant un vertex protégé protège ses 3 vertices
            // pour éviter qu'un triangle sensible ne soit fusionné/déformé.
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] idx = source.GetIndices(sub);
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    bool anyProtected =
                        data.protectedVertices[idx[t]] ||
                        data.protectedVertices[idx[t + 1]] ||
                        data.protectedVertices[idx[t + 2]];
                    if (!anyProtected) continue;
                    data.protectedVertices[idx[t]] = true;
                    data.protectedVertices[idx[t + 1]] = true;
                    data.protectedVertices[idx[t + 2]] = true;
                }
            }
            protectedVertexCount = data.protectedVertices.Count(v => v);

            int originalTriangles = CountTriangles(source);
            if (originalTriangles <= 0) { reason = "aucun triangle."; return false; }

            float extent = Mathf.Max(source.bounds.size.x, source.bounds.size.y, source.bounds.size.z);
            if (extent <= 0.000001f) { reason = "bounds dégénérés."; return false; }
            int maxGrid = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(source.vertexCount) * 1.5f), 16, 768);
            int desiredTriangles = Mathf.Max(1, Mathf.RoundToInt(originalTriangles * (1f - targetPercent / 100f)));

            ClusterProfile[] profiles =
            {
                new ClusterProfile(128, 24, true, "qualité / UV 1-128 / normales 24 / deux bones"),
                new ClusterProfile(64, 12, true, "équilibré / UV 1-64 / normales 12 / deux bones"),
                new ClusterProfile(32, 8, false, "optimisé / UV 1-32 / normales 8 / bone dominant"),
                new ClusterProfile(16, 4, false, "performance / UV 1-16 / normales 4 / bone dominant"),
                new ClusterProfile(8, 2, false, "agressif / UV 1-8 / normales 2 / bone dominant"),
                new ClusterProfile(4, 1, false, "très agressif / UV 1-4 / normales 1 / bone dominant")
            };

            int bestGrid = -1;
            int bestCount = originalTriangles;
            int bestDifference = int.MaxValue;
            ClusterProfile bestProfile = profiles[0];
            int bestProfileRank = int.MaxValue;
            bool reachedTargetWithCurrentQuality = false;

            for (int profileIndex = 0; profileIndex < profiles.Length; profileIndex++)
            {
                ClusterProfile profile = profiles[profileIndex];
                int low = 2;
                int high = maxGrid;
                int profileBestGrid = -1;
                int profileBestCount = originalTriangles;
                int profileBestDifference = int.MaxValue;
                for (int iteration = 0; iteration < 18 && low <= high; iteration++)
                {
                    int grid = (low + high) / 2;
                    int count = CountTrianglesAfterClustering(source, data, grid, profile);
                    int difference = Mathf.Abs(count - desiredTriangles);
                    if (difference < profileBestDifference)
                    {
                        profileBestDifference = difference;
                        profileBestGrid = grid;
                        profileBestCount = count;
                    }
                    if (count > desiredTriangles) high = grid - 1;
                    else low = grid + 1;
                }

                if (profileBestGrid >= 0)
                {
                    int sampleStart = Mathf.Max(2, profileBestGrid - 4);
                    int sampleEnd = Mathf.Min(maxGrid, profileBestGrid + 4);
                    for (int grid = sampleStart; grid <= sampleEnd; grid++)
                    {
                        int count = CountTrianglesAfterClustering(source, data, grid, profile);
                        int difference = Mathf.Abs(count - desiredTriangles);
                        if (difference < profileBestDifference)
                        {
                            profileBestDifference = difference;
                            profileBestGrid = grid;
                            profileBestCount = count;
                        }
                    }
                }

                if (profileBestGrid < 0) continue;

                if (profileBestCount <= desiredTriangles)
                {
                    bestGrid = profileBestGrid;
                    bestCount = profileBestCount;
                    bestDifference = profileBestDifference;
                    bestProfile = profile;
                    bestProfileRank = profileIndex;
                    reachedTargetWithCurrentQuality = true;
                    break;
                }

                if (profileBestDifference < bestDifference ||
                    (profileBestDifference == bestDifference && profileIndex < bestProfileRank))
                {
                    bestDifference = profileBestDifference;
                    bestGrid = profileBestGrid;
                    bestCount = profileBestCount;
                    bestProfile = profile;
                    bestProfileRank = profileIndex;
                }
            }

            if (bestGrid < 0 || bestCount >= originalTriangles)
            {
                reason = "aucune réduction sûre trouvée après les profils UV/skinning testés.";
                return false;
            }

            double estimatedReduction = 100.0 * (originalTriangles - bestCount) / Math.Max(1, originalTriangles);
            if (estimatedReduction < 5.0)
            {
                reason = "réduction potentielle inférieure à 5% : mesh conservé pour éviter de créer un asset presque identique.";
                return false;
            }

            ClusterBuild build = BuildClusters(source, data, bestGrid, bestProfile);
            if (build == null || build.triangleCount <= 0 || build.triangleCount >= originalTriangles)
            {
                reason = "aucune réduction exploitable après suppression des triangles dégénérés.";
                return false;
            }

            output = CreateReducedMesh(source, data, build);
            outputTriangles = CountTriangles(output);
            if (outputTriangles <= 0 || outputTriangles >= originalTriangles)
            {
                UnityEngine.Object.DestroyImmediate(output);
                output = null;
                reason = "validation finale échouée : le nombre de triangles n'a pas diminué.";
                return false;
            }
            profileLabel = bestProfile.label;
            return true;
        }

        private static bool[] BuildProtectedVertexMask(Renderer renderer, Mesh mesh, MeshData data,
            out int protectedCount)
        {
            var protectedVertices = new bool[mesh.vertexCount];
            int localProtectedCount = 0;
            Action<int> protect = index =>
            {
                if (index < 0 || index >= protectedVertices.Length || protectedVertices[index]) return;
                protectedVertices[index] = true;
                localProtectedCount++;
            };

            if (mesh.blendShapeCount > 0)
            {
                var deltaVertices = new Vector3[mesh.vertexCount];
                var deltaNormals = data.hasNormals ? new Vector3[mesh.vertexCount] : null;
                var deltaTangents = data.hasTangents ? new Vector3[mesh.vertexCount] : null;
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    int frameCount = mesh.GetBlendShapeFrameCount(shape);
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        mesh.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);
                        for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
                        {
                            bool moved = deltaVertices[vertex].sqrMagnitude > 1e-8f ||
                                (deltaNormals != null && deltaNormals[vertex].sqrMagnitude > 1e-6f) ||
                                (deltaTangents != null && deltaTangents[vertex].sqrMagnitude > 1e-6f);
                            if (moved) protect(vertex);
                        }
                    }
                }
            }

            SkinnedMeshRenderer skinnedRenderer = renderer as SkinnedMeshRenderer;
            Transform[] bones = skinnedRenderer && skinnedRenderer.bones != null ? skinnedRenderer.bones : Array.Empty<Transform>();
            if (bones.Length > 0 && data.skin != null && data.skin.HasWeights)
            {
                string[] sensitiveBoneTokens =
                {
                    "face", "head", "eye", "eyelid", "lash", "brow", "jaw", "mouth", "teeth", "tongue", "ear",
                    "hair", "bang", "fringe", "scalp", "cheveux", "frange", "visage", "yeux", "cils", "sourcil", "bouche", "tete"
                };
                for (int vertex = 0; vertex < data.skin.counts.Length; vertex++)
                {
                    int start = data.skin.starts[vertex];
                    int amount = data.skin.counts[vertex];
                    for (int i = 0; i < amount; i++)
                    {
                        BoneWeight1 weight = data.skin.weights[start + i];
                        if (weight.weight < 0.20f || weight.boneIndex < 0 || weight.boneIndex >= bones.Length || !bones[weight.boneIndex])
                            continue;
                        string boneName = bones[weight.boneIndex].name;
                        if (sensitiveBoneTokens.Any(token => boneName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            protect(vertex);
                            break;
                        }
                    }
                }
            }

            Material[] materials = renderer ? renderer.sharedMaterials : Array.Empty<Material>();
            string[] sensitiveMaterialTokens =
            {
                "face", "head", "skin", "body", "eye", "lash", "brow", "mouth", "teeth", "hair", "bang", "fringe",
                "scalp", "cheveux", "frange", "visage", "yeux", "cils", "sourcil", "bouche", "peau", "iris", "pupil"
            };
            int materialCount = Math.Min(materials != null ? materials.Length : 0, mesh.subMeshCount);
            for (int submesh = 0; submesh < materialCount; submesh++)
            {
                Material material = materials[submesh];
                if (!material || !sensitiveMaterialTokens.Any(token => material.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;
                int[] indices = mesh.GetIndices(submesh, true);
                foreach (int index in indices) protect(index);
            }

            protectedCount = localProtectedCount;
            return protectedVertices;
        }

        private static bool TryReadMeshData(Mesh mesh, out MeshData data, out string reason)
        {
            data = null;
            reason = null;
            int count = mesh.vertexCount;
            var result = new MeshData();
            result.vertices = mesh.vertices;
            result.normals = mesh.normals;
            result.tangents = mesh.tangents;
            result.colors = mesh.colors;
            result.hasNormals = result.normals != null && result.normals.Length == count;
            result.hasTangents = result.tangents != null && result.tangents.Length == count;
            result.hasColors = result.colors != null && result.colors.Length == count;
            for (int channel = 0; channel < UvChannelCount; channel++)
            {
                result.uvs[channel] = new List<Vector4>(count);
                mesh.GetUVs(channel, result.uvs[channel]);
                result.hasUv[channel] = result.uvs[channel].Count == count;
                if (!result.hasUv[channel]) result.uvs[channel].Clear();
            }

            result.skin = ReadSkinData(mesh);
            if (mesh.bindposes != null && mesh.bindposes.Length > 0 &&
                (result.skin == null || !result.skin.HasWeights || result.skin.maxInfluences == 0 || result.skin.counts.Any(value => value == 0)))
            {
                reason = "bindposes présents mais données de poids multi-bones absentes ou incohérentes.";
                return false;
            }
            if (result.skin != null && result.skin.HasWeights && (mesh.bindposes == null || mesh.bindposes.Length == 0))
            {
                reason = "poids de skinning présents sans bindposes.";
                return false;
            }
            if (result.skin != null && result.skin.HasWeights && result.skin.weights.Any(weight => weight.boneIndex < 0 || weight.boneIndex >= mesh.bindposes.Length))
            {
                reason = "un poids de skinning référence un index de bone hors bindposes.";
                return false;
            }
            if (result.vertices == null || result.vertices.Length != count)
            {
                reason = "impossible de lire les vertices.";
                return false;
            }
            data = result;
            return true;
        }

        internal static bool TryReadSkinData(Mesh mesh, out byte[] counts, out BoneWeight1[] weights)
        {
            SkinData data = ReadSkinData(mesh);
            counts = data != null ? data.counts : null;
            weights = data != null ? data.weights : null;
            return data != null && data.HasWeights;
        }

        private static SkinData ReadSkinData(Mesh mesh)
        {
            using (NativeArray<byte> perVertex = mesh.GetBonesPerVertex())
            using (NativeArray<BoneWeight1> all = mesh.GetAllBoneWeights())
            {
                if (perVertex.Length != mesh.vertexCount) return new SkinData();
                var data = new SkinData
                {
                    counts = new byte[perVertex.Length],
                    weights = new BoneWeight1[all.Length],
                    starts = new int[perVertex.Length]
                };
                int cursor = 0;
                for (int i = 0; i < perVertex.Length; i++)
                {
                    data.counts[i] = perVertex[i];
                    data.starts[i] = cursor;
                    data.maxInfluences = Math.Max(data.maxInfluences, perVertex[i]);
                    cursor += perVertex[i];
                }
                if (cursor != all.Length) return new SkinData();
                for (int i = 0; i < all.Length; i++) data.weights[i] = all[i];
                return data;
            }
        }

        private static int CountTrianglesAfterClustering(Mesh mesh, MeshData data, int grid, ClusterProfile profile)
        {
            int clusterCount;
            int[] map = BuildVertexMap(mesh, data, grid, profile, out clusterCount);
            var sums = new Vector3[clusterCount];
            var counts = new int[clusterCount];
            for (int i = 0; i < map.Length; i++) { sums[map[i]] += data.vertices[i]; counts[map[i]]++; }
            var positions = new Vector3[clusterCount];
            for (int i = 0; i < clusterCount; i++) positions[i] = sums[i] / Mathf.Max(1, counts[i]);
            int triangles = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] indices = mesh.GetIndices(sub);
                var seen = new HashSet<TriangleKey>();
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = map[indices[i]], b = map[indices[i + 1]], c = map[indices[i + 2]];
                    if (a == b || b == c || a == c) continue;
                    if (Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).sqrMagnitude < 1e-14f) continue;
                    if (seen.Add(new TriangleKey(a, b, c))) triangles++;
                }
            }
            return triangles;
        }

        private static int[] BuildVertexMap(Mesh mesh, MeshData data, int grid, ClusterProfile profile, out int clusterCount)
        {
            var map = new int[mesh.vertexCount];
            var lookup = new Dictionary<ClusterKey, int>(mesh.vertexCount);
            Vector3 min = mesh.bounds.min;
            float extent = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
            float cell = Mathf.Max(extent / Mathf.Max(1, grid), 0.0000001f);
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                ClusterKey key = MakeKey(data, i, min, cell, profile);
                int index;
                if (!lookup.TryGetValue(key, out index))
                {
                    index = lookup.Count;
                    lookup.Add(key, index);
                }
                map[i] = index;
            }
            clusterCount = lookup.Count;
            return map;
        }

        private static ClusterBuild BuildClusters(Mesh mesh, MeshData data, int grid, ClusterProfile profile)
        {
            var build = new ClusterBuild();
            build.map = new int[mesh.vertexCount];
            var lookup = new Dictionary<ClusterKey, int>(mesh.vertexCount);
            Vector3 min = mesh.bounds.min;
            float extent = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
            float cell = Mathf.Max(extent / Mathf.Max(1, grid), 0.0000001f);

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                ClusterKey key = MakeKey(data, i, min, cell, profile);
                int index;
                if (!lookup.TryGetValue(key, out index))
                {
                    index = build.vertices.Count;
                    lookup.Add(key, index);
                    build.vertices.Add(new VertexAccumulator());
                }
                build.map[i] = index;
                build.vertices[index].Add(data, i);
            }

            Vector3[] clusterPositions = build.vertices.Select(v => v.position / Mathf.Max(1, v.count)).ToArray();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] indices = mesh.GetIndices(sub);
                var seen = new HashSet<TriangleKey>();
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = build.map[indices[i]], b = build.map[indices[i + 1]], c = build.map[indices[i + 2]];
                    if (a == b || b == c || a == c) continue;
                    if (Vector3.Cross(clusterPositions[b] - clusterPositions[a], clusterPositions[c] - clusterPositions[a]).sqrMagnitude < 1e-14f) continue;
                    var triangle = new TriangleKey(a, b, c);
                    if (seen.Add(triangle)) build.triangleCount++;
                }
            }
            return build;
        }

        private static ClusterKey MakeKey(MeshData data, int vertex, Vector3 min, float cell, ClusterProfile profile)
        {
            Vector3 p = data.vertices[vertex];
            Vector4 uv0 = data.hasUv[0] ? data.uvs[0][vertex] : Vector4.zero;
            Vector4 uv1 = data.hasUv[1] ? data.uvs[1][vertex] : Vector4.zero;
            var key = new ClusterKey
            {
                x = Mathf.FloorToInt((p.x - min.x) / cell),
                y = Mathf.FloorToInt((p.y - min.y) / cell),
                z = Mathf.FloorToInt((p.z - min.z) / cell),
                u0x = data.hasUv[0] ? Mathf.FloorToInt(uv0.x * Mathf.Max(1, profile.uvBuckets)) : 0,
                u0y = data.hasUv[0] ? Mathf.FloorToInt(uv0.y * Mathf.Max(1, profile.uvBuckets)) : 0,
                u1x = data.hasUv[1] ? Mathf.FloorToInt(uv1.x * Mathf.Max(1, profile.uvBuckets)) : 0,
                u1y = data.hasUv[1] ? Mathf.FloorToInt(uv1.y * Mathf.Max(1, profile.uvBuckets)) : 0,
                n0 = data.hasNormals ? Mathf.RoundToInt((data.normals[vertex].normalized.x * 0.5f + 0.5f) * profile.normalBuckets) : 0,
                n1 = data.hasNormals ? Mathf.RoundToInt((data.normals[vertex].normalized.y * 0.5f + 0.5f) * profile.normalBuckets) : 0,
                n2 = data.hasNormals ? Mathf.RoundToInt((data.normals[vertex].normalized.z * 0.5f + 0.5f) * profile.normalBuckets) : 0,
                b0 = -1,
                b1 = -1,
                protectedVertex = data.protectedVertices != null && data.protectedVertices[vertex] ? vertex + 1 : 0
            };

            if (data.skin != null && data.skin.HasWeights)
            {
                int start = data.skin.starts[vertex];
                int amount = data.skin.counts[vertex];
                int dominantBone = -1;
                int secondaryBone = -1;
                float dominantWeight = -1f;
                float secondaryWeight = -1f;
                for (int i = 0; i < amount; i++)
                {
                    BoneWeight1 weight = data.skin.weights[start + i];
                    if (weight.weight > dominantWeight)
                    {
                        secondaryWeight = dominantWeight;
                        secondaryBone = dominantBone;
                        dominantWeight = weight.weight;
                        dominantBone = weight.boneIndex;
                    }
                    else if (weight.weight > secondaryWeight)
                    {
                        secondaryWeight = weight.weight;
                        secondaryBone = weight.boneIndex;
                    }
                }
                key.b0 = dominantBone;
                if (profile.preserveSecondaryBone && secondaryWeight >= SignificantSecondaryBoneWeight)
                    key.b1 = secondaryBone;
            }
            return key;
        }

        private static Mesh CreateReducedMesh(Mesh source, MeshData data, ClusterBuild build)
        {
            int count = build.vertices.Count;
            var positions = new Vector3[count];
            var normals = data.hasNormals ? new Vector3[count] : null;
            var tangents = data.hasTangents ? new Vector4[count] : null;
            var colors = data.hasColors ? new Color[count] : null;
            var uvOutput = new List<Vector4>[UvChannelCount];

            for (int channel = 0; channel < UvChannelCount; channel++)
                if (data.hasUv[channel]) uvOutput[channel] = new List<Vector4>(count);

            for (int i = 0; i < count; i++)
            {
                VertexAccumulator a = build.vertices[i];
                float inverse = 1f / Mathf.Max(1, a.count);
                positions[i] = a.position * inverse;
                if (data.hasNormals) normals[i] = a.normal.sqrMagnitude > 0.000001f ? a.normal.normalized : Vector3.up;
                if (data.hasTangents)
                {
                    Vector3 tangent = new Vector3(a.tangent.x, a.tangent.y, a.tangent.z);
                    tangent = tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.right;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, a.tangent.w < 0 ? -1 : 1);
                }
                if (data.hasColors) colors[i] = a.color * inverse;
                for (int channel = 0; channel < UvChannelCount; channel++)
                    if (data.hasUv[channel]) uvOutput[channel].Add(a.uvs[channel] * inverse);
            }

            var reduced = new Mesh { name = source.name + "_ZyumieDecimated" };
            reduced.indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            reduced.vertices = positions;
            if (data.hasNormals) reduced.normals = normals;
            if (data.hasTangents) reduced.tangents = tangents;
            if (data.hasColors) reduced.colors = colors;
            for (int channel = 0; channel < UvChannelCount; channel++)
                if (data.hasUv[channel]) reduced.SetUVs(channel, uvOutput[channel]);
            reduced.bindposes = source.bindposes;
            CopyBoneWeights(build.vertices, data.skin, reduced);

            reduced.subMeshCount = source.subMeshCount;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] indices = source.GetIndices(sub);
                var outputIndices = new List<int>(indices.Length);
                var seen = new HashSet<TriangleKey>();
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = build.map[indices[i]], b = build.map[indices[i + 1]], c = build.map[indices[i + 2]];
                    if (a == b || b == c || a == c) continue;
                    Vector3 area = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                    if (area.sqrMagnitude < 1e-14f) continue;
                    if (!seen.Add(new TriangleKey(a, b, c))) continue;
                    outputIndices.Add(a); outputIndices.Add(b); outputIndices.Add(c);
                }
                reduced.SetIndices(outputIndices.ToArray(), MeshTopology.Triangles, sub, false);
            }

            CopyBlendShapes(source, reduced, build);
            reduced.RecalculateBounds();
            return reduced;
        }

        // PATCH A : limite stricte à 4 influences par vertex (VRChat) + renormalisation.
        private static void CopyBoneWeights(List<VertexAccumulator> clusters, SkinData sourceSkin, Mesh output)
        {
            if (sourceSkin == null || !sourceSkin.HasWeights) return;

            const int MaxInfluences = 4;
            const float MinWeight = 0.005f;

            var counts = new byte[clusters.Count];
            var flattened = new List<BoneWeight1>(clusters.Count * MaxInfluences);

            for (int i = 0; i < clusters.Count; i++)
            {
                var ordered = clusters[i].boneWeights
                    .OrderByDescending(pair => pair.Value)
                    .Take(MaxInfluences)
                    .ToList();

                float total = ordered.Sum(pair => pair.Value);
                if (total <= 0.000001f) { counts[i] = 0; continue; }

                var finalList = ordered.Where(p => p.Value / total >= MinWeight).ToList();
                if (finalList.Count == 0) finalList.Add(ordered[0]);
                float finalTotal = finalList.Sum(p => p.Value);

                counts[i] = (byte)finalList.Count;
                foreach (var inf in finalList)
                    flattened.Add(new BoneWeight1 { boneIndex = inf.Key, weight = inf.Value / finalTotal });
            }

            var countArray = new NativeArray<byte>(counts, Allocator.Temp);
            var weightArray = new NativeArray<BoneWeight1>(flattened.ToArray(), Allocator.Temp);
            try { output.SetBoneWeights(countArray, weightArray); }
            finally { if (countArray.IsCreated) countArray.Dispose(); if (weightArray.IsCreated) weightArray.Dispose(); }
        }

        private static void CopyBlendShapes(Mesh source, Mesh target, ClusterBuild build)
        {
            int sourceCount = source.vertexCount;
            int targetCount = build.vertices.Count;
            bool hasNormals = source.normals != null && source.normals.Length == sourceCount;
            bool hasTangents = source.tangents != null && source.tangents.Length == sourceCount;
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string name = source.GetBlendShapeName(shape);
                int frameCount = source.GetBlendShapeFrameCount(shape);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    var deltaVertices = new Vector3[sourceCount];
                    var deltaNormals = hasNormals ? new Vector3[sourceCount] : null;
                    var deltaTangents = hasTangents ? new Vector3[sourceCount] : null;
                    source.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);

                    var outVertices = new Vector3[targetCount];
                    var outNormals = hasNormals ? new Vector3[targetCount] : null;
                    var outTangents = hasTangents ? new Vector3[targetCount] : null;
                    for (int i = 0; i < sourceCount; i++)
                    {
                        int mapped = build.map[i];
                        outVertices[mapped] += deltaVertices[i];
                        if (hasNormals) outNormals[mapped] += deltaNormals[i];
                        if (hasTangents) outTangents[mapped] += deltaTangents[i];
                    }
                    for (int i = 0; i < targetCount; i++)
                    {
                        float inverse = 1f / Mathf.Max(1, build.vertices[i].count);
                        outVertices[i] *= inverse;
                        if (hasNormals) outNormals[i] *= inverse;
                        if (hasTangents) outTangents[i] *= inverse;
                    }
                    target.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(shape, frame), outVertices, outNormals, outTangents);
                }
            }
        }

        internal static int CountTriangles(Mesh mesh)
        {
            if (!mesh) return 0;
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) count += (long)mesh.GetIndexCount(i) / 3L;
            return count > int.MaxValue ? int.MaxValue : (int)count;
        }

        private static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return string.IsNullOrEmpty(value) ? "AvatarMesh" : value.Replace('/', '_').Replace('\\', '_');
        }
    }

    internal static class AvatarMeshAssetUtility
    {
        public static string EnsureAvatarFolder(string avatarName, string childFolder)
        {
            EnsureFolder("Assets/ZyumieAvatarSuite");
            EnsureFolder("Assets/ZyumieAvatarSuite/Generated");
            string safeName = Sanitize(avatarName);
            string avatarFolder = "Assets/ZyumieAvatarSuite/Generated/" + safeName;
            EnsureFolder(avatarFolder);
            string output = avatarFolder + "/" + childFolder;
            EnsureFolder(output);
            return output;
        }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        public static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return string.IsNullOrEmpty(value) ? "Avatar" : value.Replace('/', '_').Replace('\\', '_');
        }
    }
}