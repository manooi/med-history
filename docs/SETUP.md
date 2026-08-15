# med-history — setup & lessons

Personal medical history tracker. ASP.NET Core MVC (.NET 10) · PostgreSQL (EF Core + Npgsql) · Tailwind v4 monochrome · photos as `bytea` in DB · single-password cookie auth · Cloud Run deploy via GitHub Actions + Workload Identity Federation.

First deployed: 2026-08-15. Detailed GCP runbook: [`deploy-cloudrun.md`](./deploy-cloudrun.md). Bug history: [`../problems/PROBLEMS.md`](../problems/PROBLEMS.md).

---

## 1. Local development

```bash
git clone <repo> && cd med-history

# secrets — .env at repo root (gitignored), loaded by DotNetEnv at startup
cp .env.example .env
# edit: ConnectionStrings__Default (Npgsql format) + Auth__Password

# db schema (3 migrations: InitialCreate, AddLogsTable, AddDataProtectionKeys)
cd MedHistory && dotnet ef database update && cd ..

# run (installs node_modules + builds tailwind css on first run)
./run.sh                       # app on http://localhost:5063

# tests
dotnet test med-history.sln    # 104 tests

# tailwind watch mode during view work
cd MedHistory && npm run css:watch
```

Config keys (env vars use `__` for `:`): `ConnectionStrings:Default`, `Auth:Password`. Sources in priority: real env vars > .env file > user-secrets.

## 2. Docker (local check)

```bash
docker build -t medhistory:local .
docker run --rm -p 8080:8080 -e Auth__Password=test medhistory:local
# login page on http://localhost:8080 — starts fine without DB; DB needed after login
```

## 3. Cloud Run deploy — one-time GCP setup

Full copy-paste runbook with explanations: [`deploy-cloudrun.md`](./deploy-cloudrun.md). Condensed sequence:

```bash
export PROJECT_ID=med-history-505602
export REGION=asia-southeast1
export GH_REPO=<owner>/<repo>          # EXACT GitHub path, case-sensitive

# project + APIs
gcloud projects create $PROJECT_ID && gcloud billing projects link $PROJECT_ID --billing-account=<ID>
gcloud config set project $PROJECT_ID
gcloud services enable run.googleapis.com artifactregistry.googleapis.com secretmanager.googleapis.com iamcredentials.googleapis.com

# artifact registry
gcloud artifacts repositories create medhistory --repository-format=docker --location=$REGION

# secrets (names == env var names, on purpose)
printf '%s' "<npgsql-conn-string-with-SSL Mode=Require>" | gcloud secrets create ConnectionStrings__Default --data-file=-
printf '%s' "<app-password>" | gcloud secrets create Auth__Password --data-file=-

# runtime SA (least privilege) + secret access
gcloud iam service-accounts create medhistory-run
export RUN_SA=medhistory-run@${PROJECT_ID}.iam.gserviceaccount.com
gcloud secrets add-iam-policy-binding ConnectionStrings__Default --member=serviceAccount:$RUN_SA --role=roles/secretmanager.secretAccessor
gcloud secrets add-iam-policy-binding Auth__Password --member=serviceAccount:$RUN_SA --role=roles/secretmanager.secretAccessor

# deploy SA
gcloud iam service-accounts create medhistory-deployer
export DEPLOY_SA=medhistory-deployer@${PROJECT_ID}.iam.gserviceaccount.com
gcloud projects add-iam-policy-binding $PROJECT_ID --member=serviceAccount:$DEPLOY_SA --role=roles/run.admin
gcloud projects add-iam-policy-binding $PROJECT_ID --member=serviceAccount:$DEPLOY_SA --role=roles/artifactregistry.writer
gcloud iam service-accounts add-iam-policy-binding $RUN_SA --member=serviceAccount:$DEPLOY_SA --role=roles/iam.serviceAccountUser

# WIF (keyless CI auth)
export PROJECT_NUMBER=$(gcloud projects describe $PROJECT_ID --format='value(projectNumber)')
gcloud iam workload-identity-pools create github --location=global
gcloud iam workload-identity-pools providers create-oidc github \
  --location=global --workload-identity-pool=github \
  --issuer-uri=https://token.actions.githubusercontent.com \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository" \
  --attribute-condition="assertion.repository == '${GH_REPO}'"
gcloud iam service-accounts add-iam-policy-binding $DEPLOY_SA \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/github/attribute.repository/${GH_REPO}"

# GitHub repo VARIABLES (not secrets) — must exist BEFORE first push to main
gh variable set GCP_PROJECT_ID  --body "$PROJECT_ID"
gh variable set GCP_DEPLOY_SA   --body "$DEPLOY_SA"
gh variable set GCP_WIF_PROVIDER --body "projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/github/providers/github"

git push origin main    # triggers .github/workflows/deploy.yml
```

