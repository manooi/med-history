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
