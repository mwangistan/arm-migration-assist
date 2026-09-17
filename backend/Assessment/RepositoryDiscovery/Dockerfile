# syntax=docker/dockerfile:1.7

ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY backend/Assessment/ backend/Assessment/
COPY backend/MigrationPlanner/contracts/ backend/MigrationPlanner/contracts/

RUN dotnet restore backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj
RUN dotnet publish backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj \
        --configuration Release \
        --output /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "repository-discovery.dll", "serve"]