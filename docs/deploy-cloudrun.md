# Deploy runbook: GCP Cloud Run

One-time GCP setup for med-history on Cloud Run. See [`plans/epic-cloudrun.md`](../plans/epic-cloudrun.md) for
the full architecture and gotchas. This doc only covers infra setup — app-level changes (Dockerfile,
DataProtection, proxy headers) are separate beads.

**Prerequisites:** `gcloud` CLI installed and authenticated (`gcloud auth login`), Docker installed locally
(for the first manual deploy in §5), a GCP billing account.

Run every command below in order; each section builds on `export`s from the ones before it.

```bash
export PROJECT_ID=medhistory
export REGION=asia-southeast1
```

## 1. One-time GCP project setup

```bash
# find your billing account id
gcloud billing accounts list

export BILLING_ACCOUNT_ID=XXXXXX-XXXXXX-XXXXXX

gcloud projects create $PROJECT_ID --name="Med History"
gcloud billing projects link $PROJECT_ID --billing-account=$BILLING_ACCOUNT_ID
gcloud config set project $PROJECT_ID

gcloud services enable \
  run.googleapis.com \
  artifactregistry.googleapis.com \
  secretmanager.googleapis.com
```

## 2. Artifact Registry

```bash
gcloud artifacts repositories create medhistory \
  --repository-format=docker \
  --location=$REGION \
  --description="med-history container images"

gcloud auth configure-docker ${REGION}-docker.pkg.dev
```

Image path used throughout this doc — tag by commit SHA (not `latest`), matching the CI convention
bead `nvs.5` will use (`$GITHUB_SHA` there, local `git rev-parse` here):

```bash
export IMAGE_TAG=$(git rev-parse HEAD)
export IMAGE="${REGION}-docker.pkg.dev/${PROJECT_ID}/medhistory/medhistory:${IMAGE_TAG}"
```

## 3. Secret Manager

Secret names match the app's env var names exactly (user convention — no translation layer between
Secret Manager and `IConfiguration`). Create the two secrets from stdin (keeps real values out of shell
history / files). Replace the placeholder values with the real VPS connection string and app login
password before running.

```bash
printf '%s' "Host=<vps-host>;Port=5432;Database=medhistory;Username=medhistory;Password=<vps-pw>;SSL Mode=Require;Trust Server Certificate=true" \
  | gcloud secrets create ConnectionStrings__Default --data-file=- --replication-policy=automatic

printf '%s' "<app-login-password>" \
  | gcloud secrets create Auth__Password --data-file=- --replication-policy=automatic
```

Create a dedicated Cloud Run runtime service account (least privilege — don't rely on the project's
default compute SA) and grant it read access to both secrets:

```bash
gcloud iam service-accounts create medhistory-run \
  --display-name="med-history Cloud Run runtime"

export RUN_SA="medhistory-run@${PROJECT_ID}.iam.gserviceaccount.com"

gcloud secrets add-iam-policy-binding ConnectionStrings__Default \
  --member="serviceAccount:${RUN_SA}" \
  --role="roles/secretmanager.secretAccessor"

gcloud secrets add-iam-policy-binding Auth__Password \
  --member="serviceAccount:${RUN_SA}" \
  --role="roles/secretmanager.secretAccessor"
```

## 4. Deploy service account + key for GitHub Actions

Auth is a service-account JSON key in a GitHub repo secret (matches the user's existing pipeline
convention) — not Workload Identity Federation.

```bash
export DEPLOY_SA=medhistory-deployer
export DEPLOY_SA_EMAIL="${DEPLOY_SA}@${PROJECT_ID}.iam.gserviceaccount.com"

gcloud iam service-accounts create $DEPLOY_SA \
  --display-name="med-history GitHub Actions deployer"

gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:${DEPLOY_SA_EMAIL}" \
  --role="roles/run.admin"

gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:${DEPLOY_SA_EMAIL}" \
  --role="roles/artifactregistry.writer"

# lets the deployer attach/impersonate the Cloud Run runtime SA on deploy
gcloud iam service-accounts add-iam-policy-binding $RUN_SA \
  --member="serviceAccount:${DEPLOY_SA_EMAIL}" \
  --role="roles/iam.serviceAccountUser"

gcloud iam service-accounts keys create key.json \
  --iam-account=$DEPLOY_SA_EMAIL
```

Paste the full contents of `key.json` into a GitHub repo secret named `GCP_SA_KEY`
(repo → Settings → Secrets and variables → Actions → New repository secret), then delete the local
copy immediately — it's a live credential, don't let it sit on disk or land in shell history:

```bash
rm key.json
```

This key has no GCP-enforced expiry. Rotate it manually roughly once a year (create a new key, update
the `GCP_SA_KEY` repo secret, then `gcloud iam service-accounts keys delete <old-key-id>
--iam-account=$DEPLOY_SA_EMAIL`), and immediately if it's ever exposed.

## 5. First manual deploy (before CI exists)

```bash
docker build -t $IMAGE .
docker push $IMAGE

gcloud run deploy medhistory \
  --image=$IMAGE \
  --region=$REGION \
  --service-account=$RUN_SA \
  --min-instances=0 \
  --max-instances=1 \
  --memory=512Mi \
  --port=8080 \
  --allow-unauthenticated \
  --set-secrets=ConnectionStrings__Default=ConnectionStrings__Default:latest,Auth__Password=Auth__Password:latest
```

`--allow-unauthenticated` is correct here — this exposes the Cloud Run URL publicly, but the app has its
own login-password gate (`Auth__Password`) in front of every route, so it's not an open service.

## 6. Values for the GitHub Actions workflow (bead `nvs.5`)

The SA key itself is not a workflow input — it's already in the `GCP_SA_KEY` repo secret from §4 and the
workflow reads it from there (e.g. via `google-github-actions/auth`).

| Value | Source | Example |
|---|---|---|
| Project ID | `$PROJECT_ID` | `medhistory` |
| Deploy SA email | `$DEPLOY_SA_EMAIL` | `medhistory-deployer@medhistory.iam.gserviceaccount.com` |
| Runtime SA (for `--service-account` on deploy) | `$RUN_SA` | `medhistory-run@medhistory.iam.gserviceaccount.com` |
| Region | — | `asia-southeast1` |
| Artifact Registry repo | — | `medhistory` |
| Cloud Run service name | — | `medhistory` |
| Image tag | — | `$GITHUB_SHA` (commit SHA, not `latest`) |

## 7. Checklist — things only the user can do

- [ ] GCP billing account exists and is linked to the project (§1).
- [ ] Repo pushed to GitHub, and `key.json` from §4 pasted into the repo's `GCP_SA_KEY` Actions secret —
      required before `nvs.5`'s workflow can authenticate.
- [ ] VPS Postgres hardened for public TLS access: `hostssl`-only `pg_hba.conf` entry, strong password,
      port reachable from Cloud Run's dynamic egress IPs — see epic gotcha #4 in
      [`plans/epic-cloudrun.md`](../plans/epic-cloudrun.md); the `postgresql-vps-agent` skill can do this.
- [ ] Secrets in §3 created with real values, not the placeholder strings shown above.
- [ ] DB migrations (including the DataProtection keys table from bead `nvs.2`) applied manually via
      `dotnet ef database update` run locally against the VPS — not auto-applied on startup.
- [ ] DNS / custom domain (optional) — Cloud Run gives a working `*.run.app` URL by default; only needed
      if a custom domain is wanted, via `gcloud run domain-mappings create`.
