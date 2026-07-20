$ErrorActionPreference = 'Stop'
$taskScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$taskRepositoryRoot = Resolve-Path (Join-Path $taskScriptDirectory '..\..\..')
$taskPreviousUpdateValue = $env:UPDATE_MESSAGE_CONTRACT_BASELINE

Push-Location $taskRepositoryRoot
try {
    $env:UPDATE_MESSAGE_CONTRACT_BASELINE = '1'
    dotnet test Tests\ApiContract.Tests\ApiContract.Tests.csproj `
        -c Release `
        --filter FullyQualifiedName~Sanitized_message_contract_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "Message baseline regeneration failed with exit code $LASTEXITCODE."
    }

    Remove-Item Env:\UPDATE_MESSAGE_CONTRACT_BASELINE -ErrorAction SilentlyContinue
    dotnet test Tests\ApiContract.Tests\ApiContract.Tests.csproj `
        -c Release `
        --no-build `
        --filter FullyQualifiedName~Sanitized_message_contract_baseline_is_deterministic_and_current
    if ($LASTEXITCODE -ne 0) {
        throw "Message baseline verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $taskPreviousUpdateValue) {
        Remove-Item Env:\UPDATE_MESSAGE_CONTRACT_BASELINE -ErrorAction SilentlyContinue
    }
    else {
        $env:UPDATE_MESSAGE_CONTRACT_BASELINE = $taskPreviousUpdateValue
    }
    Pop-Location
}
