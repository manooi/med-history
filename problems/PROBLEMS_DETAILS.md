# PROBLEMS DETAILS

## 1. Logout link never signed out

**Found:** 2026-08-15, e2e verification pass after v1 landed (POST /logout check 400, post-logout / still 200).
**Root cause:** parallel beads — tailwind bead (4ei.2) wrote nav with plain `<a href="/logout">` before auth existed; auth bead (4ei.4) was scoped away from _Layout.cshtml and only added a GET /logout dead-link guard that redirects to `/` without signing out. Net: UI logout did nothing.
**Fix (med-history-4ei.11):** `_Layout.cshtml` anchor → inline `<form method="post" action="/logout">` with `@Html.AntiForgeryToken()` + styled submit button; rendered only when authenticated.
**Regression coverage:** e2e verify script (scratchpad) asserts POST /logout → 302 and next `/` → 302 login. No unit test — defect was markup-only, no extractable decision logic.

## 2. AddDataProtectionKeys migration recreated Logs table

**Found:** 2026-08-15, nvs.3 builder's container smoke test (`relation "Logs" already exists`, then DataProtection keyring 500s on every request).
**Root cause:** nvs.2 (DataProtection) and 4ei.14 (logger) both changed the EF model in parallel worktrees. During the nvs.2 rebase-landing, `dotnet ef migrations remove` + `add` was run against a snapshot that had been reverted to a state missing LogEntry — the regenerated migration diffed model-with-Logs against snapshot-without-Logs and emitted a duplicate `CreateTable("Logs")`, never creating DataProtectionKeys. Orchestrator's verification grep miscounted and let it land.
**Fix (med-history-nvs.8):** restored snapshot from post-AddLogsTable commit (1a0d310), deleted broken migration, regenerated — new Up() creates only DataProtectionKeys. Verified on throwaway Postgres: full clean chain AND partial-failure recovery (DB stuck after AddLogsTable applies the fixed migration cleanly).
**Regression coverage:** e2e — migration chain applied + login round-trip (exercises keyring write) on fresh containerized Postgres. No unit test: defect lives in generated migration code, no extractable pure logic. Process guard added to memory: when parallel beads both touch the EF model, regenerate the later migration from the merged snapshot and diff-review the migration body before landing.

## 3. Deployed app rendered all dates/times in UTC

**Found:** 2026-08-15, user noticed dates off by 7 h on the Cloud Run deployment (local dev looked fine).
**Root cause:** `AppTime` is built on `TimeZoneInfo.Local` / `DateTime.Now` ("v1 treats server-local time as the user's time zone" — `AppTime.cs` comment). Dev machine is Asia/Bangkok so everything looked right locally; Cloud Run containers run UTC, so `TimeZoneInfo.Local` = UTC in prod and every day grouping, time label, and form default rendered in UTC. Same pattern also live in `HistoryController.cs:27` and `EntriesController.cs:33`.
**Fix (med-history-5eu):** `ENV TZ=Asia/Bangkok` in the Dockerfile final stage. Debian `aspnet:10.0` base ships tzdata, so the env var alone flips `TimeZoneInfo.Local`. Code-level pinning (`FindSystemTimeZoneById("Asia/Bangkok")` inside `AppTime`) was considered and deliberately deferred — chosen fix keeps v1's server-local design and moves the knob to the image.
**Regression coverage:** none feasible — fix is a container env var, no app code changed; `TimeZoneInfo.Local`-dependent logic can't assert a zone the test host doesn't have. Guard is the Dockerfile comment: if the base image ever moves to chiseled/alpine (no tzdata), TZ silently stops working — revisit code-level pinning then.

## 4. Lightbox dialog stuck top-left on mobile

