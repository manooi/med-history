# Epic: Containerize + Cloud Run deploy (`med-history-nvs`)

Decisions (2026-08-15): Postgres stays on existing VPS · deploy via GitHub Actions CI · secrets in GCP Secret Manager.

## Why

Run med-history on Cloud Run instead of localhost so it's reachable anywhere; personal-scale cost (~$0 at min-instances 0).

## Architecture

```
GitHub push(main) → Actions (WIF auth) → build image → Artifact Registry → Cloud Run (asia-southeast1)
                                                                      │ env from Secret Manager
                                                                      └→ VPS Postgres (TLS, sslmode=require)
```

## Design points (the gotchas)

1. **Dockerfile — 3 stages:** node:22-alpine (npm ci + `npm run css`) → dotnet/sdk:10.0 (`dotnet publish -c Release`) → dotnet/aspnet:10.0 runtime, non-root, `ASPNETCORE_HTTP_PORTS=8080` (Cloud Run default port 8080). `.dockerignore`: bin, obj, node_modules, .git, .env, med-history-wt.
2. **Data Protection keys must persist to DB** (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, `PersistKeysToDbContext<AppDbContext>()` + migration). Without it every deploy/cold start rotates keys → all login cookies + antiforgery tokens die. This is the one *required* app change.
3. **Proxy awareness:** Cloud Run terminates TLS. Add ForwardedHeaders (X-Forwarded-Proto/For) before auth; skip `UseHttpsRedirection` when running in container (env flag) to avoid redirect loops. Cookie SecurePolicy stays SameAsRequest.
4. **VPS Postgres reachability:** Cloud Run egress IPs are dynamic — either open Postgres port to world with TLS + strong password + tight pg_hba (`hostssl` only), or pay for VPC connector + static NAT. Recommendation: TLS-only pg_hba + strong password (personal-scale risk). `postgresql-vps-agent` skill can do the TLS/pg_hba setup.
5. **Connection string** gains `SSL Mode=Require` (+ `Trust Server Certificate=true` unless proper CA on VPS).
6. **Migrations stay manual** — user runs `dotnet ef database update` from local against VPS. No auto-migrate on startup.
7. **Secrets:** Secret Manager `medhistory-connstring` + `medhistory-auth-password` → exposed to service as env vars `ConnectionStrings__Default`, `Auth__Password`. DotNetEnv tolerates missing .env; env vars flow through IConfiguration unchanged.
8. **CI:** GitHub Actions with Workload Identity Federation (no service-account JSON keys). Requires pushing this repo to GitHub (not yet done — explicit step).
9. **Service shape:** region asia-southeast1, min 0 / max 1 instance (single user; also removes multi-instance edge cases), 512 Mi, startup probe GET /login.

## Scope

| bd id | Item | Model | Depends on |
|---|---|---|---|
| `.1` | Dockerfile + .dockerignore, builds + runs locally | sonnet | — |
| `.2` | DataProtection keys → DB (package, config, migration — NOT applied) | opus | — |
| `.3` | ForwardedHeaders + container env handling (ports, no https-redirect) | sonnet | .1 |
| `.4` | GCP setup runbook doc (project, Artifact Registry, Secret Manager, WIF, service) — docs/deploy-cloudrun.md | sonnet | — |
| `.5` | GitHub Actions workflow build+deploy | sonnet | .1 .4 |
| `.6` | VPS Postgres hardening for public TLS access (via postgresql-vps-agent) | — | — |
| `.7` | First deploy + e2e verify against Cloud Run URL | — | all |

Build order: (.1 .2 .4 parallel) → .3 → .5 → .6 → .7. User actions required: create GCP project/enable billing, push repo to GitHub, run the two migrations, VPS firewall changes.

## Checklist

- [ ] .1 Dockerfile
- [ ] .2 DataProtection persistence
- [ ] .3 proxy/container env
- [ ] .4 GCP runbook
- [ ] .5 CI workflow
- [ ] .6 VPS Postgres hardening
- [ ] .7 first deploy + verify
