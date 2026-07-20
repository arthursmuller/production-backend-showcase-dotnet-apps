$ErrorActionPreference = 'Stop'
$taskScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$taskRepositoryRoot = Resolve-Path (Join-Path $taskScriptDirectory '..\..\..')
$taskPreviousUpdateValue = $env:UPDATE_EF_SCHEMA_BASELINE

Push-Location $taskRepositoryRoot
try {
    $env:UPDATE_EF_SCHEMA_BASELINE = '1'
    dotnet test Tests\Architecture.Tests\Architecture.Tests.csproj `
        -c Release `
        --filter FullyQualifiedName~Sanitized_ef_schema_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "EF schema baseline regeneration failed with exit code $LASTEXITCODE."
    }

    Remove-Item Env:\UPDATE_EF_SCHEMA_BASELINE -ErrorAction SilentlyContinue
    dotnet test Tests\Architecture.Tests\Architecture.Tests.csproj `
        -c Release `
        --no-build `
        --filter FullyQualifiedName~Sanitized_ef_schema_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "EF schema baseline verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $taskPreviousUpdateValue) {
        Remove-Item Env:\UPDATE_EF_SCHEMA_BASELINE -ErrorAction SilentlyContinue
    }
    else {
        $env:UPDATE_EF_SCHEMA_BASELINE = $taskPreviousUpdateValue
    }
    Pop-Location
}
