[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Url,

    [Parameter()]
    [ValidateSet('Arm64Native', 'Arm64EC')]
    [string]$Target = 'Arm64Native'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

try {
    $env:Logging__LogLevel__Default = 'None'
    $projectPath = Join-Path $PSScriptRoot 'backend\ArmMigrationAssist.Api.csproj'
    $json = & dotnet run --project $projectPath -- assess $Url $Target
    if ($LASTEXITCODE -ne 0) {
        throw "Assessment process exited with code $LASTEXITCODE."
    }

    $result = $json -join "`n" | ConvertFrom-Json

    Write-Output ('schema={0} target={1}' -f $result.schema, $result.target)
    Write-Output ('1.1 commit={0} branch={1} files={2}' -f $result.repository.commitSha.Substring(0, 8), $result.repository.defaultBranch, $result.repository.fileCount)
    Write-Output ('1.2 langs=[{0}]' -f (($result.technology.languages | ForEach-Object { '{0}:{1}' -f $_.language, $_.fileCount }) -join ', '))
    Write-Output ('    build=[{0}] pkg=[{1}] ci={2}' -f ($result.technology.buildSystems -join ','), ($result.technology.packageManagers -join ','), $result.technology.hasExistingCi)
    Write-Output ('    buildReadiness: arm64Target={0} ciArm64={1} rids=[{2}]' -f $result.buildReadiness.hasArm64BuildTarget, $result.buildReadiness.ciHasArm64Job, ($result.buildReadiness.runtimeIdentifiers -join ','))
    Write-Output ('1.3 deps={0}' -f $result.dependencies.Count)
    $result.dependencies | Group-Object classification | ForEach-Object { Write-Output ('     {0}={1}' -f $_.Name, $_.Count) }
    $result.dependencies | Select-Object -First 10 | ForEach-Object { Write-Output ('      {0} [{1}] {2} -> {3}' -f $_.name, $_.source, $_.machine, $_.classification) }
    Write-Output ('1.4 archFindings={0}' -f $result.architectureFindings.Count)
    $result.architectureFindings | Group-Object category | Sort-Object Count -Descending | ForEach-Object { Write-Output ('     {0}: {1}' -f $_.Name, $_.Count) }
    $result.architectureFindings | Select-Object -First 5 | ForEach-Object { Write-Output ('      {0}:{1} [{2}] {3}' -f $_.file, $_.line, $_.severity, $_.category) }
    exit 0
} catch {
    Write-Error "Assessment failed: $_"
    exit 1
}
