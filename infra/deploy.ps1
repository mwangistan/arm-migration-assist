<#
.SYNOPSIS
    Orchestrates a full Feature 1 deployment by calling the two independent scripts:
    deploy-backend.ps1 (image + Container App) then deploy-frontend.ps1 (Static Web
    App + SPA), and wires the SWA origin into the backend's CORS.

.DESCRIPTION
    Prefer the focused scripts when you only need one tier:
        .\infra\deploy-backend.ps1
        .\infra\deploy-frontend.ps1

    This orchestrator is for a one-shot end-to-end run:
        .\infra\deploy.ps1                      # backend + frontend + CORS
        .\infra\deploy.ps1 -SkipBackend         # frontend only (backend already deployed)
        .\infra\deploy.ps1 -SkipFrontend        # backend only
        .\infra\deploy.ps1 -SkipBuild           # skip the image build, deploy the rest

    Requires: az (logged in), node/npm, npx. Docker is NOT required.
#>

[CmdletBinding()]
param(
    [string] $SubscriptionId = '170ceb31-efaa-48f6-94f4-d3649c1f8369',
    [string] $ResourceGroup  = 'rg-arm-migration-assist',
    [string] $Acr            = 'acrarmmigassist',
    [string] $ImageTag       = 'v1',
    [string] $CreatedBy      = 'schisiya@microsoft.com',

    [switch] $SkipBackend,
    [switch] $SkipFrontend,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$BackendScript  = Join-Path $PSScriptRoot 'deploy-backend.ps1'
$FrontendScript = Join-Path $PSScriptRoot 'deploy-frontend.ps1'
$BackendDeployName = 'feature1-backend'

function Write-Step($m) { Write-Host "`n########## $m ##########" -ForegroundColor Magenta }

function Invoke-Az {
    $azArgs = $args
    $output = & az @azArgs
    if ($LASTEXITCODE -ne 0) { throw "az $($azArgs -join ' ') failed (exit $LASTEXITCODE)" }
    return $output
}

Invoke-Az account set --subscription $SubscriptionId | Out-Null

# ---- backend ----------------------------------------------------------------
if (-not $SkipBackend) {
    Write-Step "BACKEND"
    & $BackendScript -SubscriptionId $SubscriptionId -ResourceGroup $ResourceGroup `
        -Acr $Acr -ImageTag $ImageTag -CreatedBy $CreatedBy `
        -SkipBuild:$SkipBuild | Out-Host
} else {
    Write-Host "Skipping backend." -ForegroundColor DarkGray
}

# Resolve the backend URL from its deployment output (works whether or not we just
# deployed it) so the SPA is built against the correct origin.
$BackendUrl = (Invoke-Az deployment group show -g $ResourceGroup -n $BackendDeployName `
    --query properties.outputs.backendUrl.value -o tsv)
Write-Host "Backend URL: $BackendUrl" -ForegroundColor DarkGray

# ---- frontend (+ CORS back to backend) --------------------------------------
if (-not $SkipFrontend) {
    Write-Step "FRONTEND"
    & $FrontendScript -SubscriptionId $SubscriptionId -ResourceGroup $ResourceGroup `
        -CreatedBy $CreatedBy -BackendUrl $BackendUrl -SetBackendCors | Out-Host
} else {
    Write-Host "Skipping frontend." -ForegroundColor DarkGray
}

Write-Step "DONE"
Write-Host "Portal: https://portal.azure.com/#@/resource/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/overview" -ForegroundColor Green
