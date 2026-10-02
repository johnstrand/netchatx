#
# run-integration-tests.ps1 - Runs containerized XMPP integration tests using Testcontainers
#
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalArgs
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path "$ScriptDir/.."

Write-Host "==> Running live XMPP integration tests (STANZA_INTEGRATION_TESTS=1)..." -ForegroundColor Cyan
$env:STANZA_INTEGRATION_TESTS = "1"

try {
    dotnet test "$RepoRoot/tests/Stanza.IntegrationTests/Stanza.IntegrationTests.csproj" @AdditionalArgs
}
finally {
    Remove-Item Env:\STANZA_INTEGRATION_TESTS -ErrorAction SilentlyContinue
}
