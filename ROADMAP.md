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
- [x] 13. GCP runbook `docs/deploy-cloudrun.md` (`nvs.4`)
- [x] 14. GitHub Actions build+deploy (`nvs.5`)
- [ ] 15. VPS Postgres hardening (`nvs.6`)
- [ ] 16. First deploy + e2e verify (`nvs.7`)

## Later ideas

- Charts/trends per entry type
- Export (PDF/CSV) for doctor visits
- Client-side image downscale before upload
