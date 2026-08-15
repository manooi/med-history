# PROBLEMS DETAILS

## 1. Logout link never signed out

**Found:** 2026-08-15, e2e verification pass after v1 landed (POST /logout check 400, post-logout / still 200).
**Root cause:** parallel beads — tailwind bead (4ei.2) wrote nav with plain `<a href="/logout">` before auth existed; auth bead (4ei.4) was scoped away from _Layout.cshtml and only added a GET /logout dead-link guard that redirects to `/` without signing out. Net: UI logout did nothing.
**Fix (med-history-4ei.11):** `_Layout.cshtml` anchor → inline `<form method="post" action="/logout">` with `@Html.AntiForgeryToken()` + styled submit button; rendered only when authenticated.
**Regression coverage:** e2e verify script (scratchpad) asserts POST /logout → 302 and next `/` → 302 login. No unit test — defect was markup-only, no extractable decision logic.
