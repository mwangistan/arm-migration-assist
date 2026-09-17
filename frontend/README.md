# Frontend

React and Fluent UI operational dashboard for repository assessment and
migration planning. It submits a GitHub URL, streams assessment progress,
automatically plans the migration, and presents the evidence, readiness score,
work items, and acceptance criteria in one report.

## Run

Start the Feature 1 API from the repository root:

```pwsh
dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj -- serve
```

Start the Feature 2 API in another terminal:

```pwsh
dotnet run --project backend/MigrationPlanner/src/MigrationPlanner.Api/MigrationPlanner.Api.csproj
```

Then start Vite:

```pwsh
Set-Location frontend
npm install
npm run dev
```

Open `http://127.0.0.1:5173`.

Vite proxies assessment requests to `http://127.0.0.1:5000` and migration plan
requests to `http://127.0.0.1:5080`. For a deployed build, set
`VITE_ASSESSMENT_API_URL` and `VITE_MIGRATION_PLANNER_API_URL` to their HTTPS
origins.

## Dashboard coverage

- GitHub repository intake with loading, cancellation, and error states
- automatic Git Credential Manager browser sign-in for protected repositories
- repository identity and scan coverage
- technology inventory
- dependency architecture evidence
- architecture-sensitive code findings
- ARM64 build, CI, packaging, and Windows experience signals
- explicit unknowns, print-ready reports, and JSON export
- automatic readiness scoring and migration planning
- Feature 3-compatible plan export and Markdown/HTML report downloads

Authentication is initiated only after anonymous Git access fails. The existing
dashboard remains the sole UI: it displays sign-in progress in the assessment
status area and automatically retries after success. No token or account data is
entered into or rendered by the web application.

## Azure Static Web Apps

The `frontend-static-web-app.yml` workflow verifies the frontend on pull
requests and deploys the production build from `main`. Configure these GitHub
repository settings before enabling deployment:

| Setting | Kind | Value |
|---------|------|-------|
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | Actions secret | Deployment token from the Static Web App |
| `ASSESSMENT_API_URL` | Actions variable | HTTPS origin of the deployed assessment API |
| `MIGRATION_PLANNER_API_URL` | Actions variable | HTTPS origin of the deployed planner API |

Add the Static Web App origin to the assessment API's `DashboardOrigins`
configuration (semicolon-separated) and the planner API's
`MIGRATIONPLANNER_ALLOWED_ORIGINS` configuration (comma-separated). The
deployed dashboard can assess public GitHub repositories. Git Credential
Manager sign-in for protected repositories intentionally remains a local,
loopback-only workflow; it is not exposed by the cloud deployment.

## Verify

```pwsh
npm test
npm run typecheck
npm run lint
npm run build
```
