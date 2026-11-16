# Zyumie Avatar Suite v0.7.1

**Unity Editor toolkit for VRChat avatar tracking setup, PC/Android variants, performance diagnostics and non-destructive optimization.**

> Version 0.7.1 is a development/test release. The source has not been compiled in a Unity project or validated in a live VRChat session as part of this archive. Back up your project and avatar before using generated tools.

## Requirements

- Unity **2022.3 LTS** (keep the Unity version required by the VRChat Avatars SDK).
- VRChat Avatars SDK **3.10.6 or newer** for native Unified Expressions Face Tracking.
- AV3 Demo Assets samples if the avatar has no custom FX or Gesture controller assigned.

## Installation

### As a local Unity package

1. Extract the complete `ZyumieAvatarSuite` folder from this archive.
2. In Unity, open **Window → Package Management → Package Manager**.
3. Press **+ → Add package from disk…** and select `ZyumieAvatarSuite/package.json`.
4. Wait for Unity to compile, then open **Zyumie → Avatar Suite**.

### Update an existing local copy

1. Back up the project, avatar prefab/scene, and any generated Animator assets.
2. Replace the package folder with the complete `ZyumieAvatarSuite` folder from this archive.
3. Re-open Unity and resolve any compile errors before running an operation.

## v0.7.1 changes

### Polygon reduction scope and false-protection fix

- Fixes an over-broad protection rule: the decimator no longer checks the imported `Mesh.name` for words such as `Skin`. Many unrelated FBX clothing meshes share a generic source-mesh name; using that name protected most of the avatar and left only one renderer eligible. Protection now uses the actual Renderer name and its parent names.
- The operation now processes both `SkinnedMeshRenderer` and static `MeshRenderer` + `MeshFilter` geometry, so its before/after triangle total covers both categories.
- Adds more reduction profiles (quality, balanced, optimized, performance, aggressive and very aggressive). More aggressive profiles relax UV/normal grouping and average skin weights while preserving dominant-bone grouping. This can cause visible texture/normal/skinning loss; review the copy carefully.
- BlendShape vertex protection ignores extremely small numerical noise and locks vertices only when position/normal/tangent deltas exceed a small significance threshold. This allows meshes with minor FBX delta noise to remain optimizable while meaningful expression deformation remains protected.
- Updates the geometry audit and UI version to 0.7.1.

**Important:** this fixes the false-positive protection shown by reports where clothing renderers were classified as `skin` because their imported Mesh asset had a generic name. It does not guarantee a sub-200,000 triangle avatar. Face/head/body/hair protection, UV islands, material boundaries and actual mesh topology still limit reduction. The most aggressive profiles are lossy; inspect face, hair, garments, UV textures and joints in Unity before upload. Unity compilation and avatar-specific visual validation are still required.

## v0.7.0 changes

### Quality-first polygon reduction

- Vertices moved by BlendShapes, significantly weighted to face/head/hair bones, or used by face/skin/hair material submeshes are individually protected from clustering. Renderers/meshes named for face/head/skin/body/eyes/mouth/hair/lashes/brows remain fully protected. The heuristic also checks sensitive parent names, so generic FBX renderer names are less likely to bypass protection. Review its skip report because automatic name-based recognition is not infallible.
- Non-protected meshes use finer UV buckets and quantized normal groups. Significant secondary bone influences remain part of the grouping key; the optimizer no longer relaxes down to only the dominant bone.
- Small meshes/accessories are skipped rather than aggressively collapsed. The target remains a per-mesh goal, not a guarantee for the complete avatar; the report lists every protected or skipped renderer.
- This remains a lossy spatial-clustering simplifier, not a QEM/edge-collapse decimator. Review garments, thin geometry, UV seams and joints after processing. A lower reduction with clean visuals is preferred over a high reduction that damages the avatar.

### Skinned Mesh, bone-weight and hierarchy optimization

