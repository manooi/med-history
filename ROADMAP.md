# ROADMAP

Epic: `med-history-4ei` — personal medical history web app. Plan: [`plans/epic-medhistory.md`](./plans/epic-medhistory.md)

## v1

- [x] 1. Scaffold dotnet MVC project (`med-history-4ei.1`)
- [x] 2. Tailwind v4 wiring + monochrome layout shell (`med-history-4ei.2`)
- [x] 3. EF Core data layer — Entry/Photo, Npgsql, migration (`med-history-4ei.3`)
- [x] 4. Cookie auth, single password (`med-history-4ei.4`)
- [x] 5. Entry CRUD + day view (`med-history-4ei.5`)
- [ ] 6. Photo upload + serving from bytea (`med-history-4ei.6`)
- [x] 7. History view (`med-history-4ei.7`)
- [ ] 8. xUnit pure-logic tests (`med-history-4ei.8`)

## Later ideas

- Charts/trends per entry type
- Export (PDF/CSV) for doctor visits
- Client-side image downscale before upload
