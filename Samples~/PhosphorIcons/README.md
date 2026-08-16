# Phosphor Icon Set (optional)

The full [Phosphor Icons](https://github.com/phosphor-icons/core) set — 1,512 icons in 6 weights
(thin, light, regular, bold, fill, duotone), MIT licensed. See `Third-Party Notices.txt` in the
package root.

## Do you need it?

Usually not. SusCore ships a 127-icon subset in the package itself
(`Runtime/Resources/SusRuntime/Icons/core`), which covers every icon the components and the
downstream UI packages use by default. `SusIcon` resolves those without importing anything.

Import this sample when you want to pick icons freely by Phosphor name, for example
`<sus:SusIcon icon="airplane-tilt" weight="Duotone" />`.

## What importing costs

The icons live in a `Resources` folder, which means Unity includes **all** of them in every player
build and they cannot be stripped — roughly 19 MB of vector assets, plus a noticeably longer first
import. If you only need a handful of extra icons, copy those individual `.svg` files into your own
`Assets/**/Resources/SusRuntime/Icons/phosphor/{weight}/` folder instead of importing the whole set;
`PhosphorIconProvider` picks up icons from any `Resources` folder with that layout.

## How resolution works

`SusIconRegistry` queries providers in order: project-registered providers first, then the built-in
core subset, then Phosphor. Aliases (`"settings"` → `gear`, `"gold"` → `coin`, …) and weight
suffixes (`"star-fill"` → name `star`, weight `Fill`) are resolved before the lookup, so you can
address icons semantically and swap the underlying set later.
