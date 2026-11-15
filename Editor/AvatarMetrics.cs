using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Zyumie.AvatarSuite
{
    internal sealed class AvatarMetrics
    {
        public int triangles, skinnedMeshes, basicMeshes, materialSlots, bones;
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

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var textureSet = new HashSet<Texture>();
            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (var renderer in renderers)
            {
                if (renderer is SkinnedMeshRenderer smr)
                {
                    m.skinnedMeshes++;
                    if (smr.sharedMesh)
                    {
                        m.triangles += CountTriangles(smr.sharedMesh);
                        m.blendShapes += smr.sharedMesh.blendShapeCount;
                        m.bones += smr.bones != null ? smr.bones.Length : 0;
                    }
                }
                else if (renderer is MeshRenderer)
                {
                    m.basicMeshes++;
                    var mf = renderer.GetComponent<MeshFilter>();
                    if (mf && mf.sharedMesh) m.triangles += CountTriangles(mf.sharedMesh);
                }

                if (renderer.sharedMaterials != null)
                {
                    m.materialSlots += renderer.sharedMaterials.Length;
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (!mat) continue;
                        foreach (var prop in mat.GetTexturePropertyNames())
                        {
                            var tex = mat.GetTexture(prop);
                            if (tex) textureSet.Add(tex);
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
            m.uniqueTextures = textureSet.Count;
            foreach (var tex in textureSet)
                m.estimatedTextureBytes += (long)tex.width * tex.height * 4L;

            m.animators = root.GetComponentsInChildren<Animator>(true).Length;
            m.lights = root.GetComponentsInChildren<Light>(true).Length;
            m.particleSystems = root.GetComponentsInChildren<ParticleSystem>(true).Length;
            m.lineRenderers = root.GetComponentsInChildren<LineRenderer>(true).Length;
            m.trailRenderers = root.GetComponentsInChildren<TrailRenderer>(true).Length;
            m.audioSources = root.GetComponentsInChildren<AudioSource>(true).Length;

            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (!c) continue;
                string type = c.GetType().Name;
                if (type == "VRCPhysBone") m.physBones++;
                else if (type == "VRCPhysBoneCollider") m.physBoneColliders++;
                else if (type == "VRCContactSender" || type == "VRCContactReceiver") m.contacts++;
                else if (type.Contains("Constraint")) m.constraints++;
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                m.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);

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
            b.AppendLine("");
            b.AppendLine("Géométrie");
            b.AppendLine("  Triangles : " + m.triangles.ToString("N0"));
            b.AppendLine("  Skinned Meshes : " + m.skinnedMeshes);
            b.AppendLine("  Basic Meshes : " + m.basicMeshes);
            b.AppendLine("  Material Slots : " + m.materialSlots);
            b.AppendLine("  Os référencés : " + m.bones);
            b.AppendLine("  BlendShapes : " + m.blendShapes);
            b.AppendLine("  Bounds estimées : " + m.boundsSize.ToString("F2"));
            b.AppendLine("");
            b.AppendLine("Composants");
            b.AppendLine("  Animator : " + m.animators);
            b.AppendLine("  PhysBones : " + m.physBones);
            b.AppendLine("  PhysBone Colliders : " + m.physBoneColliders);
            b.AppendLine("  Contacts : " + m.contacts);
            b.AppendLine("  Contraintes détectées : " + m.constraints);
            b.AppendLine("  Lights : " + m.lights);
            b.AppendLine("  Particle Systems : " + m.particleSystems);
            b.AppendLine("  Line / Trail Renderers : " + m.lineRenderers + " / " + m.trailRenderers);
            b.AppendLine("  Audio Sources : " + m.audioSources);
            b.AppendLine("  Missing Scripts : " + m.missingScripts);
            b.AppendLine("");
            b.AppendLine("Textures");
            b.AppendLine("  Textures uniques : " + m.uniqueTextures);
            b.AppendLine("  Mémoire brute estimée RGBA32 : " + (m.estimatedTextureBytes / 1048576f).ToString("0.0") + " MB");
            b.AppendLine("");
            b.AppendLine("Repères VRChat Mobile (Android / Quest)");
            b.AppendLine("  Triangles : Excellent ≤ 7 500 | Good ≤ 10 000 | Medium ≤ 15 000 | Poor ≤ 20 000");
            b.AppendLine("  Skinned Meshes : 1 / 1 / 2 / 2");
            b.AppendLine("  Basic Meshes : 1 / 1 / 2 / 2");
            b.AppendLine("  Material Slots : 1 / 1 / 2 / 4");
            b.AppendLine("  PhysBones : 0 / 4 / 6 / 8");
            b.AppendLine("  Os : 75 / 90 / 150 / 150");
            b.AppendLine("  Particle Systems : 0 / 0 / 0 / 2");
            b.AppendLine("");
            b.AppendLine("Ces repères ne constituent PAS un calcul officiel du rang VRChat. Seul le SDK VRChat Builder donne le résultat final.");
            b.AppendLine("La mémoire des textures est une estimation brute, pas la taille compressée de l'upload.");
            return b.ToString();
        }
    }
}
