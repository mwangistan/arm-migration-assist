# Frontend

React and Fluent UI operational dashboard for Feature 1 repository assessments.
It submits a public GitHub URL to the local assessment API and presents the
resulting evidence without calculating a readiness score.

## Run

Start the Feature 1 API from the repository root:

```pwsh
dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj -- serve
```

Then start Vite:

```pwsh
Set-Location frontend
npm install
npm run dev
```

Open `http://127.0.0.1:5173`.

## Dashboard coverage

- public GitHub repository intake with loading, cancellation, and error states
- repository identity and scan coverage
- technology inventory
- dependency architecture evidence
- architecture-sensitive code findings
- ARM64 build, CI, packaging, and Windows experience signals
- explicit unknowns and JSON export

## Verify

```pwsh
npm test
npm run typecheck
npm run lint
npm run build
```
