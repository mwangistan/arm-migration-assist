# syntax=docker/dockerfile:1
#
# Production image for the Validation.Api ASP.NET Core service.
#
# Build context: backend/Validation (NOT backend/Validation/Api). This lets the
# published image include the Validation.Api.csproj's ProjectReference to the
# sibling ../Validation.csproj engine project.
#
#   docker build -f backend/Validation/Api/Dockerfile -t validation-api:local backend/Validation
#
# Runtime tooling note (Linux-only):
# ValidationExecutor/ProcessRunner run engine-approved commands as child processes of
# this API at request time -- not only at image-build time. To make the "run" endpoint
# usable inside the container itself, the FINAL stage intentionally keeps the full
# .NET SDK (not just the aspnet runtime) so `dotnet build`/`dotnet test`/`msbuild`
# remain available to approved commands, plus `git` for GitRepositoryInspector.
# This container can only satisfy Linux/.NET-shaped approved commands:
#   - OK:      `dotnet build/test --runtime linux-x64|linux-arm64 ...`, `git ...`
#   - NOT OK:  `.vcxproj`/MSBuild ARM64 Windows checks, native Windows ARM64 smoke
#              executables, or anything requiring a Windows host/toolchain -- those
#              approved commands will report not-run/inconclusive/failed here and
#              must be executed on a Windows ARM64 runner instead, not in this image.
#   - NOT INCLUDED: a Docker CLI/daemon. `containerSmokeChecks`/Dockerfile-based
#     validation commands need `docker build`/`docker run` against a Docker daemon,
#     which this image does not provide (mounting the host daemon socket would grant
#     this API host-level control and is deliberately not done here). Those commands
#     remain not-run/inconclusive when approved and executed from inside this image.

########################################
# Stage 1: restore + publish with the SDK.
########################################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only project files first so `dotnet restore` is cached across source-only changes.
COPY Validation.csproj ./
COPY Api/Validation.Api.csproj ./Api/
# Optional enterprise feed configuration is mounted only during restore, never copied
# into a build layer or runtime image.
RUN --mount=type=secret,id=nuget_config \
    if [ -f /run/secrets/nuget_config ]; then \
        dotnet restore ./Api/Validation.Api.csproj --configfile /run/secrets/nuget_config; \
    else \
        dotnet restore ./Api/Validation.Api.csproj; \
    fi

# Copy the rest of the (already-minimized, see ../.dockerignore) build context.
COPY . .
RUN dotnet publish ./Api/Validation.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

########################################
# Stage 2: runtime. Uses the SDK image (see note above) so approved dotnet/msbuild/git
# commands remain executable by the running API, not merely by this build.
########################################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final

# git: required by GitRepositoryInspector for approved `git` commands.
# curl: required by the HEALTHCHECK below.
# Installed as root, before the USER switch further down.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git curl \
    && rm -rf /var/lib/apt/lists/*

# Fixed UID/GID for persistent volume permissions.
RUN groupadd --gid 10001 validation-api \
    && useradd --uid 10001 --gid validation-api --shell /usr/sbin/nologin --create-home validation-api

WORKDIR /app
COPY --from=build /app/publish .

# Default, mountable storage root for plan/run metadata (see ValidationApi__StorageRoot
# below). Pre-created and owned by the non-root user so the default path works
# out of the box; a host/Azure Files volume mounted at the same path must grant
# UID 10001 (or GID 10001) read/write access, e.g. `--user 10001:10001` plus matching
# host-side permissions, or an equivalent fsGroup/securityContext in your orchestrator.
RUN mkdir -p /data/validation-api \
    && chown validation-api:validation-api /data/validation-api

ENV ASPNETCORE_URLS=http://+:8080 \
    ValidationApi__StorageRoot=/data/validation-api \
    DOTNET_RUNNING_IN_CONTAINER=true \
    HOME=/home/validation-api \
    DOTNET_CLI_HOME=/home/validation-api \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=0

EXPOSE 8080
VOLUME ["/data/validation-api"]

USER validation-api:validation-api

# Practical liveness/readiness probe: GET /healthz over plain HTTP on the same port
# ASPNETCORE_URLS binds. --start-period gives the app time to finish startup recovery
# (ValidationRunWorker.RecoverAsync) before failures count against --retries.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Validation.Api.dll"]