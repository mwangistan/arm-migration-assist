$url = "https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io"
$fixtures = @("ready-managed-app", "moderate-build-gap", "blocked-native-app")
foreach ($f in $fixtures) {
  $body = (Invoke-WebRequest "$url/fixtures/$f.json" -TimeoutSec 30).Content
  $t0 = Get-Date
  try {
    $r = Invoke-RestMethod "$url/api/migration-plans" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 240
    $ms = [int]((Get-Date) - $t0).TotalMilliseconds
    "{0,-22} OK   {1,5}ms  path={2,-22} conf={3,-6} planId={4}" -f $f, $ms, $r.plan.recommendedPath, $r.plan.confidence, $r.plan.planId
  } catch {
    $ms = [int]((Get-Date) - $t0).TotalMilliseconds
    $problem = $null
    try { $problem = $_.ErrorDetails.Message | ConvertFrom-Json } catch {}
    "{0,-22} FAIL {1,5}ms  status={2} code={3}" -f $f, $ms, $_.Exception.Response.StatusCode, $problem.code
    if ($problem.errors) {
      $problem.errors | Select-Object -First 5 | ForEach-Object { "                          - $_" }
    }
  }
}
