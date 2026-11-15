using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    internal sealed class AvatarMetrics
    {
        public int triangles, vertices, skinnedMeshes, basicMeshes, materialSlots, bones;
        public int animators, lights, particleSystems, physBones, physBoneColliders;
        public int contacts, constraints, lineRenderers, trailRenderers, audioSources, missingScripts;
        public int blendShapes, uniqueTextures;
        public long estimatedTextureBytes;
        public Vector3 boundsSize;
    }

    internal static class AvatarAnalyzer
    {
        public static AvatarMetrics Analyze(GameObject root)
        {
            var m = new AvatarMetrics();
            if (!root) return m;
            var textures = new HashSet<Texture>();
            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer smr)
                {
                    m.skinnedMeshes++;
                    if (smr.sharedMesh)
                    {
                        m.triangles += CountTriangles(smr.sharedMesh);
                        m.vertices += smr.sharedMesh.vertexCount;
                        m.blendShapes += smr.sharedMesh.blendShapeCount;
                        m.bones += smr.bones != null ? smr.bones.Length : 0;
                    }
                }
                else if (renderer is MeshRenderer)
                {
                    m.basicMeshes++;
                    var mf = renderer.GetComponent<MeshFilter>();
                    if (mf && mf.sharedMesh)
                    {
                        m.triangles += CountTriangles(mf.sharedMesh);
                        m.vertices += mf.sharedMesh.vertexCount;
                    }
                }

                if (renderer.sharedMaterials != null)
                {
                    m.materialSlots += renderer.sharedMaterials.Length;
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (!mat) continue;
                        foreach (var property in mat.GetTexturePropertyNames())
                        {
                            var texture = mat.GetTexture(property);
                            if (texture) textures.Add(texture);
                        }
                    }
                }
                if (renderer.bounds.size.sqrMagnitude > 0.0001f)
                {
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }

            m.boundsSize = hasBounds ? bounds.size : Vector3.zero;
            m.uniqueTextures = textures.Count;
            foreach (var texture in textures)
                m.estimatedTextureBytes += (long)texture.width * texture.height * 4L;

            m.animators = root.GetComponentsInChildren<Animator>(true).Length;
            m.lights = root.GetComponentsInChildren<Light>(true).Length;
            m.particleSystems = root.GetComponentsInChildren<ParticleSystem>(true).Length;
            m.lineRenderers = root.GetComponentsInChildren<LineRenderer>(true).Length;
            m.trailRenderers = root.GetComponentsInChildren<TrailRenderer>(true).Length;
            m.audioSources = root.GetComponentsInChildren<AudioSource>(true).Length;

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (!component) continue;
                string type = component.GetType().Name;
                if (type == "VRCPhysBone") m.physBones++;
                else if (type == "VRCPhysBoneCollider") m.physBoneColliders++;
                else if (type == "VRCContactSender" || type == "VRCContactReceiver") m.contacts++;
                else if (type.Contains("Constraint")) m.constraints++;
            }
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                m.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            return m;
        }

        private static int CountTriangles(Mesh mesh)
        {
            int count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles)
                    count += (int)mesh.GetIndexCount(i) / 3;
            return count;
        }

        public static string Report(GameObject root, AvatarMetrics m)
        {
            var b = new System.Text.StringBuilder();
            b.AppendLine("ZYUMIE AVATAR SUITE — RAPPORT");
            b.AppendLine("Avatar : " + root.name);
            b.AppendLine("\nGéométrie");
            b.AppendLine("  Triangles : " + m.triangles.ToString("N0"));
            b.AppendLine("  Sommets : " + m.vertices.ToString("N0"));
            b.AppendLine("  Skinned Meshes : " + m.skinnedMeshes);
            b.AppendLine("  Basic Meshes : " + m.basicMeshes);
            b.AppendLine("  Material Slots : " + m.materialSlots);
            b.AppendLine("  Os référencés : " + m.bones);
            b.AppendLine("  BlendShapes : " + m.blendShapes);
            b.AppendLine("  Bounds estimées : " + m.boundsSize.ToString("F2"));
            b.AppendLine("\nComposants");
            b.AppendLine("  Animator : " + m.animators);
            b.AppendLine("  PhysBones : " + m.physBones);
            b.AppendLine("  PhysBone Colliders : " + m.physBoneColliders);
            b.AppendLine("  Contacts : " + m.contacts);
            b.AppendLine("  Contraintes : " + m.constraints);
            b.AppendLine("  Lights : " + m.lights);
            b.AppendLine("  Particle Systems : " + m.particleSystems);
            b.AppendLine("  Line / Trail Renderers : " + m.lineRenderers + " / " + m.trailRenderers);
            b.AppendLine("  Audio Sources : " + m.audioSources);
            b.AppendLine("  Missing Scripts : " + m.missingScripts);
            b.AppendLine("\nTextures");
            b.AppendLine("  Textures uniques : " + m.uniqueTextures);
            b.AppendLine("  Mémoire brute estimée RGBA32 : " + (m.estimatedTextureBytes / 1048576f).ToString("0.0") + " MB");
            b.AppendLine("\nRepères mobiles VRChat (indicatifs uniquement)");
            b.AppendLine("  Triangles : Excellent ≤ 7 500 | Good ≤ 10 000 | Medium ≤ 15 000 | Poor ≤ 20 000");
            b.AppendLine("  Skinned Meshes : 1 / 1 / 2 / 2");
            b.AppendLine("  Basic Meshes : 1 / 1 / 2 / 2");
            b.AppendLine("  Material Slots : 1 / 1 / 2 / 4");
            b.AppendLine("  PhysBones : 0 / 4 / 6 / 8");
            b.AppendLine("\nCes repères ne calculent pas le rang officiel VRChat. Utilise VRChat SDK Builder pour le résultat final.");
            b.AppendLine("La mémoire des textures est une estimation brute, pas la taille compressée finale.");
            b.AppendLine("La décimation polygonale automatique n'est pas incluse afin de préserver les BlendShapes et le skinning.");
            return b.ToString();
        }
    }
}
