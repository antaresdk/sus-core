# Sharq language for VS Code / Cursor

Syntax highlighting for `.sharq` single-file components (SUS):

- `<template>` → XML (+ Sharq directives `v-if` / `v-for`, `:prop`, `@event`, `$MainElement`)
- `<script>` → C# (+ `$using`)
- `<style>` → CSS / USS (`-unity-*`, `--sus-*` tokens)

This folder ships inside the `com.sharq-it.sus.core` package at `Tools~/vscode-sharq/`
(Unity does not import `Tools~` as assets).

## Install (local, no Marketplace)

**Option A — copy into extensions**

1. Copy this folder to:
   - Windows: `%USERPROFILE%\.vscode\extensions\sharq-it.sharq-language-0.1.0`
   - macOS / Linux: `~/.vscode/extensions/sharq-it.sharq-language-0.1.0`
   - Cursor: `%USERPROFILE%\.cursor\extensions\sharq-it.sharq-language-0.1.0` (or `~/.cursor/extensions/…`)
2. Restart the editor.
3. Open any `.sharq` — language mode should be **Sharq**.

**Option B — Install from VSIX**

A prebuilt `sharq-language-0.1.0.vsix` sits next to this README. In VS Code / Cursor:
`Extensions` → `…` → **Install from VSIX…** → pick that file → reload.

To rebuild the VSIX yourself (optional):

```bash
npm i -g @vscode/vsce
cd Tools~/vscode-sharq
vsce package --allow-missing-repository
```

## Rider / JetBrains

There is no dedicated JetBrains plugin yet. Rider can load the same TextMate grammar
(`syntaxes/sharq.tmLanguage.json`) via a TextMate bundle / “TextMate Bundles” support —
scopes match VS Code. A first-class Rider plugin is out of scope for this package drop.

## Marketplace

Publishing under publisher `sharq-it` on the VS Code Marketplace is optional and separate
from this package path — not required to use highlighting locally.
