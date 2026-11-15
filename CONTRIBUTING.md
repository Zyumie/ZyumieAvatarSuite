# Contributing to Zyumie Avatar Suite

Thank you for your interest in improving Zyumie Avatar Suite.

## Before contributing

-   Check existing issues and pull requests before opening a duplicate.
-   For large changes, open an issue first to discuss the proposed
    approach.
-   Keep changes focused and explain why they are needed.
-   Do not include private avatars, paid assets, personal data, API
    keys, credentials or other files you do not have permission to
    redistribute.

## Bug reports

Please include, where possible:

-   Unity Editor version.
-   VRChat SDK - Avatars version.
-   Operating system.
-   Zyumie Avatar Suite version or commit.
-   Exact steps to reproduce the issue.
-   Expected and actual behaviour.
-   Relevant Unity Console errors and stack traces.
-   A minimal reproduction project or sanitized diagnostic report, if
    possible.

Remove usernames, local file paths and other personal information from
logs before posting them publicly.

## Pull requests

-   Describe the problem and the proposed solution.
-   Link related issues.
-   Explain how the change was tested.
-   Include screenshots for meaningful Editor UI changes.
-   Update documentation and CHANGELOG.md when appropriate.
-   Avoid unrelated formatting or refactoring in the same pull request.
-   Do not claim a platform is supported unless it has been tested on
    that platform.

## Code expectations

-   Prefer small, focused and readable C# classes.
-   Keep Unity Editor-only code inside Editor assemblies or Editor
    folders.
-   Avoid destructive avatar modifications by default.
-   Create backups before operations that modify avatar assets.
-   Validate references before changing components, parameters or
    BlendShapes.
-   Report unsupported configurations instead of silently guessing.
-   Keep PC and Android build logic separate where platform requirements
    differ.
-   Add tests for reusable logic when practical.

## Licensing and contributions

The project is licensed under the Apache License 2.0.

By submitting a contribution that is intentionally offered for inclusion
in this project, you agree that it is submitted under the Apache License
2.0, unless a separate written agreement says otherwise. You confirm
that you have the right to submit the contribution and that it does not
knowingly include third-party material that cannot be redistributed
under the project terms.

You retain copyright in your own contributions.

## Community conduct

Be respectful, constructive and specific. Harassment, discrimination,
personal attacks and sharing private information are not acceptable.
