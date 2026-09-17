$token = az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }
$bodyObj = @{
  messages = @(@{ role = 'user'; content = 'Reply with JSON only: {"ok":true}' })
  model = 'phi-4'
  max_tokens = 50
  temperature = 0
}
$body = $bodyObj | ConvertTo-Json -Depth 5
$urls = @(
  'https://foundry-arm-mig-assist.services.ai.azure.com/models/chat/completions?api-version=2024-05-01-preview',
  'https://foundry-arm-mig-assist.openai.azure.com/openai/deployments/phi-4/chat/completions?api-version=2024-08-01-preview'
)
foreach ($u in $urls) {
  Write-Host "--- $u ---"
  try {
    $r = Invoke-RestMethod -Uri $u -Method Post -Headers $headers -Body $body -TimeoutSec 60
    Write-Host "OK content=$($r.choices[0].message.content)"
  } catch {
    Write-Host "STATUS=$($_.Exception.Response.StatusCode)"
    Write-Host $_.ErrorDetails.Message
  }
}
