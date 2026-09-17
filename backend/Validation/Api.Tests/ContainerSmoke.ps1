param([string]$Image = 'validation-api:review-final')

$ErrorActionPreference = 'Stop'
$suffix = [Guid]::NewGuid().ToString('N')
$container = "validation-api-smoke-$suffix"
$volume = "validation-api-smoke-data-$suffix"
$script:checks = 0
$script:dockerCommand = (Get-Command docker -CommandType Application | Select-Object -First 1).Source

function Docker {
    $output = & $script:dockerCommand @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args[0]) failed ($LASTEXITCODE)." }
    return $output
}

function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}

function Start-SmokeContainer([bool]$allowNonLoopback) {
    Docker run -d --name $container -p '127.0.0.1::8080' `
        -e "ValidationApi__AllowNonLoopback=$($allowNonLoopback.ToString().ToLowerInvariant())" `
        -v "${volume}:/data/validation-api" $Image | Out-Null
    $mapping = Docker port $container 8080
    $script:baseUrl = "http://$mapping"
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $health = Docker inspect --format '{{.State.Health.Status}}' $container
        if ($health -eq 'healthy') { return }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Container health did not become healthy ($health)." }
        Start-Sleep -Milliseconds 500
    } while ($true)
}

function Request([string]$method, [string]$path, $body, [int]$expected) {
    $options = @{
        Uri = "$script:baseUrl$path"
        Method = $method
        SkipHttpErrorCheck = $true
        TimeoutSec = 30
    }
    if ($null -ne $body) {
        $options.Body = ConvertTo-Json -InputObject $body -Depth 30 -Compress
        $options.ContentType = 'application/json'
    }
    $response = Invoke-WebRequest @options
    Check ($response.StatusCode -eq $expected) "$method $path expected $expected, got $($response.StatusCode): $($response.Content)"
    return $response.Content | ConvertFrom-Json
}

try {
    Docker volume create $volume | Out-Null
    Start-SmokeContainer $true
    Check ((Docker exec $container id -u) -eq '10001') 'API must run as non-root UID 10001.'
    Docker exec $container sh -c 'test ! -e /app/.git && test ! -e /src && test ! -e /run/secrets/nuget_config' | Out-Null
    Check ($LASTEXITCODE -eq 0) 'Runtime must not require a checkout or retain build secrets.'
    Request GET /healthz $null 200 | Out-Null
    Request POST /api/v1/validation/plans @{} 400 | Out-Null

    # This isolated fixture is provisioned by a trusted component, never by the API.
    $setup = @'
set -eu
mkdir -p /tmp/validation-smoke/repository /tmp/validation-smoke/evidence
cd /tmp/validation-smoke/repository
printf '%s\n' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><UseAppHost>false</UseAppHost><SelfContained>false</SelfContained></PropertyGroup></Project>' > Smoke.csproj
printf '%s\n' 'public class Smoke { }' > Smoke.cs
printf '%s\n' 'bin/' 'obj/' > .gitignore
printf '%s\n' '<configuration><packageSources><clear /></packageSources></configuration>' > NuGet.Config
git init -q
git config core.autocrlf false
git add .
git -c user.name=ContainerSmoke -c user.email=smoke@example.invalid commit -qm fixture
'@
    Docker exec $container sh -c ($setup -replace "`r", '') | Out-Null
    $body = @{
        migrationPlan = @{
            schemaVersion = '1.0'; planId = 'container-smoke'
            validationPlan = @{
                targetDevices = @('arm64')
                buildChecks = @(); functionalChecks = @(); reliabilityChecks = @()
                performanceChecks = @(); powerChecks = @(); offlineChecks = @()
                accessibilityChecks = @(); windowsExperienceChecks = @()
            }
            workItems = @()
        }
        target = @{ path = '/tmp/validation-smoke/repository' }
        options = @{ evidenceDirectory = '/tmp/validation-smoke/evidence' }
        includeProposal = $true
    }
    $plan = Request POST /api/v1/validation/plans $body 201
    Request POST $plan.links.runs $null 409 | Out-Null
    $approval = Request GET $plan.links.approval $null 200
    Check ($approval.approval.approvedCommandIds.Count -eq 0) 'GET approval must return an empty skeleton.'
    Request POST $plan.links.runs $null 409 | Out-Null
    $buildId = @($plan.proposal.commands | Where-Object kind -eq 'dot-net-build')[0].id
    Check (-not [string]::IsNullOrEmpty($buildId)) 'Expected deterministic dotnet build discovery.'
    Request PUT $plan.links.approval @{ approvedCommandIds = @($buildId) } 200 | Out-Null
    $run = Request POST $plan.links.runs $null 202
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    do {
        $status = Request GET $run.links.status $null 200
        if ($status.status -in @('completed', 'failed', 'cancelled')) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Run did not finish.' }
        Start-Sleep -Milliseconds 500
    } while ($true)
    Check ($status.status -eq 'completed') "Run failed: $($status.error)"
    $report = Request GET $run.links.report $null 200
    Check (@($report.commands | Where-Object { $_.commandId -eq $buildId -and $_.status -eq 'passed' }).Count -eq 1) `
        "Approved build did not pass: $($report.commands | ConvertTo-Json -Depth 8)"
    $dashboard = Request GET $run.links.dashboard $null 200
    Check ($dashboard.runId -eq $run.runId) 'Dashboard run ID mismatch.'
    Request POST $plan.links.runs $null 409 | Out-Null

    $body.options.evidenceDirectory = '/tmp/validation-smoke/repository/evidence'
    Request POST /api/v1/validation/plans $body 400 | Out-Null
    $body.options.evidenceDirectory = '/data/validation-api/evidence'
    Request POST /api/v1/validation/plans $body 400 | Out-Null
    Docker exec $container sh -c 'ln -s /data/validation-api /tmp/validation-smoke/linked-storage' | Out-Null
    $body.options.evidenceDirectory = '/tmp/validation-smoke/linked-storage/evidence'
    Request POST /api/v1/validation/plans $body 400 | Out-Null

    Docker stop $container | Out-Null
    Docker rm $container | Out-Null
    Start-SmokeContainer $true
    Request GET $plan.links.self $null 200 | Out-Null
    Request GET $run.links.report $null 200 | Out-Null
    Request POST $plan.links.runs $null 409 | Out-Null
    Docker stop $container | Out-Null
    Docker rm $container | Out-Null
    Start-SmokeContainer $false
    Request GET /healthz $null 403 | Out-Null
    Write-Output "PASS: $script:checks HTTP/container assertions; non-root, health, approved build, persistence, proof reuse and loopback gate."
}
finally {
    $existing = & docker ps -aq --filter "name=^/$container$"
    if ($existing) {
        & docker logs --tail 15 $container
        & docker rm -f $container | Out-Null
    }
    & docker volume rm $volume | Out-Null
}
