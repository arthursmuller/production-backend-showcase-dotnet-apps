$ErrorActionPreference = 'Stop'
$taskScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$taskRepositoryRoot = Resolve-Path (Join-Path $taskScriptDirectory '..\..\..')
$taskPreviousUpdateValue = $env:UPDATE_HTTP_OPENAPI_BASELINE

Push-Location $taskRepositoryRoot
try {
    $env:UPDATE_HTTP_OPENAPI_BASELINE = '1'
    dotnet test Tests\ApiContract.Tests\ApiContract.Tests.csproj `
        -c Release `
        --filter FullyQualifiedName~Sanitized_openapi_operation_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "OpenAPI baseline regeneration failed with exit code $LASTEXITCODE."
    }

    Remove-Item Env:\UPDATE_HTTP_OPENAPI_BASELINE -ErrorAction SilentlyContinue
    dotnet test Tests\ApiContract.Tests\ApiContract.Tests.csproj `
        -c Release `
        --no-build `
        --filter FullyQualifiedName~Sanitized_openapi_operation_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "OpenAPI baseline verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $taskPreviousUpdateValue) {
        Remove-Item Env:\UPDATE_HTTP_OPENAPI_BASELINE -ErrorAction SilentlyContinue
    }
    else {
        $env:UPDATE_HTTP_OPENAPI_BASELINE = $taskPreviousUpdateValue
    }
    Pop-Location
}