## 4. WIF lessons (each one cost a failed deploy)

| Symptom | Cause → fix |
|---|---|
| `invalid tag ...pkg.dev//medhistory...` (double slash) | `GCP_PROJECT_ID` repo **variable** missing/empty when workflow ran — set all 3 variables before pushing; re-run with `gh workflow run deploy.yml` |
| `403 iam.serviceAccounts.getAccessToken ... Unable to acquire impersonated credentials` | `roles/iam.workloadIdentityUser` binding missing on the deploy SA, or its principalSet repo path doesn't match `owner/repo` exactly (case-sensitive) |
| `INVALID_ARGUMENT: Identity Pool does not exist` | Pool created in a different project (gcloud pointed elsewhere), or wrong **project number** in the path — number ≠ project id; get it via `gcloud projects describe` |
| Auth passes locally in console but variable path fails | Pool created via console wizard gets default name `github-pool`, CLI example used `github` — `GCP_WIF_PROVIDER` must match the real name: `gcloud iam workload-identity-pools list --location=global --format='value(name)'` |

Concept notes:
- WIF security = the `--attribute-condition` scoping token exchange to your repo. Without it any GitHub repo could impersonate the deploy SA. The three GitHub values (project id, SA email, provider path) are not sensitive — that's why variables, not secrets.
- The `workloadIdentityUser` binding lives on the **service account's own** IAM policy (SA page → Permissions / Principals with access → Grant access, paste full `principalSet://…` as principal), not project-level IAM.
- SA-page tabs: **Permissions** = direct bindings on the SA; **Principals with access** = effective view incl. inheritance from project (Inheritance column).
- GCP service accounts are per-project resources — one project's `GCP_SA_KEY`/SA grants nothing in another project.

## 5. App architecture lessons

- **`MapStaticAssets()` manifest is baked at publish** — anything landing in `wwwroot` after `dotnet publish` isn't served (falls through to routing → auth redirect). Dockerfile must copy the Tailwind-built css into the build stage *before* publish. (problems/#-, Dockerfile comment)
- **DataProtection keys must persist to DB on Cloud Run** (`PersistKeysToDbContext`) — ephemeral instances otherwise rotate keys every deploy, killing all login cookies + antiforgery tokens.
- **Cloud Run terminates TLS** — ForwardedHeaders (XFP/XFF, cleared known-proxies) first in pipeline; https-redirect skipped when `DOTNET_RUNNING_IN_CONTAINER` is set.
- **DB logger must not log itself** — `DbLoggerProvider` writes over a dedicated `NpgsqlDataSource` + bounded background channel, never through `AppDbContext`, so EF's own command logging can't recurse.
- **Tailwind v4 preflight resets `button { cursor: default }`** — fixed once with a base-layer rule, not per-button utilities.
- **Parallel EF migrations**: when two branches both change the model, regenerate the later migration from the *merged* snapshot and read its `Up()` before landing — a stale snapshot produced a migration that duplicated another table's `CreateTable` (problems/#2).
- **`dotnet run` spawns a child server** — killing the `dotnet run` PID orphans it and the old binary keeps serving; kill by port: `lsof -ti :5063 | xargs kill`.
- Logout must be a POST form with antiforgery — a plain `<a href="/logout">` anchor silently did nothing (problems/#1).

## 6. Remaining / optional

- VPS Postgres hardening (`med-history-nvs.6`): TLS-only `pg_hba` (`hostssl`), strong password, firewall — Cloud Run egress IPs are dynamic, so the port is world-reachable; TLS + password is the personal-scale mitigation.
- Cost shape: min-instances 0 → scale-to-zero, ~$0 idle; cold start a few seconds on first request.
