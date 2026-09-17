$url = "https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io"
foreach ($f in @("moderate-build-gap", "blocked-native-app")) {
  $body = (Invoke-WebRequest "$url/fixtures/$f.json" -TimeoutSec 30).Content
  $r = Invoke-RestMethod "$url/api/migration-plans" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 240
  Write-Host "=== $f ==="
  "score.band          = $($r.score.band)"
  "score.provisional   = $($r.score.provisional)"
  "score.overallScore  = $($r.score.overallScore)"
  "score.uncappedScore = $($r.score.uncappedScore)"
  "score.capsApplied   = $(($r.score.capsApplied | ForEach-Object { $_.capId }) -join ',')"
  "score.confidence    = $($r.score.confidence)"
  "plan.recommendedPath= $($r.plan.recommendedPath)"
  "plan.confidence     = $($r.plan.confidence)"
  Write-Host ""
}
