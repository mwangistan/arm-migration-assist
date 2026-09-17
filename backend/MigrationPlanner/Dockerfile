# syntax=docker/dockerfile:1.7

# Build the MigrationPlanner API as a self-contained multi-stage image.
# Build context: repo root (needs both backend/MigrationPlanner and knowledge/).
# Invoke:  docker build -f backend/MigrationPlanner/Dockerfile -t <tag> .

ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY backend/MigrationPlanner/Directory.Build.props     backend/MigrationPlanner/
COPY backend/MigrationPlanner/Directory.Packages.props  backend/MigrationPlanner/
COPY backend/MigrationPlanner/MigrationPlanner.sln      backend/MigrationPlanner/
COPY backend/MigrationPlanner/src/                      backend/MigrationPlanner/src/
COPY backend/MigrationPlanner/contracts/                backend/MigrationPlanner/contracts/

RUN dotnet restore backend/MigrationPlanner/src/MigrationPlanner.Api/MigrationPlanner.Api.csproj

RUN dotnet publish backend/MigrationPlanner/src/MigrationPlanner.Api/MigrationPlanner.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY knowledge/windows-on-arm/ /app/knowledge/windows-on-arm/

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    MIGRATIONPLANNER_CORPUS_ROOT=/app/knowledge/windows-on-arm

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "MigrationPlanner.Api.dll"]
