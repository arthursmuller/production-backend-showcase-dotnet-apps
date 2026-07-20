$ErrorActionPreference = 'Stop'
$taskScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$taskRepositoryRoot = Resolve-Path (Join-Path $taskScriptDirectory '..\..\..')
$taskPreviousUpdateValue = $env:UPDATE_QUERY_PLAN_BASELINE

Push-Location $taskRepositoryRoot
try {
    $env:UPDATE_QUERY_PLAN_BASELINE = '1'
    dotnet test Tests\Architecture.Tests\Architecture.Tests.csproj `
        -c Release `
        -m:1 `
        -p:BuildInParallel=false `
        -p:UseSharedCompilation=false `
        -nodeReuse:false `
        --filter FullyQualifiedName~Sanitized_mariadb_query_plan_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "Query-plan baseline regeneration failed with exit code $LASTEXITCODE."
    }

    Remove-Item Env:\UPDATE_QUERY_PLAN_BASELINE -ErrorAction SilentlyContinue
    dotnet test Tests\Architecture.Tests\Architecture.Tests.csproj `
        -c Release `
        --no-build `
        -m:1 `
        -p:BuildInParallel=false `
        -p:UseSharedCompilation=false `
        -nodeReuse:false `
        --filter FullyQualifiedName~Sanitized_mariadb_query_plan_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "Query-plan baseline verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $taskPreviousUpdateValue) {
        Remove-Item Env:\UPDATE_QUERY_PLAN_BASELINE -ErrorAction SilentlyContinue
    }
    else {
        $env:UPDATE_QUERY_PLAN_BASELINE = $taskPreviousUpdateValue
    }
    Pop-Location
}
