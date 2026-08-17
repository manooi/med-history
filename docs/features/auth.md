# Auth: passcode screen + login throttling

Single-password cookie auth, single user.

## Secrets

Only in user-secrets — `ConnectionStrings:Default`, `Auth:Password`. Never in `appsettings`.

> **Invariant:** `Auth:Password` must be **exactly 6 digits** — the login UI is a 6-digit pad that auto-submits on the sixth digit.

## Throttling

- A wrong password inserts a `LoginAttempts` row and eats a flat 2 s delay.
- ≥5 failures in 15 min locks out until *oldest-of-the-newest-5* + 15 min. Decision is the pure `LoginThrottleRules.Decide`.
- A POST arriving while locked **neither checks the password nor records an attempt** — so hammering a locked account can't push the expiry outward.
- A successful login wipes the table. (`Succeeded` column exists but is always false in practice.)

## Passcode screen (`/login`)

iOS-style 6-digit pad. JS owns entry and only stamps the result onto the single **hidden** `Password` field — nothing is focusable, so no OS keyboard appears. Six dots fill as digits land. A physical keyboard works at every width; the on-screen 3-column keypad is `sm:hidden` (mobile only). The sixth digit auto-submits via `requestSubmit()`, double-submit guarded.

Wrong passcode → the dot row runs the `.passcode-shake` keyframes in `site.css` (mono; disabled under `prefers-reduced-motion`).

`<noscript>` ships its **own** fallback form and hides the dots/keypad — a second `Password` input inside the main form would comma-join the two values on post.

Server side is untouched by all of this: same POST contract, same throttle paths.
