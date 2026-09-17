param(
  [string]$File = "C:\Users\bnyandieka\Downloads\ComfyUI-arm-assessment (2).json",
  [string]$Url  = "https://ca-arm-migration-planner-api.delightfulcliff-b520a3d2.eastus2.azurecontainerapps.io",
  [string]$OutDir = "C:\Users\bnyandieka\source\repos\arm-migration-assist\tmp"
)

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$body = Get-Content -Raw $File
$stamp = (Get-Date).ToString("yyyyMMdd-HHmmss")
$out   = Join-Path $OutDir "comfyui-plan-$stamp.json"

$t0 = Get-Date
try {
  $r = Invoke-RestMethod $Url/api/migration-plans -Method Post `
       -ContentType "application/json" -Body $body -TimeoutSec 600
  $ms = [int]((Get-Date) - $t0).TotalMilliseconds
  $r | ConvertTo-Json -Depth 25 | Set-Content $out
  ""
  "OK $ms ms  runId=$($r.runId)"
  "response saved to $out"
  ""
  "=== VERDICT ==="
  "  recommendedPath = $($r.plan.recommendedPath)"
  "  confidence      = $($r.plan.confidence)"
  "  band            = $($r.score.band)"
  "  overallScore    = $($r.score.overallScore)"
  "  provisional     = $($r.score.provisional)"
  ""
  "=== COUNTS ==="
  "  facts             = $($r.plan.facts.Count)"
  "  inferences        = $($r.plan.inferences.Count)"
  "  alternatives      = $($r.plan.alternatives.Count)"
  "  workItems         = $($r.plan.workItems.Count)"
  "  missingSkills     = $($r.plan.missingSkills.Count)"
  "  risks             = $($r.plan.risks.Count)"
  "  unknowns          = $($r.plan.unknowns.Count)"
  "  requiredApprovals = $($r.plan.requiredApprovals.Count)"
  "  reusableOutputs   = $($r.plan.reusableOutputs.Count)"
  ""
  "=== WORK ITEMS ==="
  foreach ($w in $r.plan.workItems) {
    "  - $($w.id) [$($w.priority)] effort=$($w.estimatedEffort) risk=$($w.risk)"
    "    title: $($w.title)"
    "    skill: $($w.agentOrSkill)"
    "    acceptanceTests: $($w.acceptanceTests.Count)"
  }
  ""
  "=== MISSING SKILLS ==="
  foreach ($m in $r.plan.missingSkills) { "  - $($m.proposedName): $($m.purpose)" }
  ""
  "=== UNKNOWNS ==="
  foreach ($u in $r.plan.unknowns) { "  - $($u.id): $($u.description) [requiredSkill=$($u.requiredSkill)]" }
  ""
  "=== RISKS ==="
  foreach ($k in $r.plan.risks) { "  - $($k.id) [$($k.severity)]: $($k.description)" }
  ""
  "=== ALTERNATIVES ==="
  foreach ($a in $r.plan.alternatives) { "  - $($a.path) [$($a.disposition)]" }
  ""
  "=== GUIDANCE CITATIONS (from facts/inferences/alternatives/workItems) ==="
  $gids = @()
  foreach ($x in @($r.plan.facts + $r.plan.inferences + $r.plan.alternatives + $r.plan.workItems)) {
    if ($x.guidanceIds) { $gids += $x.guidanceIds }
  }
  ($gids | Select-Object -Unique | Sort-Object) -join ", "
} catch {
  $ms = [int]((Get-Date) - $t0).TotalMilliseconds
  $problem = $null
  if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
    try { $problem = $_.ErrorDetails.Message | ConvertFrom-Json } catch {}
  }
  "FAIL $ms ms  status=$($_.Exception.Response.StatusCode)  code=$($problem.code)"
  if ($problem) {
    "title:  $($problem.title)"
    "detail: $(($problem.detail -split "`n" | Select-Object -First 3) -join ' | ')"
    if ($problem.errors) {
      "errors:"
      $problem.errors | Select-Object -First 15 | ForEach-Object { "  - $_" }
    }
  } else {
    "no body: $($_.Exception.Message)"
  }
}
