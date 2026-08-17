# Navigation, mobile nav, PWA

All of this lives in `Views/Shared/_Layout.cshtml`.

## Desktop bar (`sm` and up)

Brand, then Today / History / Reports ▾ / Search / Types, then the theme toggle and Logout.

"Reports ▾" is a pure-CSS `group-hover` flyout — no JS. On desktop hover opens it; on touch a tap simply follows the link to the `/report` hub.

## Mobile bar (below `sm`)

Three items, left to right: **brand — menu chevron (centered) — theme toggle (far right)**.

Brand and the toggle wrapper are both `flex-1`, so the auto-width `<details>` between them lands exactly centered. The menu is a CSS-only `<details>`; its chevron rotates 180° when open (`group-open:rotate-180`).

> **Invariant:** brand, `<details>` and the toggle wrapper all stay `position: static`. The dropdown panel is `absolute left-0 right-0` against `nav.relative` so it spans the full bar — a positioned ancestor would break that.

Panel contents: Today / History / Reports (hub) / Search / Types / Logout. The flyout is desktop-only.

## Theme toggle

> **Invariant:** the toggle exists **twice** (desktop bar + mobile bar). It is bound by class `.theme-toggle` via `querySelectorAll`, with labels synced. **Never bind it by id.**

Cycle and palette details: [theming.md](./theming.md).

## PWA

`wwwroot/manifest.webmanifest` (standalone, black theme) + `wwwroot/icons/`.

`icon.svg` is the **source** — black tile, white mono "m". The PNGs (192, 512, apple-touch 180) are rasterized from it with `qlmanage -t -s <size>` and committed, because `magick` lacks SVG-text delegates on this machine.

Manifest, touch icon and theme-color are linked in the `_Layout` head. No service worker. .NET 10 static assets serve `.webmanifest` correctly with no `Program.cs` change.