**Found:** 2026-08-15, user viewing photo lightbox (med-history-h3f) on iPhone 15 Pro Max — image not centered.
**Root cause:** Tailwind v4 preflight emits `@layer base { *, :after, :before, ::backdrop { margin: 0; ... } }`. Native `<dialog>` centering comes from the UA stylesheet's `margin: auto` (with `position: fixed; inset: 0`); the preflight universal reset overrides it, so the modal renders at the viewport's top-left. Desktop masked it — image large enough to near-fill the viewport.
**Fix (med-history-3c4):** `m-auto` added to the `<dialog>` class list in `_Lightbox.cshtml`, restoring auto-margin centering on both axes.
**Regression coverage:** none — markup-only, one utility class, no extractable decision logic (same call as problem 1). Gotcha worth remembering: any future native `<dialog>` needs `m-auto` under Tailwind preflight.

## 5. Fast double-tap on a login keypad digit zoomed the page

**Found:** 2026-08-16, iOS user report — tapping the same digit twice quickly on `/login`'s passcode keypad zoomed the page instead of registering two taps.
**Root cause:** Safari's double-tap-to-zoom gesture triggers on a fast repeat tap of the same element; the keypad buttons had no `touch-action` set, so the default `auto` left the gesture live on top of the `click` handler the passcode JS listens for.
**Fix (med-history-p30):** `touch-action: manipulation` (Tailwind `touch-manipulation`) added to the `#passcode-keypad` grid AND to every digit/backspace button — the container alone is not reliable across Safari versions since the button, not its ancestor, is the actual tap target. Viewport meta tag left untouched (no `user-scalable=no` / `maximum-scale`) so page-wide pinch-zoom still works; this is a targeted fix on the keypad's own tap targets, not a page-wide zoom kill. While in that markup, also added `active:bg-black active:text-white dark:active:bg-neutral-100 dark:active:text-neutral-950` alongside the existing `hover:` pair — touch has no real hover, so the `hover:` inversion was sticking after a tap until the reader tapped elsewhere ("stuck pressed"); `active:` gives immediate tap feedback instead and matches the existing light/dark hover pairing.
**Regression coverage:** none — defect is a CSS touch-gesture behaviour in a view (`Login.cshtml`), not decision logic; there is nothing here to extract into a pure function to unit test.

## 6. Clearing the photo picker let a stale in-flight result win

**Found:** 2026-08-18, orchestrator's diff review of med-history-4ei.26 — found in review, never observed in the field.
**Root cause:** the photo `change` handler in `Views/Entries/Form.cshtml` guards against re-selection races with a `photosGeneration` counter: each run captures `var generation = ++photosGeneration` and every async continuation bails when `generation !== photosGeneration`. The bump sat *after* the `if (files.length === 0) return;` early return, so clearing the picker was the one path that did not invalidate work already in flight. A selection whose async work was still pending therefore still matched the counter and its continuations ran against a picker the user had just emptied. Two halves were affected: the pre-existing downscale swap re-populated `photosInput.files` with the previous selection's processed files after the user cleared them (so a cleared form silently uploaded the old photos), and the EXIF read added by this bead re-revealed the "Use photo date" button — clicking it would stamp the entry with the capture time of a photo that was no longer being uploaded, i.e. wrong data in a medical log. The downscale half is a latent defect that predates this bead; the EXIF half inherited it by reusing the same counter.
**Fix (med-history-4ei.26):** move `var generation = ++photosGeneration;` above the early return, so the empty-selection path invalidates everything in flight. One move repairs both halves; the rest of the counter semantics are unchanged.
**Regression coverage:** no automated test — this is browser JS and the project has no JS test runner, which the user explicitly chose not to add for this bead (no npm dependency, parser and wiring stay inline in `Form.cshtml`). Verified instead with a DOM-stub harness kept in the session scratchpad, not the repo: it loads the real `<script>` body straight out of `Form.cshtml` and drives it against stubbed `document` / `File` / `DataTransfer` / `createImageBitmap`. The case that covers this bug fires `change` for a selection with EXIF, fires `change` again with an empty file list before the first read settles, then asserts the button stays hidden *and* `photosInput.files` is still empty. It fails on the old ordering and passes on the fix. Keep that harness shape in mind for any future change to this handler — the bug class is "async continuation outlives the selection that started it", and the counter is the only thing standing between it and a wrong timestamp.
