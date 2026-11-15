# 💜 Zyumie Avatar Suite

**An all-in-one Unity Editor toolkit for VRChat avatar optimization, tracking configuration and cross-platform preparation.**

Zyumie Avatar Suite aims to simplify VRChat avatar development through automated diagnostics, non-destructive optimization, tracking tools and platform preparation.

Our goal is to make avatars easier to prepare for **PCVR, Meta Quest and Steam Frame**, while preserving their appearance, expressions and tracking capabilities.

<details>
<summary>🚧 Project Status</summary>

**Work in Progress — v0.4.1**

Zyumie Avatar Suite is actively under development.

Tracking integration, facial expression mapping and cross-platform compatibility are currently undergoing testing. Features and compatibility may change between releases.

</details>

---

## ✨ Core Features

| Module | Description |
|---|---|
| 🔧 Avatar Optimizer | Analyze avatar performance and optimize copied materials and textures. |
| 🌐 Cross-Platform | Create PC and Android avatar variants and configure platform overrides. |
| 👁️ Eye Tracking | Configure VRChat Eye Look and Bone Rotation fallback. |
| 👄 Face Tracking | Detect BlendShapes and assist with native VRChat Unified Expressions mapping. |
| 🎭 Gesture Controls | Configure facial FX layers associated with hand gestures. |
| 📊 Diagnostics | Inspect tracking configuration, Expression Parameters and avatar compatibility. |

### 🎯 Supported Platforms

- 🖥️ **PCVR / Windows** — Full desktop avatar configuration.
- 📱 **Meta Quest** — Android avatar variant preparation.
- 🎮 **Steam Frame** — Android platform preparation and tracking integration where supported.

Platform compatibility depends on avatar assets, shaders, SDK support and device limitations.

---

## 👁️ Tracking & FEO

The **FEO (Facial & Eye Operations)** module is available through:

`Zyumie → Avatar Suite → Tracking`

The automatic setup creates copies of relevant avatar assets and adds the FEO controls to:

`Expressions → Function → FEO`

### Eye Tracking

FEO can configure VRChat's native Eye Look using the avatar's Left Eye and Right Eye references.

When native facial tracking mappings are unavailable, the avatar can use **Bone Rotation** as an Eye Look fallback.

External OSC applications such as FrameEyeOSC may provide eye tracking data through VRChat's OSC interface.

Supported tracking paths include:

- `/tracking/eye/*` — Eye tracking and eyelid data.
- `/tracking/face/v1` — Native Unified Expressions face tracking.

Receiving OSC data does not automatically guarantee that the avatar is configured to animate its eyes or face.

### Native Face Tracking

FEO can inspect available SkinnedMeshRenderers and BlendShapes to assist with VRChat Unified Expressions configuration.

**Important:** Native Face Tracking requires compatible BlendShapes and valid mappings to tracking keys. Automatic detection is not guaranteed for every avatar.

When no compatible BlendShapes are found, FEO keeps the native Face Tracking setup inactive rather than assigning incorrect mappings.

Manual mapping may be necessary for:

- Eye gaze
- Eyelid closure
- Facial expressions
- Mouth and jaw movements
- Other Unified Expressions tracking keys

### MainGestures

The `MainGestures` option is designed to control facial FX layers driven by hand gesture parameters.

The previous finger-neutralization system has been removed so that normal hand and finger tracking can remain active.

**Known limitation:** FEO currently identifies and controls entire FX layers. A layer mixing gesture expressions with blinking or other facial animations may also have those animations disabled. This behavior is still being tested and refined.

---

## 🌐 Cross-Platform Preparation

Zyumie Avatar Suite can create a separate Android variant while keeping the selected avatar as the PC base.

The platform setup uses VRChat's `PerPlatformOverrides` API to associate the Android avatar with the PC avatar.

The workflow includes:

- Creating or reusing an Android avatar variant.
- Registering the Android platform override.
- Preserving the original PC avatar.
- Inspecting platform-specific compatibility.

**Important:** Registering an Android override does not automatically convert every asset into a Quest-compatible asset.

Always verify shaders, materials, textures, meshes, PhysBones, Contacts and performance in the VRChat SDK Builder.

Use **Multi-Platform Build & Publish** to publish the PC and Android variants under the same Avatar ID.

---

## 📊 Diagnostics & Optimization

The toolkit includes:

- Avatar performance analysis.
- Expression Parameters budget inspection.
- Eye Look and Face Tracking diagnostics.
- BlendShape discovery and reporting.
- FX Animator inspection.
- Missing Script detection and removal.
- Non-destructive material and texture optimization.

Optimization works on a copy of the avatar and creates copied materials and compatible texture assets. Original source assets are intended to remain unchanged.

No automatic removal of Colliders, Rigidbody, PhysBones or Contacts is performed.

---

## 🧰 Requirements

- **Unity 2022.3 LTS**
- **VRChat SDK - Avatars**
- VRChat SDK **3.10.6 or newer** for the current native Face Tracking integration.
- Compatible avatar with a valid VRC Avatar Descriptor.

Additional dependencies or SDK samples may be necessary depending on the selected features.

---

## 🧪 Testing Status

The v0.4.1 development build includes several changes that still require validation.

| Feature | Current status |
|---|---|
| Avatar diagnostics | Available |
| Non-destructive texture optimization | Available |
| Eye Look Bone Rotation setup | Testing |
| OSC eye tracking integration | Testing |
| Native Face Tracking mappings | In progress |
| MainGestures facial layer control | Testing |
| PC / Android platform override | Testing |
| Steam Frame compatibility | Testing |

A successful configuration report does not replace testing in VRChat.

Always use **Build & Test** before uploading an avatar.

---

## 📚 Documentation

| Document | Description |
|---|---|
| [📜 License](LICENSE) | Usage, redistribution and modification rights. |
| [🤝 Contributing](CONTRIBUTING.md) | How to contribute to the project. |
| [ℹ️ Notice](NOTICE) | Credits and third-party notices. |

---

## 🗺️ Development Roadmap

- [x] Unity Editor interface
- [x] Avatar diagnostic engine
- [x] Non-destructive texture optimization
- [x] PC / Android variant creation
- [x] Per-platform override integration
- [x] FEO menu and controller duplication
- [x] Eye Look Bone Rotation fallback
- [x] Initial gesture-layer detection
- [ ] Complete native Face Tracking BlendShape mapping
- [ ] Improve facial-only gesture suppression
- [ ] Validate eyelid and blink behavior
- [ ] Full PCVR / Quest testing
- [ ] Steam Frame compatibility validation
- [ ] Automated tracking validation reports
- [ ] First stable release

---

## 💜 Credits

**Created and maintained by Zyumie.**

Developed with assistance from OpenAI ChatGPT for architecture, documentation, code drafting and debugging.

Third-party SDKs, libraries and avatar assets remain the property of their respective owners.

Community contributions and improvements are welcome!

---

<div align="center">

**Developed with ❤️ by Zyumie**

*Making VRChat avatar optimization and tracking integration easier.*

</div>
