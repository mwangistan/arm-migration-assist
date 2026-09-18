# Migration Workspace Frontend

React and Fluent UI operational dashboard for repository assessment and
migration planning. It submits a GitHub URL, streams assessment progress,
automatically plans the migration, and presents the evidence, readiness score,
work items, and acceptance criteria in one report.

## Architecture

The frontend is the product entrypoint for the whole workflow, not a module-
specific dashboard. Its real-state workflow rail covers Connect, Assess, Plan,
Transform, and Validate. It creates assessment jobs, consumes SSE with polling
fallback, sends the completed assessment unchanged to the planner, and exposes
Feature 3-compatible plan and report artifacts. Business rules remain in the
backend contracts and validators.

See [the end-to-end architecture](../docs/ARCHITECTURE.md).

## Run

Start the assessment API from the repository root:

```pwsh
dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj -- serve
```

Start the migration planner API in another terminal:

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
requests to `http://127.0.0.1:5080` in local dev. In production the Static Web
App proxies `/api/*` to the composed host through
[`public/staticwebapp.config.json`](public/staticwebapp.config.json), so the
frontend calls same-origin paths and no API origins are baked into the build.

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
requests and deploys the production build from `main`. Configure this GitHub
repository setting before enabling deployment:

| Setting | Kind | Value |
|---------|------|-------|
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | Actions secret | Deployment token from the Static Web App |

The Static Web App's `staticwebapp.config.json` proxies `/api/*` to the
composed host at
`ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io`,
so the frontend never calls the ACA origin directly and no API URL vars are
needed. Add the Static Web App origin to the composed host's `DashboardOrigins`,
`MIGRATIONPLANNER_ALLOWED_ORIGINS`, and `AUTOMATION_ALLOWED_ORIGINS` env vars so
its own CORS layer accepts the proxied preflights. The deployed dashboard can
assess public GitHub repositories. Git Credential Manager sign-in for protected
repositories intentionally remains a local, loopback-only workflow; it is not
exposed by the cloud deployment.

## Verify

```pwsh
npm test
npm run typecheck
npm run lint
npm run build
```
