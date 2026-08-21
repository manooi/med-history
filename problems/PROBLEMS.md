# PROBLEMS

_Index of resolved bugs. Details in PROBLEMS_DETAILS.md, same number._

1. Logout link never signed out — layout had GET anchor, endpoint is POST-only → replaced with POST form + antiforgery in _Layout (med-history-4ei.11)
2. AddDataProtectionKeys migration recreated Logs table instead of DataProtectionKeys — snapshot mangled during cross-bead rebase; regenerated from known-good snapshot (med-history-nvs.8)
3. Deployed app rendered all dates/times in UTC — AppTime reads TimeZoneInfo.Local and Cloud Run containers default to UTC → ENV TZ=Asia/Bangkok in Dockerfile final stage (med-history-5eu)
4. Lightbox dialog stuck top-left on mobile — Tailwind v4 preflight `margin:0` on `*` overrides native `<dialog>` `margin:auto` centering → `m-auto` on the dialog (med-history-3c4)
5. Fast double-tap on a login keypad digit zoomed the page instead of entering two digits — Safari's double-tap-to-zoom gesture fires on the tap target → `touch-action: manipulation` on the keypad container and every button (med-history-p30)
6. Clearing the photo picker let an in-flight result from the previous selection win — the entry form's `change` handler returned before bumping `photosGeneration`, so a stale downscale swap re-populated the cleared input (and the new EXIF read re-revealed the date button) → bump the counter before the early return (med-history-4ei.26)
7. Editing an entry from search or the type report saved back to the day page, losing the query/page and the report selection — the entry form had three entry points and one hard-coded exit (`EntriesController.RedirectToDay`) → returnUrl round-trip through the form, every target `Url.IsLocalUrl`-guarded via `RedirectRules` (med-history-4ei.27)
