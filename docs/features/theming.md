# Theming, dark mode, print

## Palette rule

Black / white / neutral grays only. **One carve-out: destructive actions** (delete, remove) use red:

- plain: `text-red-600 hover:text-red-800`
- bordered: `border-red-600 hover:bg-red-600 hover:text-white`

Deactivate is **not** destructive — it stays neutral. No other color classes, ever. Severity is carried by label text and border weight, never by hue.

## Dark mode

Attribute variant `[data-theme=dark]`, declared as `@custom-variant dark` in `Styles/site.css` and nested inside `@media not print` — so **dark utilities are inert on paper** (the doctor report always prints light). `color-scheme: dark` lives in the same media block.

A pre-stylesheet script in `_Layout` head sets `data-theme` before first paint: localStorage override (`'dark'`/`'light'`) wins, else `prefers-color-scheme`. The system-preference listener only takes effect while no override is stored (i.e. in Auto). The navbar button cycles Auto → Dark → Light.

### Palette map (grey-lifted, bead `bmp`)

| Role | Light | Dark |
|---|---|---|
| ground | white | `neutral-800` |
| ink | black | `neutral-100` |
| hover on ground | `neutral-100` | `neutral-700` |
| raised panels | white | `neutral-700` |
| hover back to ground | — | `neutral-800` |
| borders | `neutral-300` / `neutral-400` | `neutral-600` / `neutral-500` |
| dividers | `neutral-300` | `neutral-600` |

Solid selected states invert light-on-dark: chip is `dark:bg-neutral-100 dark:text-neutral-950` — **the only remaining `950` tokens are chip TEXT**. `hover:bg-black hover:text-white` ↔ `dark:hover:bg-neutral-100 dark:hover:text-neutral-950`. Red rests at `red-400` in dark; the solid `red-600` hover is shared by both themes. No `dark:red-500` and no `dark:bg-*-950` anywhere.

> **Invariant:** every new light class in a view carries its `dark:` counterpart **on the same line**.

## Print

Controls and the site nav carry Tailwind `print:hidden`. In the *built* CSS the selector is escaped as `print\:hidden` — `grep -F` needs the backslash when you go looking for it.