- Adds a safe skinning pass that can keep a configurable maximum of 2, 4 or 8 influences per vertex (4 by default), drops tiny weights below the selected threshold, then normalizes the remaining weights in descending order.
- Removes unused entries from each `SkinnedMeshRenderer.bones` array and the matching `Mesh.bindposes` array. The remapped mesh is saved as a new asset.
- The optimizer never deletes, reparents or renames rig GameObjects/Transforms. This is deliberate: hierarchy transforms may still be referenced by Animator clips, constraints, contacts, PhysBones or custom scripts. The operation reduces renderer bone slots/weights, not the total rig hierarchy count.
- Retains the conservative SkinnedMeshRenderer merger. It still rejects BlendShape meshes and renderers whose paths/transforms are animated.

### Material Slots

- Adds consolidation of duplicate submeshes that use the exact same Material asset on one renderer. It combines their triangle indices into one submesh and reduces the renderer's matching material slots.
- Different Material assets are not merged even when their shader/properties appear similar. Renderers with MaterialPropertyBlocks, mismatched slot/submesh counts, unreadable meshes or non-triangle topology are skipped.

### PhysBones and Colliders

- Adds cleanup of null/duplicate explicit PhysBone collider references.
- Removes redundant child entries from `Ignore Transforms` when an ancestor is already ignored; ignored transforms already cover their descendants. It measures affected transforms but does not delete/reparent chain bones or silently change their motion.
- Reports estimated affected-transform counts, explicit collider references and collision checks.
- Can optionally remove a `VRCPhysBoneCollider` only when it is not referenced in any explicit PhysBone collider list and its serialized global-collision flag is readable and false. This optional step runs only when every PhysBone collider list was discovered; if the SDK serialized fields cannot be identified reliably, deletion is disabled. It warns that custom scripts may still reference components by name.
- Never deletes or reparents PhysBone chain transforms. Large chains are reported for manual review/splitting instead of being silently altered.

### Performance diagnostics

- Counts unique Transform references for the Bones estimate rather than simply summing each renderer's bone array.
- Adds estimated PhysBone affected-transform and collision-check counts. These are diagnostics only; the VRChat SDK Builder remains authoritative.
- Updates the PC and Android threshold reference to the current VRChat Performance Ranks documentation.

## v0.6.1 changes

### Polygon decimation fix

- Replaces the previous near-exact attribute clustering key, which often prevented vertices from grouping and produced negligible reductions.
- The v0.6.1 implementation used spatial vertex clustering with several UV/skinning preservation profiles. v0.7.0 replaces those profiles with finer UV and normal grouping and skips sensitive meshes by default.
- Chooses the tested profile/grid combination that comes closest to the requested triangle target. Reports the actual per-renderer reduction and the selected profile.
- Skips meshes when the estimated reduction is below 5% instead of claiming a successful optimization.
- Disposes Unity NativeArrays used to read multi-influence skin weights.
- This is still a lossy vertex-clustering decimator, not a feature-preserving QEM/edge-collapse decimator. Texture seams, silhouettes, fine accessories, skin deformation and BlendShape detail can be softened. Inspect the generated copy carefully.

## v0.6.0 changes

### FrameEyeOSC / native Face Tracking

- Keeps the avatar's Eye Look in **Bone Rotation** mode after FEO setup. FrameEyeOSC's native `/tracking/face/v1` `EyeLook*` values are designed to rotate the configured eye bones. **Both** additionally applies EyeLook BlendShapes and can over-rotate some avatars.
- BlendShape matching now supports namespaced/prefixed names by matching normalized suffixes. Examples include `Body: EyeBlinkLeft` and `Face: EyeBlinkRight`.
- Adds an explicit opt-in for `AFK_Mouse: Blink` (and equivalent normalized spellings) as a fallback for **both** `EyeClosedLeft` and `EyeClosedRight`. It is disabled by default and reported when used.
- A shared blink BlendShape produces a symmetric blink only. It cannot represent independent left/right winks.
- Reports `Body: vrc_Lookingup` / `Body: vrc_Lookingdown` candidates without assigning them automatically. The plugin does not stack these shapes with bone rotation.
- Preserves existing non-empty Face Tracking mappings and keeps the 16-mesh limit.

