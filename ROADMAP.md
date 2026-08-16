# ROADMAP

Epic: `med-history-4ei` — personal medical history web app. Plan: [`plans/epic-medhistory.md`](./plans/epic-medhistory.md)

## v1

- [x] 1. Scaffold dotnet MVC project (`med-history-4ei.1`)
- [x] 2. Tailwind v4 wiring + monochrome layout shell (`med-history-4ei.2`)
- [x] 3. EF Core data layer — Entry/Photo, Npgsql, migration (`med-history-4ei.3`)
- [x] 4. Cookie auth, single password (`med-history-4ei.4`)
- [x] 5. Entry CRUD + day view (`med-history-4ei.5`)
- [x] 6. Photo upload + serving from bytea (`med-history-4ei.6`)
- [x] 7. History view (`med-history-4ei.7`)
- [x] 8. xUnit pure-logic tests (`med-history-4ei.8`)
- [x] 9. dotenv support — `.env.example`, DotNetEnv (`med-history-4ei.9`)
- [x] 9b. DB logger — Logs table, DbLogger provider, app events (`med-history-4ei.14`)

## v2 — Cloud Run (`med-history-nvs`, plan: [`plans/epic-cloudrun.md`](./plans/epic-cloudrun.md))

- [x] 10. Dockerfile + .dockerignore (`nvs.1`)
- [x] 11. DataProtection keys → DB (`nvs.2`)
- [x] 12. ForwardedHeaders + container env (`nvs.3`)
- [x] 13. GCP runbook (merged into `docs/SETUP.md`) (`nvs.4`)
- [x] 14. GitHub Actions build+deploy (`nvs.5`)
- [~] 15. VPS Postgres hardening (`nvs.6`) — won't-do, TLS already in effect; risk accepted
- [x] 16. First deploy + e2e verify (`nvs.7`)

## v3 — quality of life

- [x] 17. Login throttling — 2s fail delay + DB-backed lockout (`med-history-435`)
- [x] 18. Client-side image downscale before upload (`med-history-26d`)
- [x] 19. Search over note + med name (`med-history-f1t`)
- [x] 20. Doctor summary export, print-CSS (`med-history-lqv`)
- [x] 21. PWA manifest + home-screen icon (`med-history-9lf`)
- [x] 22. Dark mode — system-follow + toggle, near-black (`med-history-d36`)
- [x] 24. Mobile hamburger nav below sm (`med-history-0xq`)
- [x] 23. Controller refactor epic — split Meds + Data query layer (`med-history-9b1`, plan: [`plans/epic-refactor-controllers.md`](./plans/epic-refactor-controllers.md))

## v4 — measurements

- [x] 25. Measurements framework + weight recording — generic `Measurements` table (Kind/Value/OccurredAt), day-page card, `/weight-report` month grid (`4ei.15`)

## v5 — hardening

- [x] 26. Per-IP rate limiting on `/login` — built-in ASP.NET limiter, fixed window 10/60s, 429 + Retry-After (`4ei.16`)

## v6 — UI polish

- [x] 27. Day page: Meds checklist collapses into an accordion with per-day progress text (`4ei.17`)
- [x] 28. Type report: multi-select types — checkbox toggles, merged day timeline (`4ei.18`)

## Later ideas

- Charts/trends per entry type
- More measurement kinds (blood pressure, temperature) on the Measurements table
