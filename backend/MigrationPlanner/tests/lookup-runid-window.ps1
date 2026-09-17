param([Parameter(Mandatory=$true)][string]$RunId)

$ws = az monitor log-analytics workspace show -n law-arm-migration-assist -g rg-arm-migration-assist --query customerId -o tsv

$audit = az monitor log-analytics query -w $ws --analytics-query "ContainerAppConsoleLogs_CL | where TimeGenerated > ago(7d) | where Log_s has '$RunId' and Log_s has 'planner_run' | project TimeGenerated, Log_s | take 1" -o json | ConvertFrom-Json
if (-not $audit) { Write-Host "Not found"; return }

$auditTs = [datetime]$audit[0].TimeGenerated
$from = $auditTs.AddSeconds(-90).ToString("yyyy-MM-ddTHH:mm:ssZ")
$to   = $auditTs.AddSeconds(30).ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "Audit at $auditTs (UTC)"
Write-Host "Window: $from -> $to"
Write-Host ""

$q = @"
ContainerAppConsoleLogs_CL
| where TimeGenerated between (datetime($from) .. datetime($to))
| project TimeGenerated, Log_s
| order by TimeGenerated asc
"@

az monitor log-analytics query -w $ws --analytics-query $q -o json |
  ConvertFrom-Json |
  ForEach-Object { "$($_.TimeGenerated)  $($_.Log_s)" }
