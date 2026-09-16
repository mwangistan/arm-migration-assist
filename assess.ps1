param([string]$Url, [string]$Target = 'Arm64Native')
$env:Logging__LogLevel__Default = 'None'
Push-Location 'C:\Users\schisiya\source\repos\Hackathon ARM Migration\ArmMigrationAssist.Api'
$json = & dotnet run -- assess $Url $Target 2>$null
Pop-Location
$r = ($json -join "`n" | ConvertFrom-Json)

Write-Host ('schema={0} target={1}' -f $r.schema, $r.target)
Write-Host ('1.1 commit={0} branch={1} files={2}' -f $r.repository.commitSha.Substring(0,8), $r.repository.defaultBranch, $r.repository.fileCount)
Write-Host ('1.2 langs=[{0}]' -f (($r.technology.languages | ForEach-Object { '{0}:{1}' -f $_.language, $_.fileCount }) -join ', '))
Write-Host ('    build=[{0}] pkg=[{1}] ci={2}' -f ($r.technology.buildSystems -join ','), ($r.technology.packageManagers -join ','), $r.technology.hasExistingCi)
Write-Host ('    buildReadiness: arm64Target={0} ciArm64={1} rids=[{2}]' -f $r.buildReadiness.hasArm64BuildTarget, $r.buildReadiness.ciHasArm64Job, ($r.buildReadiness.runtimeIdentifiers -join ','))
Write-Host ('1.3 deps={0}' -f $r.dependencies.Count)
$r.dependencies | Group-Object classification | ForEach-Object { Write-Host ('     {0}={1}' -f $_.Name, $_.Count) }
$r.dependencies | Select-Object -First 10 | ForEach-Object { Write-Host ('      {0} [{1}] {2} -> {3}' -f $_.name, $_.source, $_.machine, $_.classification) }
Write-Host ('1.4 archFindings={0}' -f $r.architectureFindings.Count)
$r.architectureFindings | Group-Object category | Sort-Object Count -Descending | ForEach-Object { Write-Host ('     {0}: {1}' -f $_.Name, $_.Count) }
$r.architectureFindings | Select-Object -First 5 | ForEach-Object { Write-Host ('      {0}:{1} [{2}] {3}' -f $_.file, $_.line, $_.severity, $_.category) }
