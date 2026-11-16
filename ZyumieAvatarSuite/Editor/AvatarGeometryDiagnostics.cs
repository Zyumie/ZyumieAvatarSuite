using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    /// <summary>Read-only geometry audit for the optimizer and conservative mesh merger.</summary>
    internal static class AvatarGeometryDiagnostics
    {
        private sealed class MeshInfo
        {
            public SkinnedMeshRenderer renderer;
            public int triangles;
        }

        public static string Report(GameObject root)
        {
            if (!root) return "Sélectionne la racine de l'avatar.";
            var output = new StringBuilder();
            output.AppendLine("ZYUMIE AVATAR SUITE — AUDIT GÉOMÉTRIE v0.7.1");
            output.AppendLine("Avatar : " + root.name);

            var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r && r.sharedMesh)
                .Select(r => new MeshInfo { renderer = r, triangles = AvatarMeshOptimizer.CountTriangles(r.sharedMesh) })
                .OrderByDescending(info => info.triangles).ToList();

            output.AppendLine("Skinned Mesh Renderers : " + meshes.Count);
            output.AppendLine("Triangles Skinned cumulés : " + meshes.Sum(info => info.triangles).ToString("N0"));
            output.AppendLine("\nMeshes les plus lourds :");
            foreach (var info in meshes.Take(25))
            {
                var renderer = info.renderer;
                output.AppendLine("  - " + renderer.name + " | " + info.triangles.ToString("N0") +
                    " triangles | " + renderer.sharedMesh.vertexCount.ToString("N0") + " vertices | " +
                    renderer.sharedMesh.blendShapeCount + " BlendShapes | " +
                    (renderer.bones != null ? renderer.bones.Length : 0) + " bones | " +
                    (renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0) + " materials | " +
                    (renderer.sharedMesh.isReadable ? "Read/Write" : "non lisible"));
            }

            var remaining = meshes.Select(m => m.renderer).ToList();
            var groups = new List<List<SkinnedMeshRenderer>>();
            var animationAudit = new AvatarAnimationBindingAudit(root);
            while (remaining.Count > 0)
            {
                var first = remaining[0];
                remaining.RemoveAt(0);
                var group = new List<SkinnedMeshRenderer> { first };
                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    string reason;
                    if (AvatarSkinnedMeshMerger.CanMergePair(root, first, remaining[i], animationAudit, out reason))
                    {
                        group.Add(remaining[i]);
                        remaining.RemoveAt(i);
                    }
                }
                if (group.Count > 1) groups.Add(group);
            }

            output.AppendLine("\nGroupes fusionnables par l'outil automatique : " + groups.Count);
            if (groups.Count == 0)
                output.AppendLine("  Aucun groupe ne remplit tous les critères stricts de fusion.");
            else
            {
                int index = 1;
                foreach (var group in groups)
                {
                    output.AppendLine("  Groupe " + index++ + " : " + string.Join(" + ", group.Select(r => r.name)));
                    output.AppendLine("    Même transform monde, rootBone, ordre des bones, bindposes et réglages de rendu ; aucune courbe détectée sur les transforms propres ou les renderers ; matériaux/submeshes conservés.");
                }
            }

            int withBlendShapes = meshes.Count(m => m.renderer.sharedMesh.blendShapeCount > 0);
            output.AppendLine("\nMeshes avec BlendShapes : " + withBlendShapes);
            output.AppendLine("La fusion automatique exclut ces meshes, car les animations Animator/FX et les chemins des renderers nécessiteraient un remappage.");
            output.AppendLine("Elle exclut aussi les renderers dont les transforms propres ou le composant Renderer sont ciblés par une courbe d'animation trouvée dans les Animator/Playable Layers du Descriptor.");
            output.AppendLine("\nRéduction polygonale : vertex clustering avec conservation des UV, normales, tangentes, couleurs, poids multi-bones et frames BlendShapes.");
            output.AppendLine("Le pourcentage configuré est une cible, pas une garantie. L'outil affiche le taux réellement obtenu pour chaque mesh.");
            output.AppendLine("Les deux opérations doivent être lancées sur une copie ; les assets sources ne sont pas remplacés.");
            return output.ToString();
        }
    }
}
