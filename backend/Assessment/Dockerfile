# syntax=docker/dockerfile:1
#
# Feature 1 (Repository Assessment Engine) backend image.
#
# Two runtime dependencies drive the design of this image:
#   1. The ingestion service shells out to the `git` executable (git clone/fetch),
#      so `git` MUST be present on PATH at runtime.
#   2. The dependency scanner optionally invokes Microsoft component-detection to
#      improve SBOM coverage; the linux-x64 self-contained binary is bundled and
#      exposed through COMPONENT_DETECTION_PATH.

# ---- build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching) - only the project file is needed.
COPY ArmMigrationAssist.Api.csproj ./
RUN dotnet restore ArmMigrationAssist.Api.csproj

# Copy the rest of the backend and publish a framework-dependent build.
COPY . .
RUN dotnet publish ArmMigrationAssist.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Pin the component-detection release so image builds are reproducible.
ARG COMPONENT_DETECTION_VERSION=v7.1.13

# git is a hard runtime requirement; curl/ca-certificates are needed to fetch the
# component-detection binary and to reach github.com/nuget.org/pypi.org/npm at runtime.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

# Bundle Microsoft component-detection (optional SBOM scanner). If this download
# ever fails, the API still runs and falls back to its built-in manifest parsers.
RUN curl -fsSL -o /usr/local/bin/component-detection \
      "https://github.com/microsoft/component-detection/releases/download/${COMPONENT_DETECTION_VERSION}/component-detection-linux-x64" \
    && chmod +x /usr/local/bin/component-detection

# Configuration the app reads at startup:
#   COMPONENT_DETECTION_PATH     - explicit path to the SBOM scanner
#   ARM_MIGRATION_WORKSPACE_ROOT - writable clone/cache root (avoid the read-only app dir)
ENV COMPONENT_DETECTION_PATH=/usr/local/bin/component-detection \
    ARM_MIGRATION_WORKSPACE_ROOT=/tmp/arm-ma \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

WORKDIR /app
COPY --from=build /app/publish ./

# The aspnet image defaults to listening on 8080; Container Apps ingress targets 8080.
EXPOSE 8080
ENTRYPOINT ["dotnet", "ArmMigrationAssist.Api.dll"]
