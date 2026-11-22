<div align="center">

# Zyumie Avatar Suite

**A Unity Editor toolkit for VRChat avatar diagnostics, tracking setup and non-destructive optimization.**

[![Unity](https://img.shields.io/badge/Unity-2022.3-black?logo=unity)](https://unity.com/)
[![VRChat SDK](https://img.shields.io/badge/VRChat_Avatars_SDK-3.10.6%2B-blue)](https://creators.vrchat.com/)
[![License](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)

</div>

Zyumie Avatar Suite brings common avatar checks and editing tools together in one Unity Editor workflow. It focuses on readable diagnostics, guarded quick actions, and optimization workflows that create an avatar copy instead of overwriting the original source meshes.

> **Development status:** the repository package metadata currently reports `0.5.0`. Features and compatibility are still being tested. This README describes the current editor workflow, not a guarantee that every avatar or SDK configuration will pass validation.

---

## ✨ Features

| Area | Available tools |
|---|---|
| **Diagnostics** | Avatar performance analysis; Skinning, Materials and PhysBones audit; Animator and Avatar Mask audit; Eye Look audit; Face Tracking diagnostic; Tracking, OSC and Expression Parameters inspection. |
| **Quick Actions** | Missing Scripts cleanup; Eye Look setup; Face Tracking (FEO) setup. Modifying actions ask for confirmation. |
| **Optimization** | Skinning and bone-weight optimization; duplicate Material Slot consolidation; PhysBone reference cleanup; texture optimization; polygon reduction. |
| **Preview** | Mouse-driven eye simulator for local testing. |

## 🧭 Unity menu

All standalone commands are grouped under **Tools → Zyumie**.

- **Open Avatar Suite** — opens the main editor window.
- **Eye Simulator (Mouse)** — opens the local eye preview directly below the main command.
- **Diagnostic** — read-only reports and audits.
- **Quick Actions** — operations that can modify the avatar or its setup.

The main window is organized into three tabs: **Diagnostic**, **Actions rapides** and **Optimisation**.

## 🔍 Diagnostics

The diagnostic tools inspect avatar performance metrics, Skinning, bones, Materials, PhysBones, FX/Gesture Animator controllers, Avatar Masks, VRChat Eye Look, Face Tracking BlendShape candidates and Tracking/OSC/Expression Parameters configuration.

Diagnostic reports are intended to help identify issues. They do not replace the VRChat SDK Builder or an in-game test.

## ⚙️ Quick Actions

Quick Actions currently include:

- **Clean Missing Scripts** — removes missing script components from the selected avatar hierarchy.
- **Configure Eye Look** — assists with the VRChat Avatar Descriptor Eye Look configuration.
- **Configure Face Tracking (FEO)** — applies the suite's Face and Eye Operations setup.

The suite displays a confirmation before these modifying operations. Back up your project and avatar before running them.

## 🚀 Optimization

Optimization tools currently include:

- **Skinning & Bones** — reduces unnecessary skinning influences and renderer bone references where supported.
- **Duplicate Material Slots** — consolidates repeated submeshes using the same Material asset on a renderer.
- **PhysBones** — cleans supported redundant or null references and ignore-transform entries.
- **Textures** — creates optimized texture/material copies for the duplicated avatar.
- **Polygon Reduction** — applies a configurable reduction target to eligible geometry.

Optimization workflows create a separate avatar copy and generated assets. Polygon reduction is lossy: inspect silhouettes, UV seams, clothing, joints, hair and deformation before using the result. A requested reduction percentage is a target, not a guaranteed final avatar triangle count.

## 👁️ Eye & Face Tracking

The suite includes setup and diagnostic workflows for VRChat Eye Look and native Unified Expressions Face Tracking.

- Eye Look setup uses the avatar's configured eye references where available.
- FEO can inspect avatar renderers and BlendShapes to assist with tracking configuration.
- The mouse eye simulator is a local preview tool; it does not prove that live headset or OSC tracking is working.
- Face Tracking requires compatible avatar BlendShapes and valid mappings. Automatic discovery may need manual review.
- OSC diagnostics can report configuration and expected status information, but cannot independently prove that an external application is transmitting valid live data.

Always test tracking in VRChat with the intended headset, runtime and OSC application.

## 📦 Requirements

- **Unity 2022.3 LTS**
- **VRChat Avatars SDK**
- VRChat Avatars SDK **3.10.6 or newer** for the native Unified Expressions Face Tracking workflow.
- A valid avatar with a configured `VRC Avatar Descriptor`.
- AV3 Demo Assets may be required when the avatar does not have custom FX or Gesture controllers assigned.

## 📥 Installation

1. Download or clone this repository.
2. In Unity, open **Window → Package Management → Package Manager**.
3. Select **+ → Add package from disk…**.
4. Choose `ZyumieAvatarSuite/package.json`.
5. Wait for Unity to compile, then open **Tools → Zyumie → Open Avatar Suite**.

For updates, back up the project, avatar and generated Animator assets, replace the package folder, reopen Unity and resolve all compilation errors before running an operation.

## 🧪 Validation & known limitations

- A successful diagnostic report does not guarantee that VRChat avatar validation or upload will succeed.
- The VRChat SDK Builder remains authoritative for platform compatibility, performance ranks and upload validation.
- Polygon reduction is a lossy spatial simplification. Review the result visually and test deformation.
- Automatic Face Tracking mapping is not guaranteed for every avatar.
- External OSC transmission and headset tracking must be tested in the actual runtime.
- The current development build has not been certified against every Unity, VRChat SDK or avatar configuration.

Recommended workflow: back up the project, run read-only diagnostics, apply one operation at a time, inspect the generated avatar, run VRChat **Build & Test**, then validate the intended platform in the SDK Builder.

## 📚 Project files

- [Package documentation](ZyumieAvatarSuite/README.md)
- [License](LICENSE)
- [Contributing](CONTRIBUTING.md)
- [Third-party notices](NOTICE)

## 💜 Credits

Created and maintained by **Zyumie**.

Developed with assistance from OpenAI ChatGPT for architecture, documentation, code drafting and debugging.

Unity, VRChat and third-party SDKs, libraries and avatar assets remain the property of their respective owners. This project is not affiliated with or endorsed by Unity or VRChat.

---

<div align="center">

**Developed with ❤️ by Zyumie**

*Making VRChat avatar optimization and tracking integration easier.*

</div>
