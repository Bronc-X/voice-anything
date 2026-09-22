$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Uninstall.ps1')

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('VoiceAnything-UninstallTests-' + [Guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $fixtureRoot 'VoiceAnything'
$outsideDirectory = Join-Path $fixtureRoot 'keep'
$scriptFile = Join-Path $installDirectory 'Uninstall.ps1'
$uninstallScriptPath = $scriptFile
$junction = Join-Path $installDirectory 'redirected'

function Assert-Rejected([scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe uninstall target was accepted.' }
}

try {
    New-Item -ItemType Directory -Path $installDirectory,$outsideDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $outsideDirectory 'sentinel.txt') -Value 'retain'
    Assert-Rejected { Assert-InstallDirectory }
    Set-Content -LiteralPath (Join-Path $installDirectory 'install-receipt.txt') -Value 'fixture'
    Assert-InstallDirectory
    $uninstallScriptPath = Join-Path $outsideDirectory 'Uninstall.ps1'
    Assert-Rejected { Assert-InstallDirectory }
    $uninstallScriptPath = $scriptFile
    New-Item -ItemType Junction -Path $junction -Target $outsideDirectory | Out-Null
    Assert-Rejected { Assert-InstallDirectory }
    Remove-Item -LiteralPath $junction -Force
    Assert-InstallDirectory
    if ((Get-Content -LiteralPath (Join-Path $outsideDirectory 'sentinel.txt')).Trim() -ne 'retain') {
        throw 'Validation modified a path outside the installation.'
    }
    Write-Output 'PASS uninstaller rejects missing receipts, copied scripts and redirected directories without deleting user data'
} finally {
    if (Test-Path -LiteralPath $junction) { Remove-Item -LiteralPath $junction -Force }
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $allowedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\VoiceAnything-UninstallTests-'
    if (-not $resolvedFixture.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture path' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
