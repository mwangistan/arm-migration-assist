<#
.SYNOPSIS
    Deploys ONLY the Feature 1 backend (Azure Container App) - builds the image and
    deploys infra/backend.bicep into rg-arm-migration-assist.

.DESCRIPTION
    Independent of the frontend. Re-runnable. Bump -ImageTag to ship a new image.

        .\infra\deploy-backend.ps1                 # build + deploy
        .\infra\deploy-backend.ps1 -SkipBuild      # redeploy infra with the current image
        .\infra\deploy-backend.ps1 -ImageTag v2    # build v2 and deploy it
        .\infra\deploy-backend.ps1 -FrontendOrigin https://<swa-host>   # also set CORS

    Requires: az (logged in). Docker is NOT required (az acr build is server-side).
    Returns the backend URL as its pipeline output so an orchestrator can capture it.
#>

[CmdletBinding()]
param(
    [string] $SubscriptionId = '170ceb31-efaa-48f6-94f4-d3649c1f8369',
    [string] $ResourceGroup  = 'rg-arm-migration-assist',
    [string] $Acr            = 'acrarmmigassist',
    [string] $ImageTag       = 'v1',
    [string] $CreatedBy      = 'schisiya@microsoft.com',
    [string] $BackendAppName = 'ca-arm-migration-assessment-api',
    [string] $DeploymentName = 'feature1-backend',
    [string] $FrontendOrigin = '',
    [switch] $SkipBuild,
    [switch] $SkipVerify
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$BackendDir = Join-Path $RepoRoot 'backend\Assessment'
$BicepFile  = Join-Path $PSScriptRoot 'backend.bicep'
$ImageRef   = "arm-migration-assessment-api:$ImageTag"

function Write-Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Info($m) { Write-Host "    $m" -ForegroundColor DarkGray }

# Simple function (no param block) so az flags like -o/-g/-n pass straight through.
function Invoke-Az {
    $azArgs = $args
    $output = & az @azArgs
    if ($LASTEXITCODE -ne 0) { throw "az $($azArgs -join ' ') failed (exit $LASTEXITCODE)" }
    return $output
}

Write-Step "Backend deploy"
Write-Info "Image: $Acr.azurecr.io/$ImageRef"
Invoke-Az account set --subscription $SubscriptionId | Out-Null

if (-not $SkipBuild) {
    Write-Step "Building backend image (az acr build)"
    Invoke-Az acr build --registry $Acr --image $ImageRef $BackendDir
    Write-Info "Image pushed."
} else {
    Write-Info "Skipping image build."
}

Write-Step "Deploying backend.bicep"
$deployArgs = @(
    'deployment', 'group', 'create',
    '--resource-group', $ResourceGroup, '--name', $DeploymentName, '--template-file', $BicepFile,
    '--parameters', "createdBy=$CreatedBy",
    "imageTag=$ImageTag", "backendAppName=$BackendAppName"
)
if ($FrontendOrigin) { $deployArgs += "frontendOrigin=$FrontendOrigin" }
Invoke-Az @deployArgs | Out-Null

$outputs    = (Invoke-Az deployment group show -g $ResourceGroup -n $DeploymentName --query properties.outputs -o json) | ConvertFrom-Json
$BackendUrl = $outputs.backendUrl.value
Write-Info "Backend URL: $BackendUrl"

if (-not $SkipVerify) {
    Write-Step "Verify"
    Invoke-Az containerapp revision list -n $BackendAppName -g $ResourceGroup `
        --query "[].{revision:name, active:properties.active, running:properties.runningState}" -o table | Out-Host
}

Write-Host "Backend: $BackendUrl" -ForegroundColor Green
return $BackendUrl
