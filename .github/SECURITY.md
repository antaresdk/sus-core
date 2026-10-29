# Security policy

## Supported versions

Security fixes go into the latest release of `com.sharq-it.sus.core`
([releases](https://github.com/antaresdk/sus-core/releases)). Please check that the problem is
still there in the latest version before reporting it.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

- Report it privately through GitHub: the **Security** tab of this repository →
  **Report a vulnerability**.
- If you cannot use GitHub, e-mail [support@sus-ui.dev](mailto:support@sus-ui.dev) with
  "Security" in the subject.

Include the package version, the Unity version and platform, the steps to reproduce (or a proof of
concept) and the impact as you see it.

We will acknowledge the report, keep you informed while we work on a fix, and credit you in the
release notes unless you ask us not to.

## Scope

The package runs inside your Unity project, in the Editor and in players. In scope are problems in
the package code — for example the Editor-time compiler or an editor tool doing something unsafe
with crafted project files. Problems in Unity itself go to Unity.
