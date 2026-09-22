param(
    [string]$OutputDirectory,
    [string]$DotNetPath,
    [string]$GadgetPath
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\VoiceAnything-Setup'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
if (-not $OutputDirectory.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a child of the repository artifacts directory: $artifactRoot"
}

if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    $DotNetPath = 'dotnet'
}

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

$setupProject = Join-Path $repositoryRoot 'Windows\src\SayAll.Setup\SayAll.Setup.csproj'
$appProject = Join-Path $repositoryRoot 'Windows\src\SayAll.Windows\SayAll.Windows.csproj'
$bridgeProject = Join-Path $repositoryRoot 'Windows\src\SayAll.HidBridge\SayAll.HidBridge.csproj'
$mcpProject = Join-Path $repositoryRoot 'Windows\src\VoiceAnything.Mcp\VoiceAnything.Mcp.csproj'
$appOutput = Join-Path $OutputDirectory 'Payload\App'
$bridgeOutput = Join-Path $OutputDirectory 'Payload\HidBridge'

& $DotNetPath publish $setupProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'VoiceAnything.Setup publish failed.' }
& $DotNetPath publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $appOutput
if ($LASTEXITCODE -ne 0) { throw 'VoiceAnything app publish failed.' }
& $DotNetPath publish $bridgeProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $bridgeOutput
if ($LASTEXITCODE -ne 0) { throw 'VoiceAnything.HidBridge publish failed.' }
& $DotNetPath publish $mcpProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $appOutput
if ($LASTEXITCODE -ne 0) { throw 'VoiceAnything.Mcp publish failed.' }

$gadgetSource = if ($GadgetPath) { [IO.Path]::GetFullPath($GadgetPath) } else {
    Join-Path $env:ProgramData 'RemoteMicRC003\hid-tap\17.15.3-x64-6fca4007b228\RemoteMicRC003HidTap.dll'
}
if (-not (Test-Path -LiteralPath $gadgetSource -PathType Leaf)) {
    throw "Verified RC003MS HID runtime is missing: $gadgetSource"
}
$gadgetHash = (Get-FileHash -LiteralPath $gadgetSource -Algorithm SHA256).Hash
if ($gadgetHash -ne '6FCA4007B2284C765A6C15C967A741F536B5865BF83867326A54029A3B752748') {
    throw "Unexpected RC003MS HID runtime fingerprint: $gadgetHash"
}
Copy-Item -LiteralPath $gadgetSource -Destination (Join-Path $bridgeOutput 'RemoteMicRC003HidTap.dll')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\licenses') -Destination (Join-Path $OutputDirectory 'licenses') -Recurse
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\UPSTREAM-NOTICES.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE.md') -Destination $appOutput
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $appOutput
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\licenses') -Destination (Join-Path $appOutput 'licenses') -Recurse

# PDB files are not required by the installed application and retain internal
# project names. Keep the distributable limited to VoiceAnything runtime files.
Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.pdb' -File -Recurse |
    Remove-Item -Force

Write-Host "VoiceAnything installer package: $OutputDirectory"
