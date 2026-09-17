# Feature 1 — Deploy runbook (backend Container App + frontend Static Web App)

Target: **rg-arm-migration-assist** / **PHAnomalyDetection-Dev** (eastus2), reusing the
existing `acrarmmigassist` registry and `cae-arm-migration-assist` Container Apps environment.

Prerequisites: `az` (logged in, able to write to `~/.azure`), `node`/`npm`, `npx`.
Docker is **not** required — the image is built server-side by `az acr build`.

## Quick path — use the scripts

Three PowerShell scripts under `infra/`. The backend and frontend deploy **independently**:

```powershell
# Backend only (image build + Container App). Bump -ImageTag to ship a new image.
.\infra\deploy-backend.ps1
.\infra\deploy-backend.ps1 -SkipBuild        # redeploy without rebuilding the image
.\infra\deploy-backend.ps1 -ImageTag v2

# Frontend only (Static Web App + SPA). -SetBackendCors also allows the SWA origin in backend CORS.
.\infra\deploy-frontend.ps1
.\infra\deploy-frontend.ps1 -SkipInfra       # SWA exists: just rebuild + publish the SPA
.\infra\deploy-frontend.ps1 -SetBackendCors

# One-shot orchestrator: backend, then frontend, then wire CORS.
.\infra\deploy.ps1
.\infra\deploy.ps1 -SkipBackend              # frontend only
.\infra\deploy.ps1 -SkipFrontend             # backend only
```

Bicep is split to match: `infra/backend.bicep` (Container App) and `infra/frontend.bicep`
(Static Web App). Override defaults with `-CreatedBy`, `-ImageTag`, `-SubscriptionId`, etc.
The manual commands below are the same steps, for reference or one-off runs.

Run all commands from the repository root.

```powershell
# ---- shared variables -------------------------------------------------------
$sub        = "170ceb31-efaa-48f6-94f4-d3649c1f8369"   # PHAnomalyDetection-Dev
$rg         = "rg-arm-migration-assist"
$acr        = "acrarmmigassist"
$imageTag   = "v1"
$image      = "arm-migration-assessment-api:$imageTag"
$planner    = "https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io"
$createdBy  = "schisiya@microsoft.com"                 # edit to your identity (policy tag)

az login
az account set --subscription $sub
```

## Phase A — build image + deploy backend

```powershell
# 1. Build & push the backend image (server-side, no local Docker needed).
az acr build --registry $acr --image $image backend/Assessment

# 2. Preview the backend changes (best practice).
az deployment group what-if `
  --resource-group $rg --template-file infra/backend.bicep `
  --parameters migrationPlannerBaseUrl=$planner createdBy=$createdBy imageTag=$imageTag

# 3. Deploy the backend Container App.
az deployment group create `
  --resource-group $rg --name feature1-backend --template-file infra/backend.bicep `
  --parameters migrationPlannerBaseUrl=$planner createdBy=$createdBy imageTag=$imageTag

# 4. Capture the backend URL.
$backendUrl = az deployment group show -g $rg -n feature1-backend --query properties.outputs.backendUrl.value -o tsv
"backend = $backendUrl"
```

## Phase B — deploy SWA, build the SPA against the backend, publish, enable CORS

```powershell
# 5. Create the Static Web App.
az deployment group create `
  --resource-group $rg --name feature1-frontend --template-file infra/frontend.bicep `
  --parameters createdBy=$createdBy
$swaName = az deployment group show -g $rg -n feature1-frontend --query properties.outputs.staticWebAppName.value -o tsv
$swaUrl  = az deployment group show -g $rg -n feature1-frontend --query properties.outputs.staticWebAppUrl.value -o tsv

# 6. Build the SPA with the backend URL baked in (Vite reads this at build time).
Push-Location frontend
npm install
$env:VITE_API_BASE_URL = $backendUrl
npm run build
Pop-Location

# 7. Publish the built assets to the Static Web App.
$token = az staticwebapp secrets list -n $swaName -g $rg --query "properties.apiKey" -o tsv
npx -y @azure/static-web-apps-cli deploy ".\frontend\dist" --deployment-token $token --env production

# 8. Allow the SWA origin through the backend's CORS policy (targeted env-var update).
az containerapp update -n ca-arm-migration-assessment-api -g $rg --set-env-vars "FrontendOrigins__0=$swaUrl"
```

## Verify

```powershell
# Backend is up (a 404 at the root path is expected — it has no root route).
curl.exe -i $backendUrl

# Container App revision health.
az containerapp revision list -n ca-arm-migration-assessment-api -g $rg -o table

# End-to-end assessment call.
$body = '{"repoUrl":"https://github.com/octocat/Hello-World","target":"Arm64Native"}'
curl.exe -s -X POST "$backendUrl/assess" -H "content-type: application/json" -d $body

# Open the dashboard.
Start-Process $swaUrl
```

## Notes

- **Cold start:** the backend scales to zero (`minReplicas: 0`); the first request wakes it,
  then performs the clone. Set `minReplicas: 1` in `infra/backend.bicep` if you want it always warm.
- **Rebuilding the backend:** `.\infra\deploy-backend.ps1 -ImageTag v2` (build + deploy in one step).
- **ACR auth:** the app pulls via a user-assigned managed identity (`id-ca-arm-migration-assessment-api`)
  granted `AcrPull`, so no registry credentials are stored anywhere.
- **Planner dependency:** the backend requires `MigrationPlanner__BaseUrl` at startup; it is wired to
  the existing `ca-arm-migration-planner-api` app.
