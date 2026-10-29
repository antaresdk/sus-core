# Contributing to SUS Core

Thanks for helping. Issues and pull requests are welcome.

## Before you start

- **Bugs:** open an issue with the bug report form — the package version, the Unity version and the
  steps to reproduce are what lets us find the problem.
- **Larger changes** (a new API, a change of behaviour): open an issue first, so we agree on the
  approach before you write the code.
- **Small fixes** (typos, docs, an obvious bug): a pull request is enough.
- **Security problems:** not in public issues — see [SECURITY.md](SECURITY.md).
- **Questions:** the [docs](https://sus-ui.dev/docs/getting-started) and
  [Discord](https://discord.gg/gwS9nwqWWj).

## Development setup

1. Unity 6000.3 or later.
2. Clone this repository and add it to a Unity project as a local package, with its tests enabled
   (`Packages/manifest.json`):

   ```json
   {
     "dependencies": {
       "com.sharq-it.sus.core": "file:../../sus-core"
     },
     "testables": ["com.sharq-it.sus.core"]
   }
   ```

3. Run the tests in **Window → General → Test Runner**: EditMode
   (`com.sharq-it.sus.core.editor.tests`) and PlayMode (`com.sharq-it.sus.core.runtime.tests`).

## Rules for a change

- **Generated files are not edited by hand.** `*.g.cs` and `*.g.uss` come from a `.sharq` source or a
  generator: change the source and regenerate.
- **Styles live in USS.** C# switches states with `AddToClassList` / `RemoveFromClassList`, not with
  the inline style API; USS uses only properties UI Toolkit supports.
- **English everywhere a person reads:** code comments, XML docs, exception messages, commit
  messages and docs.
- **A behaviour change comes with a test** and a line under `## [Unreleased]` in `CHANGELOG.md`.
- **One change per pull request.** Say what it fixes and how you tested it — the template asks for
  both.

## License

By contributing you agree that your contribution is licensed under the MIT License of this
repository ([LICENSE.md](../LICENSE.md)).
