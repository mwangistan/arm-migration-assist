# syntax=docker/dockerfile:1.7
#
# Single-image build for the ArmMigrationAssist composed host that surfaces all four
# services (Assessment, Migration Planner, Automated Migration, Validation) on one
# ASP.NET Core process.
#
# Build context: repo root.
#   docker build -f Dockerfile -t arm-migration-assist:local .
#
# The runtime stage uses the .NET SDK image (not aspnet) because Validation approves
# `dotnet build/test` commands at request time, and needs `dotnet`, `git`, and MSBuild
# available inside the container.

ARG DOTNET_VERSION=8.0

########################################
# Stage 1: restore + publish
########################################
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY backend/MigrationPlanner/Directory.Build.props    backend/MigrationPlanner/
COPY backend/MigrationPlanner/Directory.Packages.props backend/MigrationPlanner/
COPY backend/MigrationPlanner/MigrationPlanner.sln    backend/MigrationPlanner/

COPY backend/Assessment/         backend/Assessment/
COPY backend/AutomatedMigration/ backend/AutomatedMigration/
COPY backend/AutomatedMigration.Api/ backend/AutomatedMigration.Api/
COPY backend/MigrationPlanner/src/       backend/MigrationPlanner/src/
COPY backend/MigrationPlanner/contracts/ backend/MigrationPlanner/contracts/
COPY backend/Validation/         backend/Validation/
COPY backend/ArmMigrationAssist.Api/ backend/ArmMigrationAssist.Api/
COPY knowledge/                  knowledge/

RUN dotnet restore backend/ArmMigrationAssist.Api/ArmMigrationAssist.Api.csproj

RUN dotnet publish backend/ArmMigrationAssist.Api/ArmMigrationAssist.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

########################################
# Stage 2: runtime
########################################
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS runtime

# git: required by F3 publishing paths and F4 GitRepositoryInspector.
# curl: HEALTHCHECK.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Fixed UID/GID so persistent volumes (F4 storage root) have stable permissions.
RUN groupadd --gid 10001 armmigration \
    && useradd --uid 10001 --gid armmigration --shell /usr/sbin/nologin --create-home armmigration

WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/knowledge/windows-on-arm /app/knowledge/windows-on-arm

RUN mkdir -p /data/validation-api \
    && chown -R armmigration:armmigration /data/validation-api /app

USER armmigration

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    MIGRATIONPLANNER_CORPUS_ROOT=/app/knowledge/windows-on-arm \
    ValidationApi__StorageRoot=/data/validation-api

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "ArmMigrationAssist.Api.dll"]
