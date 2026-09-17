# WinUI 3 desktop client

Native Windows client for ARM Migration Assist. It runs alongside the React
frontend and calls the same `POST /assess` backend endpoint.

## Prerequisites

- Windows 10 version 1809 or later
- .NET 10 SDK
- Visual Studio 2026 with the WinUI application development workload, or the
  .NET SDK for command-line builds

The project uses Windows App SDK 2.4 and produces a self-contained, unpackaged
application.

## Run

Start the backend from the repository root:

```powershell
dotnet run --project .\backend\Assessment\ArmMigrationAssist.Api.csproj
```

In a second terminal, run the desktop client:

```powershell
dotnet run --project .\winui\ArmMigrationAssist.WinUI.csproj -p:Platform=x64
```

The default API base URL is `http://localhost:5285/`. It can be changed in the
application before starting an assessment.

## Publish

Publish for the machine architecture you need:

```powershell
dotnet publish .\winui\ArmMigrationAssist.WinUI.csproj -c Release -r win-x64 --self-contained
dotnet publish .\winui\ArmMigrationAssist.WinUI.csproj -c Release -r win-arm64 --self-contained
```

Published applications are unpackaged and do not require a separate Windows App
SDK runtime installation.
