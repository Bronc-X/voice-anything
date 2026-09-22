[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path -Parent $PSScriptRoot
$dotnet = if ($env:SAYALL_DOTNET -and (Test-Path -LiteralPath $env:SAYALL_DOTNET)) {
    $env:SAYALL_DOTNET
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Invoke-BaselineStep {
    param(
        [Parameter(Mandatory)]
        [string]$Label,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host "BASELINE $Label"
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Baseline step failed: $Label"
    }
}

Push-Location $windowsRoot
try {
    Invoke-BaselineStep 'core contracts' @(
        'run', '--project', 'tests\SayAll.Core.ContractTests\SayAll.Core.ContractTests.csproj')
    Invoke-BaselineStep 'HID bridge contracts' @(
        'run', '--project', 'tests\SayAll.HidBridge.ContractTests\SayAll.HidBridge.ContractTests.csproj')
    Invoke-BaselineStep 'history, privacy, MCP and device profiles' @(
        'run', '--project', 'tests\VoiceAnything.Tests\VoiceAnything.Tests.csproj')
    Invoke-BaselineStep 'WinRT PCM buffer integration' @(
        'run', '--project', 'tests\SayAll.Windows.IntegrationTests\SayAll.Windows.IntegrationTests.csproj')
    Invoke-BaselineStep 'Windows app build' @(
        'build', 'src\SayAll.Windows\SayAll.Windows.csproj')
    Invoke-BaselineStep 'HID helper build' @(
        'build', 'src\SayAll.HidBridge\SayAll.HidBridge.csproj')
    Invoke-BaselineStep 'installer build' @(
        'build', 'src\SayAll.Setup\SayAll.Setup.csproj')
    Invoke-BaselineStep 'MCP helper build' @(
        'build', 'src\VoiceAnything.Mcp\VoiceAnything.Mcp.csproj')
    Write-Host 'BASELINE PASS: automated contracts and builds; hardware acceptance is a separate check.'
}
finally {
    Pop-Location
}
