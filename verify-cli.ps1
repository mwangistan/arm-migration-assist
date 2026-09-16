[CmdletBinding()]
param(
    [Parameter()]
    [string]$DllPath = (Join-Path $PSScriptRoot '.test-output\publish\ArmMigrationAssist.Api.dll')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$env:Logging__LogLevel__Default = 'None'
$env:DOTNET_ENVIRONMENT = 'Production'

function Invoke-Assessment {
    param(
        [Parameter(Mandatory)]
        [string]$Url,

        [Parameter(Mandatory)]
        [ValidateSet('Arm64Native', 'Arm64EC')]
        [string]$Target
    )

    $json = & dotnet $DllPath assess $Url $Target
    if ($LASTEXITCODE -ne 0) {
        throw "Assessment process exited with code $LASTEXITCODE."
    }

    return ($json -join "`n" | ConvertFrom-Json)
}

function Show-AssessmentSummary {
    param(
        [Parameter(Mandatory)]
        [string]$Label,

        [Parameter(Mandatory)]
        [string]$Url,

        [Parameter(Mandatory)]
        [ValidateSet('Arm64Native', 'Arm64EC')]
        [string]$Target
    )

    Write-Output "############ $Label ($Target) ############"
    $result = Invoke-Assessment -Url $Url -Target $Target
    Write-Output ('schema={0}  target={1}' -f $result.schema, $result.target)
    Write-Output ('1.1 ingestion : commit={0} branch={1} files={2}' -f $result.repository.commitSha.Substring(0, 8), $result.repository.defaultBranch, $result.repository.fileCount)
    Write-Output ('1.2 tech      : langs=[{0}] pkg=[{1}] ci={2}' -f (($result.technology.languages | ForEach-Object { $_.language }) -join ','), ($result.technology.packageManagers -join ','), $result.technology.hasExistingCi)
    Write-Output ('    build     : arm64Target={0} ciArm64={1} rids=[{2}]' -f $result.buildReadiness.hasArm64BuildTarget, $result.buildReadiness.ciHasArm64Job, ($result.buildReadiness.runtimeIdentifiers -join ','))
    Write-Output ('1.3 deps      : count={0}' -f $result.dependencies.Count)
    $result.dependencies | Select-Object -First 4 | ForEach-Object { Write-Output ('      [{0}] {1} {2} -> {3}' -f $_.id, $_.name, $_.source, $_.classification) }
    Write-Output ('1.4 arch      : count={0}' -f $result.architectureFindings.Count)
    $result.architectureFindings | Group-Object severity | Sort-Object Name | ForEach-Object { Write-Output ('      {0}={1}' -f $_.Name, $_.Count) }
    $result.architectureFindings | Select-Object -First 2 | ForEach-Object { Write-Output ('      [{0}] {1}:{2} {3}' -f $_.id, $_.file, $_.line, $_.category) }
    Write-Output ''
}

try {
    if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) {
        throw "Published API assembly was not found at '$DllPath'. Run dotnet publish first."
    }

    Show-AssessmentSummary -Label 'electron-quick-start' -Url 'https://github.com/electron/electron-quick-start' -Target 'Arm64EC'
    Show-AssessmentSummary -Label 'nothings/stb' -Url 'https://github.com/nothings/stb' -Target 'Arm64Native'
    exit 0
} catch {
    Write-Error "CLI verification failed: $_"
    exit 1
}
