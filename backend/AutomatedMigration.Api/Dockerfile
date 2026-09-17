FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/AutomatedMigration/AutomatedMigration.csproj backend/AutomatedMigration/
COPY backend/AutomatedMigration.Api/AutomatedMigration.Api.csproj backend/AutomatedMigration.Api/

RUN dotnet restore backend/AutomatedMigration.Api/AutomatedMigration.Api.csproj

COPY backend/AutomatedMigration/ backend/AutomatedMigration/
COPY backend/AutomatedMigration.Api/ backend/AutomatedMigration.Api/

RUN dotnet publish backend/AutomatedMigration.Api/AutomatedMigration.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AutomatedMigration.Api.dll"]
