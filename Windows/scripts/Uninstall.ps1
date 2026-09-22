[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$installDirectory = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'VoiceAnything'))
$scriptFile = Join-Path $installDirectory 'Uninstall.ps1'
$uninstallScriptPath = [IO.Path]::GetFullPath($PSCommandPath)
$serviceName = 'VoiceAnythingHidBridge'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoiceAnything'

# No caller-supplied deletion targets. Refuse redirected directories and an
# uninstaller copied outside the administrator-owned installation directory.
function Assert-InstallDirectory {
    if (-not $uninstallScriptPath.Equals($scriptFile, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Run the installed uninstaller from Windows Settings.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'install-receipt.txt') -PathType Leaf)) {
        throw 'The Voice Anything installation receipt is missing.'
    }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($installDirectory)
    while ($pending.Count -gt 0) {
        $item = Get-Item -LiteralPath $pending.Pop() -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'The installation contains a redirected path. No files were removed.'
        }
        if ($item.PSIsContainer) {
            foreach ($child in @(Get-ChildItem -LiteralPath $item.FullName -Force)) { $pending.Push($child.FullName) }
        }
    }
}

# Permit the validation function to be checked in isolated temporary fixtures.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    Assert-InstallDirectory
    if ($WhatIfPreference) {
        $null = $PSCmdlet.ShouldProcess($installDirectory, 'Uninstall Voice Anything; retain personal data and shared audio/HID runtimes')
        exit 0
    }
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        Start-Process -FilePath $powershell -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $scriptFile + '"'))
        exit 0
    }
    Add-Type -AssemblyName PresentationFramework
    $choice = [System.Windows.MessageBox]::Show('卸载 Voice Anything？请先从托盘退出应用，以便恢复麦克风。回眸、统计、授权和设置会保留在本机。', '卸载 Voice Anything', 'YesNo', 'Question')
    if ($choice -ne 'Yes') { exit 0 }

    $appExecutable = Join-Path $installDirectory 'App\VoiceAnything.exe'
    $mcpExecutable = Join-Path $installDirectory 'App\VoiceAnything.Mcp.exe'
    foreach ($process in @(Get-Process -Name VoiceAnything,VoiceAnything.Mcp -ErrorAction SilentlyContinue)) {
        if ($process.Path -eq $appExecutable -or $process.Path -eq $mcpExecutable) {
            throw '请先退出 Voice Anything，并断开 Agent 的 MCP 连接，再重新卸载。'
        }
    }
    $service = Get-CimInstance Win32_Service -Filter "Name='VoiceAnythingHidBridge'"
    if ($null -ne $service) {
        $expectedCommand = '"' + (Join-Path $installDirectory 'HidBridge\VoiceAnything.HidBridge.exe') + '" --service'
        if (-not $service.PathName.Equals($expectedCommand, [StringComparison]::OrdinalIgnoreCase)) {
            throw '后台服务路径与安装记录不符，已停止卸载。'
        }
        $controller = Get-Service -Name $serviceName
        if ($controller.Status -ne 'Stopped') {
            Stop-Service -Name $serviceName
            $controller.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
        & (Join-Path $env:SystemRoot 'System32\sc.exe') delete $serviceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '后台服务未能删除，请重试。' }
    }
    Assert-InstallDirectory
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
    $shortcut = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'Programs\VoiceAnything.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Force }
    [System.Windows.MessageBox]::Show('Voice Anything 已卸载。个人记录和设置已保留；虚拟音频驱动及共享 HID 运行库未删除。', '卸载完成', 'OK', 'Information') | Out-Null
} catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, '卸载未完成', 'OK', 'Error') | Out-Null
    exit 1
}
