# Zyumie Avatar Suite v0.3.9

Unity Editor tools for VRChat avatar diagnostics, non-destructive texture optimization, FEO tracking controls, a desktop eye/blink preview with explicit BlendShape selection, native eye-tracking diagnostics and PC/Android avatar variants.

## Requirements

- Unity **2022.3 LTS**.
- **VRChat SDK - Avatars 3.8.1 or newer**.
- VRChat **AV3 Demo Assets** samples if the avatar uses default FX or Gesture layers without custom controllers.

## Installation

1. Back up your Unity project.
2. Extract this package.
3. Open your VRChat avatar project in Unity.
4. Open **Window → Package Manager**.
5. Select **+ → Add package from disk…**.
6. Select this package's `package.json`.
7. Wait for Unity to compile.
8. Open **Zyumie → Avatar Suite**.

## FEO tracking setup

1. Select the avatar root (the GameObject containing `VRC Avatar Descriptor`).
2. Open **Zyumie → Avatar Suite → Tracking**.
3. Run **Configurer automatiquement Function > FEO**.
4. FEO duplicates the menu, Expression Parameters, FX and Gesture controllers before assigning generated copies to the Descriptor.
5. `EyesTracking` controls VRChat's native eye/eyelid tracking through `VRCAnimatorTrackingControl`.
6. `MainGestures` controls finger tracking/neutral finger pose. It does not disable facial expression animations.
7. Run **Diagnostiquer Eye Look / OSC / Animator** and inspect the generated report.

### Important: `UserEyeTracking`

`UserEyeTracking` is a VRChat built-in Animator parameter. FEO ensures a Bool declaration exists in the duplicated FX controller (default false); it is **not** added to the avatar's Expression Parameters asset and does not consume synchronized parameter budget.

The parameter indicates eye-tracking availability. It does **not** rotate eye bones, drive eyelid BlendShapes, translate OSC messages into avatar parameters, or guarantee the avatar will visibly move. Animator transitions/motions must use it if the avatar needs behavior conditional on tracking availability.

### Expression Parameters budget

FEO counts only entries whose `Network Synced` option is enabled: Bool = 1 bit; Int/Float = 8 bits. The limit is 256 synchronized bits. If the total exceeds the limit, FEO stops before assigning generated assets. It does not delete or desynchronize user parameters automatically.

### OSC notes

- Native eye tracking uses `/tracking/eye/EyesClosedAmount` for eyelid closure and an eye-look address such as `/tracking/eye/CenterVec` or `/tracking/eye/LeftRightPitchYaw`.
- `/tracking/face/v1` is a separate Unified Expressions face-tracking protocol.
- Seeing changing OSC values in VRChat's OSC Debug confirms reception, not that the avatar's Descriptor/Animator is configured to use those values.
- This package does not receive OSC directly. VRChat handles the OSC input.

## FEO Desktop Eye Simulator (v0.3.9)

Open **Zyumie → Avatar Suite → Tracking → Ouvrir le simulateur des yeux à la souris**.

- Select the avatar root. The simulator reads Left Eye / Right Eye from the Descriptor; common bone names are fallback only.
- Start the simulation, then move or drag the mouse over the control pad.
- Horizontal/vertical amplitude can be adjusted.
- **Regard neutre** centers the gaze.
- The simulator lists likely blink BlendShapes with renderer name, exact BlendShape name and index. Select **one** candidate at a time, then use the slider or **Cligner**. This avoids driving every shape containing `eye` or `close` simultaneously.
- The candidate search includes common English names and a few Japanese eyelid terms. It is a heuristic; verify the selected shape visually.
- Stop the simulation or close the window to restore captured eye rotations and the selected BlendShape weight.

**Limitations:** this is an Editor-only preview. It temporarily changes eye Transform local rotations and one selected BlendShape weight. It does not emulate VRChat's runtime tracking pipeline, FrameEyeOSC, OSC reception, Animator Tracking Control, or actual avatar upload behavior. If the eyes still do not move, the Descriptor eye references may not be the transforms that actually rotate the visible eyeballs, or another preview/animation system may overwrite them. If no candidate BlendShape closes the eyelids, the avatar may use different names or a different mechanism; select the correct shape manually in the model/import configuration before relying on it.

## Diagnostic report

The tracking diagnostic reports:
- Eye Look enabled state and exact Left Eye / Right Eye hierarchy paths.
- FX controller name and presence/type/default value of `UserEyeTracking` and `EyesTracking`.
- synchronized Expression Parameters bit cost.
- eyelid BlendShape candidate names and exact indices.
- expected native OSC addresses and the limits of what can be diagnosed from Unity.

The diagnostic does not listen to VRChat OSC traffic and does not prove that the Animator visibly moves the avatar.

## Cross-platform avatars

- Requires VRChat SDK - Avatars 3.8.1+.
- The Cross-platform action keeps the selected avatar as the PC base, creates/reuses an Android variant and registers it using the SDK's `PerPlatformOverrides` API.
- Verify both variants in the SDK Builder and use **Multi-Platform Build & Publish**. The tool does not upload avatars automatically.

## Safety

- Source menus, Expression Parameters and controllers are duplicated before FEO assignment.
- The source assets are not overwritten.
- The simulator restores its captured values when stopped or closed.
- Back up the Unity project before running avatar modifications.
- No Unity compilation or VRChat runtime test is claimed by this package build.

## License and copyright

Copyright 2026 Zyumie. Licensed under the Apache License, Version 2.0. See the included `LICENSE` file.

Third-party SDKs, assets and avatar content remain the property of their respective owners. This package is not affiliated with or endorsed by VRChat or Unity.
