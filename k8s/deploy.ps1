<#
.SYNOPSIS
Builds the API image and deploys it, with PostgreSQL and Adminer, to Docker Desktop's Kubernetes.

.DESCRIPTION
Each build gets a unique tag (quotesnap:yyyyMMdd-HHmmss), so Kubernetes always runs the code just built. The manifests
in k8s/ are applied with that tag, the script waits for the rollout, checks /health, and removes all but the newest
few builds. If the quotesnap secret doesn't exist yet, it's created from the repository's .env.

.PARAMETER SkipBuild
Applies only the manifest changes, keeping the image that's running.

.PARAMETER Keep
How many of the newest images to keep; older ones are removed. 3 by default.

.EXAMPLE
./k8s/deploy.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [ValidateRange(1, 50)][int]$Keep = 3
)

$ErrorActionPreference = 'Stop'
$namespace = 'quotesnap'
$context = 'docker-desktop'
$root = Split-Path $PSScriptRoot

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# Throws when the last native command (docker, kubectl) failed.
function Assert-Ok([string]$what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

function Test-Exists([string]$kind, [string]$name) {
    kubectl get $kind $name -n $namespace -o name 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

$current = kubectl config current-context
if ($current -ne $context) {
    throw "kubectl points at '$current', not '$context'. Run: kubectl config use-context $context"
}

kubectl get namespace $namespace -o name 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Step "Creating namespace $namespace"
    kubectl apply -f (Join-Path $PSScriptRoot 'namespace.yaml') | Out-Null
    Assert-Ok 'Creating the namespace'
}

if (-not (Test-Exists secret quotesnap)) {
    Step 'Creating the quotesnap secret from .env'
    $envFile = Join-Path $root '.env'
    if (-not (Test-Path $envFile)) { throw "No .env at $envFile; copy .env.example and fill it in." }
    $values = @{}
    foreach ($line in Get-Content $envFile) {
        if ($line -match '^\s*([^#=\s]+)\s*=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
    }
    foreach ($name in 'QUOTESNAP_DB_PASSWORD', 'Auth__ApiKey', 'Auth__AdminApiKey') {
        if (-not $values[$name]) { throw "$name is empty in .env." }
    }
    $secretArgs = @(
        "--from-literal=db-password=$($values.QUOTESNAP_DB_PASSWORD)",
        "--from-literal=api-key=$($values.Auth__ApiKey)",
        "--from-literal=admin-api-key=$($values.Auth__AdminApiKey)")
    if ($values.Fyers__ExpectedFyersId) { $secretArgs += "--from-literal=expected-fyers-id=$($values.Fyers__ExpectedFyersId)" }
    kubectl create secret generic quotesnap -n $namespace @secretArgs | Out-Null
    Assert-Ok 'Creating the secret'
}

if ($SkipBuild) {
    if (-not (Test-Exists deployment quotesnap)) { throw 'Nothing is deployed yet; run without -SkipBuild first.' }
    $image = kubectl get deployment quotesnap -n $namespace -o jsonpath='{.spec.template.spec.containers[0].image}'
    Assert-Ok 'Reading the running image'
    Step "Keeping the running image $image"
}
else {
    $image = "quotesnap:$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Step "Building $image"
    docker build -t $image $root
    Assert-Ok 'docker build'
}

Step 'Applying k8s manifests'
$manifests = (kubectl kustomize $PSScriptRoot) -join "`n"
Assert-Ok 'Rendering the manifests'
$manifests.Replace('image: quotesnap:local', "image: $image") | kubectl apply -f -
Assert-Ok 'kubectl apply'

Step 'Waiting for the rollout'
foreach ($workload in 'statefulset/postgres', 'deployment/quotesnap', 'deployment/adminer') {
    kubectl rollout status $workload -n $namespace --timeout=300s
    Assert-Ok "Waiting for $workload (see: kubectl describe -n $namespace $workload)"
}

$health = $null
foreach ($attempt in 1..10) {
    try { $health = (Invoke-WebRequest 'http://localhost:8080/health' -UseBasicParsing -TimeoutSec 5).Content; break } catch { Start-Sleep 2 }
}
if ($health -ne 'Healthy') { throw "The API didn't answer healthy at http://localhost:8080/health. Logs: kubectl logs -n $namespace deploy/quotesnap" }

if (-not $SkipBuild) {
    $old = docker images quotesnap --format '{{.Tag}}' | Where-Object { $_ -match '^\d{8}-\d{6}$' } | Sort-Object -Descending | Select-Object -Skip $Keep
    foreach ($tag in $old) { docker rmi "quotesnap:$tag" 2>$null | Out-Null }
    if ($old) { Step "Removed $(@($old).Count) old image(s)" }
}

Write-Host ''
Write-Host "Deployed $image" -ForegroundColor Green
Write-Host '  API:     http://localhost:8080'
Write-Host '  Adminer: http://localhost:8081/?pgsql=postgres&username=quotesnap&db=quotesnap'
Write-Host "  Logs:    kubectl logs -n $namespace deploy/quotesnap -f"
