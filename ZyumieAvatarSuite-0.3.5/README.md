# Zyumie Avatar Suite v0.3.5

Unity Editor tools for VRChat avatar analysis, non-destructive texture optimization, FEO tracking controls and platform-copy preparation.

## Requirements

- Unity **2022.3 LTS** (use the Unity version supported by your installed VRChat SDK).
- **VRChat SDK - Avatars** installed in the Unity project.
- The VRChat **AV3 Demo Assets** samples imported if the avatar uses the default FX or Gesture playable layer and has no custom controller assigned.

## Installation

1. Make a backup of your Unity project.
2. Extract the `ZyumieAvatarSuite-0.3.5` folder into a location on your computer.
3. Open the Unity project that contains your VRChat avatar.
4. In Unity, open **Window → Package Manager**.
5. Click **+ → Add package from disk…**.
6. Select `ZyumieAvatarSuite-0.3.5/package.json`.
7. Wait for Unity to finish importing and compiling the package.
8. Open **Zyumie → Avatar Suite** from the Unity menu.

## FEO setup

1. Select the root GameObject of your avatar in the Hierarchy.
2. Open **Zyumie → Avatar Suite → Tracking**.
3. Click **Configurer automatiquement Function > FEO**.
4. The tool creates or updates the avatar Expressions Menu path **Function → FEO** with the toggles `EyesTracking` and `MainGestures`.
5. Check the generated assets under `Assets/ZyumieAvatarSuite/Generated/FEO/`.
6. Test both toggles using **VRChat SDK → Build & Test** before uploading.

### Toggle behavior

- `EyesTracking` ON restores VRChat eye and eyelid tracking. OFF switches the avatar eye tracking behavior to Animation through `VRCAnimatorTrackingControl`.
- `MainGestures` ON restores finger tracking. OFF switches both finger groups to Animation and uses the generated masked neutral finger pose layer.
- The menu does not stop the FrameEyeOSC application or disable VRChat's OSC receiver. FrameEyeOSC remains an external OSC sender.
- Eye tracking and eyelid tracking are coupled by VRChat's `trackingEyes` control; disabling it also disables native eyelid tracking.

The avatar should be in its intended neutral finger pose before FEO setup, because the generated neutral finger clip captures the current local finger rotations.

### Safety notes

- The tool duplicates the Expressions Menu, Expression Parameters and playable-layer Animator Controllers before adding FEO controls.
- Existing source assets are kept. The generated copies are assigned to the avatar Descriptor after generation.
- An `AnimatorOverrideController` is not modified automatically; setup stops with an explanatory error to avoid discarding overrides.
- If a default FX or Gesture controller is used, the corresponding AV3 sample controller must be present. The tool stops rather than assigning an empty controller when the sample is missing.
- Review the generated FX/Gesture controller and verify facial expressions, visemes, eyelids, PhysBones and Contacts in Build & Test.

## Platform copies

In the **Plateformes** tab:

- **Windows / PCVR** creates a Windows-target copy (also applicable to Steam Frame PC streaming).
- **Android / Quest / Steam Frame** creates an Android-target copy (for Quest and Steam Frame standalone).
- **Créer le pack CROSS-PLATFORM (Windows + Android)** creates both copies from the selected source avatar.

Windows and Android are separate build targets, not one combined binary. Validate each target separately with VRChat SDK Builder. For platform variants of one avatar, use the same VRChat Blueprint ID and the SDK's platform override workflow.

## Other tools

- Avatar performance report (triangles, vertices, renderers, materials, bones, BlendShapes, PhysBones, Contacts and related components).
- Texture optimization on a scene copy, using duplicated materials and supported imported textures.
- Eye Look / FrameEyeOSC diagnostic report.
- Missing Script cleanup with Unity Undo support.

Automatic polygon decimation is not included. Generic mesh decimation can damage blendshapes, skin weights, clothing, eyelids and visemes.

## Copyright and license

**Copyright © 2026 Zyumie.**

Zyumie Avatar Suite is distributed under the **Apache License, Version 2.0**. See the included `LICENSE` file for the complete license terms. Unity and VRChat SDK components remain the property of their respective owners and are subject to their own licenses and terms.
