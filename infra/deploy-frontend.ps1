<#
.SYNOPSIS
    Deploys ONLY the Feature 1 frontend - creates the Azure Static Web App
    (infra/frontend.bicep), builds the SPA against the backend URL, and publishes it.

.DESCRIPTION
    Independent of the backend. Re-runnable.

        .\infra\deploy-frontend.ps1                    # create SWA + build + publish
        .\infra\deploy-frontend.ps1 -SkipInfra         # SWA exists: just rebuild + publish
        .\infra\deploy-frontend.ps1 -SetBackendCors    # also allow the SWA origin in backend CORS
        .\infra\deploy-frontend.ps1 -BackendUrl https://<backend-host>

    The SPA bakes VITE_API_BASE_URL at build time, so -BackendUrl must be correct
    before building. It defaults to the deterministic backend Container App FQDN.

    Requires: az (logged in), node/npm, npx.
#>

[CmdletBinding()]
param(
    [string] $SubscriptionId  = '170ceb31-efaa-48f6-94f4-d3649c1f8369',
    [string] $ResourceGroup   = 'rg-arm-migration-assist',
    [string] $CreatedBy       = 'schisiya@microsoft.com',
    [string] $StaticWebAppName = 'swa-arm-migration-assessment',
    [string] $StaticWebAppLocation = 'eastus2',
    [string] $DeploymentName  = 'feature1-frontend',
    [string] $BackendUrl      = 'https://ca-arm-migration-assessment-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io',
    [string] $BackendAppName  = 'ca-arm-migration-assessment-api',
    [switch] $SkipInfra,
    [switch] $SkipPublish,
    [switch] $SetBackendCors
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$FrontendDir = Join-Path $RepoRoot 'frontend'
$BicepFile   = Join-Path $PSScriptRoot 'frontend.bicep'

function Write-Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Info($m) { Write-Host "    $m" -ForegroundColor DarkGray }

# Simple function (no param block) so az flags like -o/-g/-n pass straight through.
function Invoke-Az {
    $azArgs = $args
    $output = & az @azArgs
    if ($LASTEXITCODE -ne 0) { throw "az $($azArgs -join ' ') failed (exit $LASTEXITCODE)" }
    return $output
}

Write-Step "Frontend deploy"
Write-Info "Backend URL (baked into SPA): $BackendUrl"
Invoke-Az account set --subscription $SubscriptionId | Out-Null

if (-not $SkipInfra) {
    Write-Step "Deploying frontend.bicep (Static Web App)"
    Invoke-Az deployment group create `
        --resource-group $ResourceGroup --name $DeploymentName --template-file $BicepFile `
        --parameters createdBy=$CreatedBy staticWebAppName=$StaticWebAppName staticWebAppLocation=$StaticWebAppLocation | Out-Null
} else {
    Write-Info "Skipping SWA infrastructure deployment."
}

$outputs = (Invoke-Az deployment group show -g $ResourceGroup -n $DeploymentName --query properties.outputs -o json) | ConvertFrom-Json
$SwaName = $outputs.staticWebAppName.value
$SwaUrl  = $outputs.staticWebAppUrl.value
Write-Info "Static Web App: $SwaUrl"

if (-not $SkipPublish) {
    Write-Step "Building SPA and publishing to SWA"
    Push-Location $FrontendDir
    try {
        Write-Info "npm install..."
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed (exit $LASTEXITCODE)" }

        Write-Info "npm run build (VITE_API_BASE_URL=$BackendUrl)..."
        $env:VITE_API_BASE_URL = $BackendUrl
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed (exit $LASTEXITCODE)" }

        $token = Invoke-Az staticwebapp secrets list -n $SwaName -g $ResourceGroup --query properties.apiKey -o tsv
        Write-Info "Publishing dist to Static Web App..."
        npx -y '@azure/static-web-apps-cli' deploy '.\dist' --deployment-token $token --env production
        if ($LASTEXITCODE -ne 0) { throw "swa deploy failed (exit $LASTEXITCODE)" }
    }
    finally {
        Remove-Item Env:\VITE_API_BASE_URL -ErrorAction SilentlyContinue
        Pop-Location
    }
} else {
    Write-Info "Skipping SPA build/publish."
}

if ($SetBackendCors) {
    Write-Step "Allowing SWA origin through backend CORS"
    # Targeted env-var update on the existing backend Container App (creates a new revision).
    Invoke-Az containerapp update -n $BackendAppName -g $ResourceGroup `
        --set-env-vars "FrontendOrigins__0=$SwaUrl" | Out-Null
    Write-Info "Backend CORS origin set to $SwaUrl."
}

Write-Host "Dashboard: $SwaUrl" -ForegroundColor Green
return $SwaUrl
