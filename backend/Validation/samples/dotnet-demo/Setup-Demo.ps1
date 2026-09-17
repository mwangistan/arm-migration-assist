[CmdletBinding()]
param(
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\validation-demo'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if (Test-Path $OutputRoot) {
    throw "Demo output already exists: $OutputRoot. Choose a new -OutputRoot to preserve prior evidence."
}

$target = Join-Path $OutputRoot 'target-repo'
$evidence = Join-Path $OutputRoot 'evidence'
New-Item -ItemType Directory -Path $target | Out-Null
New-Item -ItemType Directory -Path $evidence | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'source\DemoApp.csproj') $target
Copy-Item (Join-Path $PSScriptRoot 'source\Program.cs') $target
Copy-Item (Join-Path $PSScriptRoot 'source\.gitignore') $target
Copy-Item (Join-Path $PSScriptRoot 'migration-plan.json') (Join-Path $OutputRoot 'migration-plan.json')
Copy-Item (Join-Path $PSScriptRoot 'validation.runsettings') (Join-Path $OutputRoot 'validation.runsettings')

@{
    evidenceDirectory = $evidence
    mappings = @(
        @{
            commandId = 'dotnet-build-9f532b99b14a'
            criterionKeys = @(
                'validation:vc-dotnet-build'
                'acceptance:wi-dummy-dotnet-arm64:at-win-arm64-build'
            )
        }
    )
} | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8NoBOM (Join-Path $OutputRoot 'validation-options.json')

git -C $target init --initial-branch migration/dummy-validation | Out-Null
git -C $target config core.autocrlf false
git -C $target config user.name 'ARM Migration Assist Demo'
git -C $target config user.email 'arm-migration-demo@example.invalid'
git -C $target add DemoApp.csproj Program.cs .gitignore
git -C $target commit -m 'Create dummy ARM64 validation target' | Out-Null

$commit = (git -C $target rev-parse HEAD).Trim()
$branch = (git -C $target branch --show-current).Trim()
$project = Join-Path $repositoryRoot 'backend\Validation\Validation.csproj'
$settings = Join-Path $OutputRoot 'validation.runsettings'
$migration = Join-Path $OutputRoot 'migration-plan.json'
$options = Join-Path $OutputRoot 'validation-options.json'
$proposal = Join-Path $OutputRoot 'proposal.json'
$approval = Join-Path $OutputRoot 'approval.json'
$report = Join-Path $OutputRoot 'report.json'
$dashboard = Join-Path $OutputRoot 'dashboard.json'

Write-Host "Dummy validation workspace created: $OutputRoot"
Write-Host "Target repository: $target"
Write-Host "Commit: $commit"
Write-Host ""
Write-Host "1. Generate the AI-assisted proposal:"
Write-Host "dotnet run --project `"$project`" -- --settings `"$settings`" plan `"$migration`" `"$target`" `"$options`" `"$proposal`" `"$approval`" $commit $branch"
Write-Host ""
Write-Host "2. Review proposal.json and add reviewed command IDs to approval.json."
Write-Host ""
Write-Host "3. Execute the approved validation:"
Write-Host "dotnet run --project `"$project`" -- --settings `"$settings`" run `"$proposal`" `"$approval`" `"$report`" `"$dashboard`""
