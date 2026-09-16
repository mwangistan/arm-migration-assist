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

The client currently calls `POST /assess` through `src/api.js`. Future Feature
2-4 endpoint clients should be added to that API layer rather than embedded in
UI components.

## Run

Start the backend from the repository root:

```powershell
dotnet run --project .\backend\ArmMigrationAssist.Api.csproj
```

Then start the frontend:

```powershell
cd .\frontend
npm install
npm run dev
```

Open `http://localhost:5173`. Vite proxies assessment requests to
`http://localhost:5285`.

For a separately hosted backend, copy `.env.example` to `.env.local` and set:

```text
VITE_API_BASE_URL=https://your-api.example.com
```
