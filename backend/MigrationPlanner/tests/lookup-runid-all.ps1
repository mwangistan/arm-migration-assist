param([Parameter(Mandatory=$true)][string]$RunId)

$ws = az monitor log-analytics workspace show -n law-arm-migration-assist -g rg-arm-migration-assist --query customerId -o tsv

$q = @"
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(7d)
| where Log_s has "$RunId"
| project TimeGenerated, Log_s
| order by TimeGenerated asc
"@

$rows = az monitor log-analytics query -w $ws --analytics-query $q -o json | ConvertFrom-Json
Write-Host "Rows for runId $RunId in last 7 days: $($rows.Count)`n"
foreach ($r in $rows) {
  "$($r.TimeGenerated)  $($r.Log_s)"
}