### FrameEyeOSC checklist and runtime boundary

In FrameEyeOSC, use **VRChat** as the output mode and enable **Output → Send to Face Tracking too** to send `/tracking/face/v1`. Enable OSC in VRChat and use an avatar whose Descriptor has Face Tracking and **Use All Tracking Keys** enabled.

The native message is one 102-byte OSC blob. FrameEyeOSC currently documents per-eye `EyeClosed`, `EyeWide`, `EyeLook`, and (when configured) `EyeSquint`, pupil and brow values. Other keys, including mouth keys, are sent as zero by this output mode. Unmapped keys do not affect BlendShapes, so mouth/gesture animations can remain driven by their existing Animator layers.

The Unity diagnostic prints the expected FrameEyeOSC settings and the VRChat status outputs to inspect:

- `/avatar/avatarfullfacetracking` — confirms that the currently loaded avatar advertises Face Tracking with Use All Tracking Keys.
- `/avatar/parameters/UserFaceTracking` — reports whether face tracking is allowed and a source is active.
- `/avatar/parameters/UserEyeTracking` — reports whether eye tracking data is available.

**Important:** Unity Editor cannot prove that FrameEyeOSC is currently transmitting or that VRChat has received a particular OSC packet. The diagnostic is read-only and gives a checklist; verify those outputs via VRChat OSCQuery/OSC Debug and test the avatar in VRChat.

### Animator / Avatar Mask diagnostics

- Audits FX/Gesture playable controllers, their layers, weights, blending modes, Avatar Masks and state counts.
- Flags a `lowarms`-named Animator layer when it is not first, but treats this as a review hint rather than proof of an error.
- Lists `lowarms`-named hierarchy objects and their sibling indices.
- Does not edit controllers or masks. An Avatar Mask on Gesture can be intentional; review it against the actual rig and clips.

### Geometry and optimization

- Provides a **quality-first polygon-reduction operation** using adaptive spatial vertex clustering, with a target slider from 5% to 80%. v0.7.0 protects vertices moved by BlendShapes, weighted to face/head/hair bones, or belonging to sensitive material submeshes; explicitly named face/skin/hair renderers remain untouched, while eligible geometry in mixed meshes can still be reduced.
- The decimator uses finer UV/normal groups and keeps dominant plus significant secondary bone boundaries. It reports actual before/after counts per mesh and lists protected/skipped renderers. The target is not guaranteed for the whole avatar.
- New mesh assets preserve submesh/material boundaries, UV channels, vertex colors, bindposes and skin weights on processed meshes. Spatial averaging is still lossy and can affect silhouette, UVs, normals or deformation; the original Mesh assets are never overwritten.
- Adds **automatic SkinnedMeshRenderer merging** for groups with the same world transform, rootBone, ordered bone array, bindposes, vertex-channel layout and renderer settings (including probes, lightmap state, property blocks and motion settings). Submeshes/material slots and arbitrary bone weights are concatenated into a new Mesh asset.
- Skinned Meshes with BlendShapes are deliberately excluded from automatic merging. Even where BlendShape data can be copied into one mesh, existing Animator/FX animation curves and VRChat avatar references target the original renderer paths. Remapping those references is not included, so merging those renderers would silently break facial/body animation.
- Merge candidates are checked using the same strict compatibility rules as the merge operation. Animator/Playable Layer clips are inspected; pairs are rejected when a clip animates one renderer component or a transform branch that would lose its relative motion. The empty GameObjects are retained to avoid breaking hierarchy paths. Custom scripts that change renderer references, object activation or transforms at runtime cannot be fully inspected by the clip audit and must still be reviewed manually.
- Retains the non-destructive texture workflow: it clones materials/textures on an avatar copy and applies selected resolution/compression settings.
- Retains the Android variant workflow that registers/reuses the Android override and optimizes textures on that Android copy.

