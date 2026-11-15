# Zyumie Avatar Suite v0.4.1 (FIX2)

Unity Editor tools for VRChat avatar diagnostics, native Unified Expressions face tracking setup, gesture-driven facial expression toggling, texture optimization and PC/Android avatar overrides.

## Requirements

- Unity **2022.3 LTS**. Do not upgrade this project to Unity 6 for this package.
- VRChat SDK - Avatars **3.10.6 or newer** for native Face Tracking support.
- AV3 Demo Assets samples if the avatar has no custom FX or Gesture controller assigned.

## Installation / update

This archive is a **patch overlay**, not a full replacement of every file in the repository. It contains the modified scripts plus the newly added platform builder.

1. Back up the Unity project and the avatar prefab/scene.
2. In the project, open the existing `ZyumieAvatarSuite-0.3.9` package folder.
3. Replace `Editor/FEOAutoIntegration.cs`, `Editor/AvatarOperations.cs` and `Editor/ZyumieAvatarSuiteWindow.cs` with the files from this patch.
4. Add the new `Editor/AvatarPlatformBuilder.cs` file.
5. Replace the package's `package.json` and `README.md` with the versions in this patch. Keep the existing `AvatarMetrics.cs`, `AvatarTextureOptimizer.cs` and `FEOMouseEyeSimulator.cs` files.
6. Wait for Unity to compile, then open **Zyumie → Avatar Suite**.

## Tracking / FEO

1. Select the avatar root containing the VRC Avatar Descriptor.
2. Open **Zyumie → Avatar Suite → Tracking**.
3. Run **Configurer automatiquement Function > FEO**.
4. The tool checks the current SDK's native Face Tracking serialized fields, maps recognised Unified Expressions BlendShapes and preserves existing non-empty mappings.
5. If no compatible BlendShape is found, the tool no longer aborts the entire FEO setup. It leaves native Face Tracking disabled, configures Eye Look in **Bone Rotation** mode with the avatar's Left Eye / Right Eye bones, and prints the available BlendShape names in the report for manual mapping. This fallback can still use legacy `/tracking/eye/*` OSC input; it does not animate facial expressions through `/tracking/face/v1`.
6. When compatible mappings are found, Eye Gaze Mode becomes **Both** when EyeLook mappings exist, and Eyelids Type becomes **Face Tracking** only when both left and right blink mappings are found.
7. `EyesTracking` controls VRChat's `VRCAnimatorTrackingControl` eye tracking behavior. OFF returns control to avatar animations; ON allows VRChat tracking.

### OSC / FrameEyeOSC

Native Unified Expressions face tracking uses:

- Address: `/tracking/face/v1`
- One OSC blob argument (`b`)
- Exactly **102 bytes** in the official Unified Expressions key order
- VRChat OSC must be enabled, and the avatar must have Face Tracking + Use All Tracking Keys enabled

The legacy `/tracking/eye/*` addresses are not the same as `/tracking/face/v1`. Seeing `/tracking/eye/CenterVec` or `/tracking/eye/EyesClosedAmount` in OSC Debug does not prove that FrameEyeOSC is sending the native 102-byte face tracking blob. The avatar must also have matching Blendshapes Set mappings for the values it should display.

Useful read-only status outputs:

- `/avatar/avatarfullfacetracking` — whether the current avatar advertises full native face tracking support
- `/avatar/parameters/UserEyeTracking` — whether eye tracking is available to the local user
- `/avatar/parameters/UserFaceTracking` — whether a face tracking source is active and allowed

`UserEyeTracking` and `UserFaceTracking` are built-in VRChat Animator parameters. FEO does not add them to Expression Parameters and does not claim that adding their names alone enables tracking.

### MainGestures toggle

The old v0.3.9 implementation created a Gesture playable layer with a neutral finger-pose animation. That could freeze the hands/fingers. Version 0.4.1 removes that legacy layer from the duplicated Gesture controller and does not add any finger pose override.

`MainGestures` is added only when the FX controller contains a non-base layer that both:

- has transition conditions using `GestureLeft`, `GestureRight`, `GestureLeftWeight` or `GestureRightWeight`; and
- contains animation curves for SkinnedMeshRenderer BlendShapes.

The new toggle uses `VRCAnimatorLayerControl` to set those detected FX layer weights to 0 or 1. It does not modify the Gesture playable layer, so normal hand/finger tracking remains enabled.

Limit: the tool mutes the entire detected FX layer. If one layer combines gesture facial expressions with other facial animations, those other animations are also muted while MainGestures is OFF. Review the generated report and test both states in VRChat Build & Test. If no suitable layer is detected, the button is intentionally omitted rather than presenting a non-functional toggle.

## Cross-platform PC + Android / Quest

1. Select the PC avatar as the base avatar.
2. Open **Zyumie → Avatar Suite → Plateformes**.
3. Click **Créer / mettre à jour le pack CROSS-PLATFORM**.
4. The tool creates or reuses a separate `_Android_Quest` avatar in the same scene and registers it as the Android `PerPlatformOverrides` variant on the PC base avatar.
5. The tool audits shader names and reports mesh/triangle estimates. This is a warning-oriented audit, not a substitute for VRChat's Android Builder validation.
6. Optimise the Android copy manually: replace shaders with Android-compatible shaders, reduce texture sizes/material cost and check PhysBones/contacts/performance in the SDK Builder.
7. In **VRChat SDK → Builder**, select the PC/base avatar and use **Multi-Platform Build & Publish**.

Important: registering an override does not upload it. A regular PC **Build & Publish** does not publish the Android version. VRChat's Multi-Platform Build & Publish uploads the platform variants under the same Avatar ID. If Quest still reports Windows-only features, verify that the Android override is present and that the multi-platform upload completed successfully.

The Android variant receives the same Expressions Menu and Expression Parameters asset references as the PC base so the parameter order and menu controls remain aligned. It keeps its own Animator controller slots; if you configured FEO on PC after an older Android copy was created, run FEO on that Android copy too. The tool does not automatically convert PC shaders, remove components, optimise meshes or publish avatars. It deliberately leaves avatar content intact rather than deleting components that may be used by PhysBones, Contacts or custom systems.

## Safety / limitations

- Source FX/Gesture controllers, expression menus and parameter assets are duplicated before FEO assignment.
- The previous FEO neutral-finger layer is removed only from the generated Gesture controller copy.
- Face Tracking mappings are filled only where the first set has empty slots; existing non-empty mapping values are preserved.
- No-match BlendShape detection is a supported fallback: FEO keeps Eye Look bone rotation configured and reports candidates instead of throwing an exception.
- A maximum of 16 Face Tracking meshes is respected.
- Cross-platform copies and overrides are created locally; no upload is initiated.
- The tools have not been compiled inside Unity or tested on a live VRChat avatar as part of this source patch. Back up the project and use Build & Test before publishing.

## License / copyright

Copyright 2026 Zyumie. Licensed under the Apache License, Version 2.0. See the included `LICENSE` file from the repository root.

Third-party SDKs, assets and avatar content remain the property of their respective owners. This package is not affiliated with or endorsed by VRChat or Unity.
