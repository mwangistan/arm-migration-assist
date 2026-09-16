$env:Logging__LogLevel__Default = 'None'
$env:DOTNET_ENVIRONMENT = 'Production'
$dll = (Get-Content 'C:\Users\schisiya\source\repos\Hackathon ARM Migration\publish-path.txt').Trim()

function Assess($url, $target) {
  $json = & dotnet $dll assess $url $target 2>$null
  return ($json -join "`n" | ConvertFrom-Json)
}

function Show($label, $url, $target) {
  Write-Host "############ $label ($target) ############"
  $r = Assess $url $target
  Write-Host ('schema={0}  target={1}' -f $r.schema, $r.target)
  Write-Host ('1.1 ingestion : commit={0} branch={1} files={2}' -f $r.repository.commitSha.Substring(0,8), $r.repository.defaultBranch, $r.repository.fileCount)
  Write-Host ('1.2 tech      : langs=[{0}] pkg=[{1}] ci={2}' -f (($r.technology.languages | ForEach-Object { $_.language }) -join ','), ($r.technology.packageManagers -join ','), $r.technology.hasExistingCi)
  Write-Host ('    build     : arm64Target={0} ciArm64={1} rids=[{2}]' -f $r.buildReadiness.hasArm64BuildTarget, $r.buildReadiness.ciHasArm64Job, ($r.buildReadiness.runtimeIdentifiers -join ','))
  Write-Host ('1.3 deps      : count={0}' -f $r.dependencies.Count)
  $r.dependencies | Select-Object -First 4 | ForEach-Object { Write-Host ('      [{0}] {1} {2} -> {3}' -f $_.id, $_.name, $_.source, $_.classification) }
  Write-Host ('1.4 arch      : count={0}' -f $r.architectureFindings.Count)
  $r.architectureFindings | Group-Object severity | Sort-Object Name | ForEach-Object { Write-Host ('      {0}={1}' -f $_.Name, $_.Count) }
  $r.architectureFindings | Select-Object -First 2 | ForEach-Object { Write-Host ('      [{0}] {1}:{2} {3}' -f $_.id, $_.file, $_.line, $_.category) }
  Write-Host ''
}

Show 'electron-quick-start' 'https://github.com/electron/electron-quick-start' 'Arm64EC'
Show 'nothings/stb'        'https://github.com/nothings/stb'                    'Arm64Native'