**Important limitations:** vertex clustering is a geometry simplifier, not a topology-aware edge-collapse/QEM decimator. BlendShape-modified vertices, face/head/hair bone-weighted vertices and sensitive material submeshes are kept separate, but the heuristic is not a substitute for a visual check. Review every result in Unity, especially silhouette, joints, clothing seams and transparent materials. A target reduction is not a guarantee of a particular VRChat performance rank.

### PCVR / Android / Quest

- Retains separate Windows/PCVR and Android/Quest avatar-copy tools.
- Retains registration through the VRChat `PerPlatformOverrides` API and audits mobile shader names, mesh counts and triangle estimates.
- Adds an audit button for an existing Android override and an Android texture-optimization workflow.
- Does not convert desktop shaders, strip PhysBones/Contacts, or publish automatically. Use the VRChat SDK Builder to validate the Android rank and **Multi-Platform Build & Publish** to upload both variants.

**Steam Frame clarification:** when Steam Link streams the Windows VRChat client from a PC, the running VRChat session is still the Windows/PCVR platform. Creating an Android variant does not turn that streamed session into an Android session. The Android override is for Android/Quest builds.

## Existing features retained

- Native Unified Expressions Face Tracking configuration.
- Bone-based Eye Look fallback.
- `EyesTracking` toggle and safe `MainGestures` facial-layer toggle.
- Mouse-driven local eye/blink preview with value restoration.
- Avatar metrics and missing-script cleanup.
- Texture/material cloning and resolution/compression optimization.
- Android `PerPlatformOverrides` registration and audit.

## Safety and limitations

- Generated assets are written under `Assets/ZyumieAvatarSuite/Generated/`.
- Source materials and textures are not modified by the texture optimizer; the avatar copy receives duplicated materials and compatible texture assets.
- FEO duplicates FX/Gesture controllers before editing them and does not add a finger-neutralization pose.
- The symmetric blink fallback is opt-in and explicitly reported.
- Geometry and Animator diagnostics are read-only. Polygon reduction and renderer merging create new Mesh assets and are applied only to a duplicated avatar; the imported/source meshes are not overwritten.
- Renderer merging excludes BlendShape-bearing meshes because Animator/FX path remapping is not implemented. It also requires identical transforms, skeleton order, bindposes and rendering settings, and rejects detected clip bindings on those renderers, object activation, or their independent transform branches. Runtime changes made by custom scripts are not automatically detected.
- Polygon reduction locks BlendShape-modified vertices and vertices associated with sensitive face/head/hair bones or material submeshes; explicitly named sensitive renderers/meshes remain untouched. Other meshes still use lossy spatial clustering, so review UV seams, silhouettes, joints and clothing after processing.
- The plugin does not capture or decode live FrameEyeOSC/VRChat OSC packets.
- The archive has not been Unity-compiled or live-tested for this release. Test in a copy of your project with VRChat SDK Builder and VRChat Build & Test before publishing.

## Copyright / license

Copyright 2026 Zyumie. Licensed under the Apache License, Version 2.0. See `LICENSE`.

Third-party SDKs, assets and avatar content remain the property of their respective owners. This package is not affiliated with or endorsed by VRChat or Unity.


## References

- VRChat Performance Ranks: https://creators.vrchat.com/avatars/avatar-performance-ranking-system/
- VRChat PhysBones: https://creators.vrchat.com/common-components/physbones/
- Unity `Mesh.SetBoneWeights`: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.SetBoneWeights.html

## Validation status

This archive has been inspected statically and ZIP integrity is checked. It has not been compiled in Unity 2022.3 with the VRChat Avatars SDK, and no avatar-specific Play Mode or VRChat upload test has been run here. The PhysBone editor pass uses `SerializedObject` property discovery to avoid a hard compile-time dependency on the SDK's internal serialized fields; if a future SDK changes those field names, the PhysBone cleanup will skip the unrecognized list and report no change. Always compare the final avatar in the VRChat SDK Builder and test it in-game.
