using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    internal sealed class AvatarMetrics
    {
        public int triangles, vertices, skinnedMeshes, basicMeshes, materialSlots, bones;
        public int animators, lights, particleSystems, physBones, physBoneColliders;
        public int physBoneAffectedTransforms, physBoneCollisionCheckCount;
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
            var uniqueBoneTransforms = new HashSet<Transform>();

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
                        if (smr.bones != null)
                            foreach (Transform bone in smr.bones) if (bone) uniqueBoneTransforms.Add(bone);
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
            m.bones = uniqueBoneTransforms.Count;
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
            AvatarAdvancedOptimizer.MeasurePhysBoneStats(root, out int physBoneCount, out int physBoneColliderCount,
                out m.physBoneAffectedTransforms, out m.physBoneCollisionCheckCount);
            m.physBones = physBoneCount;
            m.physBoneColliders = physBoneColliderCount;
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
            b.AppendLine("  PhysBones Affected Transforms (estimation) : " + m.physBoneAffectedTransforms);
            b.AppendLine("  PhysBone Colliders : " + m.physBoneColliders);
            b.AppendLine("  PhysBone Collision Check Count (estimation) : " + m.physBoneCollisionCheckCount);
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
            b.AppendLine("\nRepères VRChat officiels (Excellent / Good / Medium / Poor ; données vérifiées le 16/11/2026)");
            b.AppendLine("  PC — Triangles : 32 000 / 70 000 / 70 000 / 70 000");
            b.AppendLine("  PC — Skinned Meshes : 1 / 2 / 8 / 16 ; Material Slots : 4 / 8 / 16 / 32 ; Bones : 75 / 150 / 256 / 400");
            b.AppendLine("  PC — PhysBones : 4 / 8 / 16 / 32 ; Affected Transforms : 16 / 64 / 128 / 256 ; Colliders : 4 / 8 / 16 / 32 ; Collision Checks : 32 / 128 / 256 / 512");
            b.AppendLine("  Android — Triangles : 7 500 / 10 000 / 15 000 / 20 000");
            b.AppendLine("  Android — Skinned Meshes : 1 / 1 / 2 / 2 ; Material Slots : 1 / 1 / 2 / 4 ; Bones : 75 / 90 / 150 / 150");
            b.AppendLine("  Android — PhysBones : 0 / 4 / 6 / 8 ; Affected Transforms : 0 / 16 / 32 / 64 ; Colliders : 0 / 4 / 8 / 16 ; Collision Checks : 0 / 16 / 32 / 64");
            b.AppendLine("\nCes repères ne calculent pas le rang officiel VRChat : le rang global suit la statistique la plus défavorable. Utilise VRChat SDK Builder pour le résultat final.");
            b.AppendLine("La mémoire des textures est une estimation brute, pas la taille compressée finale.");
            b.AppendLine("Les bones sont comptés ici comme des Transform uniques référencés par les SkinnedMeshRenderer. Les counts PhysBone sont estimés depuis Root Transform / Ignore Transforms et les listes Colliders explicites.");
            b.AppendLine("Les opérations de réduction polygonale et de fusion se trouvent dans l’onglet Optimisation et travaillent sur une copie ; vérifie toujours le résultat et les rangs dans VRChat SDK Builder.");
            return b.ToString();
        }
    }
}
