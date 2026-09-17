# CI/CD

GitHub Actions independently deliver the two Container App APIs and the Static
Web Apps frontend, so updating one service does not replace another.

| Workflow | Trigger | What it does |
| --- | --- | --- |
| [`ci.yml`](../.github/workflows/ci.yml) | PR + push to `main` + manual | `dotnet restore/build/test`, hadolint on the Dockerfile, `az bicep build` on the IaC. |
| [`cd.yml`](../.github/workflows/cd.yml) | Push to `main` under `backend/MigrationPlanner/**` or `knowledge/windows-on-arm/**`, or manual dispatch | `az acr build` a new image tagged with the git SHA, then `az containerapp update` to roll the app, then hit `/health`. |
| [`assessment-cd.yml`](../.github/workflows/assessment-cd.yml) | Push to `main` under `backend/Assessment/**`, or manual dispatch | Builds the read-only assessment API image, updates its existing Container App, then hits `/api/health`. |
| [`frontend-static-web-app.yml`](../.github/workflows/frontend-static-web-app.yml) | Frontend changes on PR/push to `main`, or manual dispatch | Verifies React, injects both API origins, and deploys `frontend/dist` to the existing Static Web App. |
| [`infra.yml`](../.github/workflows/infra.yml) | Push to `main` under `infra/**` (deploy), PR under `infra/**` (what-if), or manual dispatch | `az deployment group create` (or what-if) against [`infra/main.bicep`](../infra/main.bicep). |

Authentication is **GitHub OIDC federation** — no client secret is stored in the repo.

## One-time setup

### 1. Create an Entra ID app registration for the workflows

```powershell
$app = az ad app create --display-name "gh-arm-migration-assist" | ConvertFrom-Json
$sp  = az ad sp create --id $app.appId | ConvertFrom-Json
$appId = $app.appId
```

### 2. Grant the SP rights on the resource group

The workflows need to build images in ACR, roll the Container App, and (for `infra.yml`) create/modify resources plus assign roles. Grant `Contributor` and `User Access Administrator` scoped to the resource group only:

```powershell
$sub = "170ceb31-efaa-48f6-94f4-d3649c1f8369"
$rg  = "rg-arm-migration-assist"
$scope = "/subscriptions/$sub/resourceGroups/$rg"

az role assignment create --assignee $appId --role "Contributor"                --scope $scope
az role assignment create --assignee $appId --role "User Access Administrator" --scope $scope
```

`User Access Administrator` is required because [`infra/main.bicep`](../infra/main.bicep) creates the `AcrPull` and `Cognitive Services User` role assignments on the app's user-assigned MI.

### 3. Add federated credentials on the app registration

Bind the app to this GitHub repo. Repeat for each ref pattern you need:

```powershell
$repo = "mwangistan/arm-migration-assist"

# Pushes to main (used by cd.yml and infra.yml deploy job)
az ad app federated-credential create --id $appId --parameters (@{
  name        = "gh-main-branch"
  issuer      = "https://token.actions.githubusercontent.com"
  subject     = "repo:$repo`:ref:refs/heads/main"
  audiences   = @("api://AzureADTokenExchange")
} | ConvertTo-Json -Compress)

# Pull requests (used by infra.yml what-if job)
az ad app federated-credential create --id $appId --parameters (@{
  name        = "gh-pull-requests"
  issuer      = "https://token.actions.githubusercontent.com"
  subject     = "repo:$repo`:pull_request"
  audiences   = @("api://AzureADTokenExchange")
} | ConvertTo-Json -Compress)

# Production environment (used by cd.yml + infra.yml deploy job — see step 4)
az ad app federated-credential create --id $appId --parameters (@{
  name        = "gh-env-production"
  issuer      = "https://token.actions.githubusercontent.com"
  subject     = "repo:$repo`:environment:production"
  audiences   = @("api://AzureADTokenExchange")
} | ConvertTo-Json -Compress)
```

### 4. Create the `production` GitHub environment

The `cd.yml` and `infra.yml` deploy jobs are pinned to a `production` environment. Under **Settings → Environments → New environment → production** you can optionally add required reviewers to force a manual approval before every deploy.

### 5. Set repo secrets and variables

Under **Settings → Secrets and variables → Actions**:

Secrets (used by `azure/login@v2`):

| Name | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | `$appId` from step 1 |
| `AZURE_TENANT_ID` | `72f988bf-86f1-41af-91ab-2d7cd011db47` |
| `AZURE_SUBSCRIPTION_ID` | `170ceb31-efaa-48f6-94f4-d3649c1f8369` |

Repository variables (non-secret; edit here to point at a different environment):

| Name | Value |
| --- | --- |
| `AZURE_RESOURCE_GROUP` | `rg-arm-migration-assist` |
| `AZURE_LOCATION` | `eastus2` |
| `ACR_NAME` | `acrarmmigassist` |
| `CONTAINER_APP_NAME` | `ca-arm-migration-planner-api` |
| `ASSESSMENT_CONTAINER_APP_NAME` | Existing Feature 1 Container App name |
| `ASSESSMENT_API_URL` | Feature 1 Container App HTTPS origin |
| `MIGRATION_PLANNER_API_URL` | Feature 2 Container App HTTPS origin |
| `STATIC_WEB_APP_ORIGIN` | Static Web App HTTPS origin allowed by both APIs |

Set `AZURE_STATIC_WEB_APPS_API_TOKEN` as an Actions secret using the existing
Static Web App deployment token. Set the Static Web App origin in both backend
CORS configurations before deploying the frontend.

## Adopting the Bicep template against the live environment

The current Container App uses a **system-assigned** managed identity. [`infra/main.bicep`](../infra/main.bicep) switches to a **user-assigned** MI so RBAC can be granted before the app exists (breaking the AcrPull chicken-and-egg cycle at first deploy). Two options:

- **Recommended:** Run `infra.yml` once and let Bicep create a parallel user-assigned MI, grant the roles, and update the existing app to use it. Bicep is idempotent — subsequent runs are no-ops.
- Or run `az deployment group what-if` locally first to preview the diff:
  ```powershell
  az deployment group what-if `
    -g rg-arm-migration-assist `
    -f infra/main.bicep `
    -p infra/main.parameters.json
  ```

## Local equivalents

Build & test:

```powershell
cd backend/MigrationPlanner
dotnet test MigrationPlanner.sln
```

Build image and deploy (matches what `cd.yml` does):

```powershell
az acr build -r acrarmmigassist -g rg-arm-migration-assist `
  -t migration-planner-api:$(git rev-parse --short=12 HEAD) `
  -t migration-planner-api:latest `
  -f backend/MigrationPlanner/Dockerfile .

az containerapp update -n ca-arm-migration-planner-api -g rg-arm-migration-assist `
  --image acrarmmigassist.azurecr.io/migration-planner-api:$(git rev-parse --short=12 HEAD)
```
