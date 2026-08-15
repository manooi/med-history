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
  secretmanager.googleapis.com \
  iamcredentials.googleapis.com
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

## 4. Deploy service account + Workload Identity Federation for GitHub Actions

Auth is keyless: GitHub Actions exchanges a short-lived OIDC token for GCP credentials via Workload
Identity Federation (WIF) — no long-lived service-account key ever leaves GCP, so there's nothing to
paste into a secret and nothing to rotate.

```bash
export DEPLOY_SA=medhistory-deployer
export DEPLOY_SA_EMAIL="${DEPLOY_SA}@${PROJECT_ID}.iam.gserviceaccount.com"
export GH_REPO=<github-org-or-user>/<repo-name>

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
```

Create the workload identity pool and an OIDC provider trusting GitHub's token issuer:

```bash
export PROJECT_NUMBER=$(gcloud projects describe $PROJECT_ID --format='value(projectNumber)')

gcloud iam workload-identity-pools create github \
  --location=global \
  --display-name="GitHub Actions"

gcloud iam workload-identity-pools providers create-oidc github \
  --location=global \
  --workload-identity-pool=github \
  --display-name="GitHub Actions provider" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository" \
  --attribute-condition="assertion.repository == '${GH_REPO}'"
```

The `--attribute-condition` is mandatory, not optional hardening — without it, any GitHub repo anywhere
could mint a token that impersonates the deploy SA. It scopes token exchange to this repo only.

Grant that repo's identity permission to impersonate the deploy SA:

```bash
gcloud iam service-accounts add-iam-policy-binding $DEPLOY_SA_EMAIL \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/github/attribute.repository/${GH_REPO}"
```

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

Nothing secret is stored in GitHub for auth — the workflow authenticates via the WIF provider and
deploy SA set up in §4, both of which are safe to expose as repo *variables* (Settings → Secrets and
variables → Actions → Variables tab), not secrets.

| Value | Source | Example |
|---|---|---|
| `GCP_WIF_PROVIDER` variable | full provider resource name from §4 | `projects/123456789012/locations/global/workloadIdentityPools/github/providers/github` |
| `GCP_DEPLOY_SA` variable | `$DEPLOY_SA_EMAIL` | `medhistory-deployer@medhistory.iam.gserviceaccount.com` |
| `GCP_PROJECT_ID` variable | `$PROJECT_ID` | `medhistory` |
| Runtime SA (for `--service-account` on deploy) | `$RUN_SA` | `medhistory-run@medhistory.iam.gserviceaccount.com` |
| Region | — | `asia-southeast1` |
| Artifact Registry repo | — | `medhistory` |
| Cloud Run service name | — | `medhistory` |
| Image tag | — | `$GITHUB_SHA` (commit SHA, not `latest`) |

## 7. Checklist — things only the user can do

- [ ] GCP billing account exists and is linked to the project (§1).
- [ ] Repo pushed to GitHub, and the `GCP_WIF_PROVIDER` and `GCP_DEPLOY_SA` repo variables from §4/§6 set —
      required before `nvs.5`'s workflow can authenticate.
- [ ] VPS Postgres hardened for public TLS access: `hostssl`-only `pg_hba.conf` entry, strong password,
      port reachable from Cloud Run's dynamic egress IPs — see epic gotcha #4 in
      [`plans/epic-cloudrun.md`](../plans/epic-cloudrun.md); the `postgresql-vps-agent` skill can do this.
- [ ] Secrets in §3 created with real values, not the placeholder strings shown above.
- [ ] DB migrations (including the DataProtection keys table from bead `nvs.2`) applied manually via
      `dotnet ef database update` run locally against the VPS — not auto-applied on startup.
- [ ] DNS / custom domain (optional) — Cloud Run gives a working `*.run.app` URL by default; only needed
      if a custom domain is wanted, via `gcloud run domain-mappings create`.
