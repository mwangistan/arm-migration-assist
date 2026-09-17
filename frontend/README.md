# Frontend

Standalone React client for ARM Migration Assist, built with Vite.

It is kept outside the API project so this application surface can grow
independently as later features add planning, transformation, and validation
endpoints.

Current screens include:

- repository intake and migration-target selection;
- assessment overview and build signals;
- dependency compatibility and filtering;
- architecture-specific code findings;
- scanner coverage and unresolved evidence;
- printable report and JSON export.
- AI migration-plan generation from the completed assessment;
- recommended strategy, work items, risks, validation checks, and plan JSON export.

The client calls `POST /assess` and then passes the resulting
`RepositoryAssessmentV1` document to `POST /api/migration-plans` through
`src/api.js`. The backend proxies plan generation to the configured Feature 2
service so the browser does not depend on cross-origin access to that service.
The planner response is retained as `{ runId, plan, score, warnings }`, and the
Migration Plan tab renders the nested `plan` document.

## Run

Start the backend from the repository root:

```powershell
dotnet run --project .\backend\Assessment\ArmMigrationAssist.Api.csproj
```

Then start the frontend:

```powershell
cd .\frontend
npm install
npm run dev
```

Open `http://localhost:5173`. Vite proxies assessment and migration-plan
requests to `http://localhost:5285`.

For a separately hosted backend, copy `.env.example` to `.env.local` and set:

```text
VITE_API_BASE_URL=https://your-api.example.com
```
