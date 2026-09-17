param([Parameter(Mandatory=$true)][string]$RunId)

$ws = az monitor log-analytics workspace show -n law-arm-migration-assist -g rg-arm-migration-assist --query customerId -o tsv
$query = @"
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(7d)
| where Log_s has "$RunId" or Log_s has "phi_completion"
| project TimeGenerated, Log_s
| order by TimeGenerated asc
"@

$rows = az monitor log-analytics query -w $ws --analytics-query $query -o json | ConvertFrom-Json

# The retry loop emits an audit event PER attempt (same runId, later timestamp).
# The final audit event is the caller-visible outcome; earlier ones are intermediate.
$audits = $rows | Where-Object { $_.Log_s -match "planner_run.*$RunId" } | Sort-Object { [datetime]$_.TimeGenerated }
if (-not $audits) {
  Write-Host "No audit row found for runId $RunId in the last 7 days."
  Write-Host "Rows returned: $($rows.Count)"
  return
}

$final = $audits | Select-Object -Last 1
$attemptCount = @($audits).Count
$auditTime = [datetime]$final.TimeGenerated
$phi = $rows |
  Where-Object { $_.Log_s -match "phi_completion" } |
  Where-Object { [datetime]$_.TimeGenerated -le $auditTime -and (($auditTime - [datetime]$_.TimeGenerated).TotalSeconds -lt 60) } |
  Sort-Object { [datetime]$_.TimeGenerated } |
  Select-Object -Last 1

Write-Host "=== runId $RunId (attempts=$attemptCount) ===`n"

if ($attemptCount -gt 1) {
  Write-Host "intermediate attempts:"
  $audits | Select-Object -SkipLast 1 | ForEach-Object {
    "  $($_.TimeGenerated)  $($_.Log_s)"
  }
  Write-Host ""
}

Write-Host "phi_completion (final attempt):"
Write-Host "  $($phi.Log_s)"
Write-Host ""
Write-Host "planner_run (final, caller-visible):"
Write-Host "  $($final.Log_s)"
