# RUN_LOCAL

Local build, test, and run instructions for the AI Migration Planner (Feature 2).

## Prerequisites

- .NET 8 SDK (`dotnet --version` >= 8.0.100)
- Windows, macOS, or Linux
- No cloud credentials required for the default (`Fake`) provider

## Build

```powershell
cd backend/MigrationPlanner
dotnet build MigrationPlanner.sln
```

## Test

```powershell
cd backend/MigrationPlanner
dotnet test MigrationPlanner.sln
```

The integration test constructs a minimal valid `RepositoryAssessmentV1` payload in
code, POSTs it to `POST /api/migration-plans`, and asserts a `200 OK` whose plan
`assessmentId` echoes the request.

## Run the API

```powershell
cd backend/MigrationPlanner/src/MigrationPlanner.Api
dotnet run
```

The API listens on the default Kestrel port printed at startup. On startup the
service loads `knowledge/windows-on-arm/corpus.json`, verifies the SHA-256 of every
snippet file, and **refuses to start on mismatch**.

## Switching the planner model provider

Exactly one environment variable selects the `IPlannerModel` implementation:

```
MIGRATIONPLANNER_MODEL_PROVIDER
```

| Value    | Behavior                                                                                                             |
| -------- | -------------------------------------------------------------------------------------------------------------------- |
| `Fake`   | Deterministic canned `MigrationPlanV1`-shaped JSON keyed by `assessmentId`.                                          |
| `Hosted` | OpenAI-compatible hosted model with function calling. Requires the `Planner:Hosted` config or env overrides.         |
| `Phi`    | Azure AI Foundry Phi deployment via `Azure.AI.Inference`. Requires the `Planner:Phi` config or env overrides below.  |

Default when unset: `Fake`.

### Using the `Phi` provider (Azure AI Foundry)

The default deployment lives in the `PHAnomalyDetection-Dev` subscription
(`170ceb31-efaa-48f6-94f4-d3649c1f8369`), resource group
`rg-arm-migration-assist`, region `eastus2`:

- Foundry account: `foundry-arm-mig-assist`
- Inference endpoint: `https://foundry-arm-mig-assist.services.ai.azure.com/`
- Deployment name: `phi-4` (Model `Phi-4` version `7`, SKU `GlobalStandard`)

`appsettings.json` already carries these values under `Planner:Phi`. To run
locally against them with your own identity:

```powershell
az login
$env:MIGRATIONPLANNER_MODEL_PROVIDER = "Phi"
dotnet run --project src/MigrationPlanner.Api
```

`DefaultAzureCredential` picks up your `az login` token. The `Cognitive
Services User` role has already been granted on the account.

Override endpoint or deployment without editing config:

```powershell
$env:MIGRATIONPLANNER_PHI_ENDPOINT   = "https://<other-foundry>.services.ai.azure.com/"
$env:MIGRATIONPLANNER_PHI_DEPLOYMENT = "phi-4"
$env:MIGRATIONPLANNER_PHI_API_KEY    = "<only if not using DefaultAzureCredential>"
```

Example with the fake provider (no cloud):

```powershell
$env:MIGRATIONPLANNER_MODEL_PROVIDER = "Fake"
dotnet run --project src/MigrationPlanner.Api
```

## Runtime boundaries

- The repository-level React application in `frontend/` is the sole browser UI.
- No repository writes, shell execution, or outbound HTTP other than the
  selected model provider's calls to its configured endpoint.

## Deployed environment (Azure Container Apps)

A live copy of the API runs in the `PHAnomalyDetection-Dev` subscription
(`170ceb31-...`), resource group `rg-arm-migration-assist`, region `eastus2`:

| Resource | Value |
| -------- | ----- |
| Container registry | `acrarmmigassist.azurecr.io` |
| Image | `migration-planner-api:0.1.0` |
| Container Apps env | `cae-arm-migration-assist` |
| Container App | `ca-arm-migration-planner-api` |
| Public URL | `https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io` |
| Log Analytics | `law-arm-migration-assist` |
| Identity | System-assigned MI on the app |
| RBAC | `AcrPull` on ACR + `Cognitive Services User` on the Foundry account |

The app is configured with `MIGRATIONPLANNER_MODEL_PROVIDER=Phi` and points at
the Foundry Phi deployment described above; no API key is stored. Ingress is
external HTTPS on port 8080; `--min-replicas 0` so idle cost is zero.

### Rebuild and redeploy

```powershell
# From the repo root
az acr build -r acrarmmigassist -g rg-arm-migration-assist `
  -t migration-planner-api:<new-tag> -t migration-planner-api:latest `
  -f backend/MigrationPlanner/Dockerfile .

az containerapp update -n ca-arm-migration-planner-api -g rg-arm-migration-assist `
  --image acrarmmigassist.azurecr.io/migration-planner-api:<new-tag>
```

### Smoke test the deployed API

```powershell
$url = "https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io"
Invoke-RestMethod "$url/health"
Invoke-RestMethod "$url/api/migration-plans" -Method Post `
  -ContentType "application/json" `
  -Body (Get-Content -Raw backend/MigrationPlanner/tests/smoke-phi-assessment.json)
```

### Frontend integration (browser CORS)

The API allows browser origins listed in `Planner:AllowedOrigins` (or the
`MIGRATIONPLANNER_ALLOWED_ORIGINS` env var, comma-separated). Defaults for
local dev: `http://localhost:5173` (Vite) and `http://localhost:3000` (CRA).
The deployed Container App has the same two dev origins today; add the real
frontend origin before demo:

```powershell
az containerapp update -n ca-arm-migration-planner-api -g rg-arm-migration-assist `
  --set-env-vars "MIGRATIONPLANNER_ALLOWED_ORIGINS=http://localhost:5173,https://<your-fe>.azurestaticapps.net"
```

Example browser call from the React app:

```ts
const API_BASE = import.meta.env.VITE_MIGRATION_PLANNER_API ??
  "https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io";

export async function requestPlan(assessment: RepositoryAssessmentV1) {
  const res = await fetch(`${API_BASE}/api/migration-plans`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(assessment),
  });

  if (!res.ok) {
    // RFC 9457 Problem Details with a stable `code` field.
    const problem = await res.json();
    throw new PlannerError(problem.code, problem.title, problem.errors);
  }

  return (await res.json()) as PlanResponse; // { runId, plan, score, warnings }
}
```

Allowed methods on the API: `GET`, `POST`, `OPTIONS`.
Allowed request header: `Content-Type`. No credentials, no cookies.
