# Frontend

Browser client for ARM Migration Assist.

The current implementation is dependency-free HTML, CSS, and JavaScript. It is
kept outside the API project so this application surface can grow independently
as later features add planning, transformation, and validation endpoints.

Current screens include:

- repository intake and migration-target selection;
- assessment overview and build signals;
- dependency compatibility and filtering;
- architecture-specific code findings;
- scanner coverage and unresolved evidence;
- printable report and JSON export.

The client currently calls `POST /assess`. Future Feature 2-4 endpoint clients
should remain in this directory rather than being embedded in backend code.

During development, `backend/Program.cs` serves this directory directly. The
backend project also copies these files into `wwwroot` during `dotnet publish`,
so the deployed API remains a self-contained application.
