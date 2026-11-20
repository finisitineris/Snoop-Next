#Requires -Version 5.1
<#
.SYNOPSIS
Interactive, opt-in WPF inspection smoke session for trusted test harnesses only.
.DESCRIPTION
Not for GitHub-hosted CI or production applications. Always records human
observation separately from the inspector's process exit code.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HarnessExecutable,
    [Parameter(Mandatory = $true)]
    [string]$SnoopExecutable,
    [string]$Scenario = 'manual',
    [string]$ReportPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows desktop is required.' }
if (-not [Environment]::UserInteractive) { throw 'Interactive Windows desktop is required.' }
if ([System.Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) {
    throw 'Session 0 and unattended runner execution are prohibited.'
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this test from a non-elevated account; do not inject as administrator.'
}
$HarnessExecutable = [System.IO.Path]::GetFullPath($HarnessExecutable)
$SnoopExecutable = [System.IO.Path]::GetFullPath($SnoopExecutable)
foreach ($file in @($HarnessExecutable, $SnoopExecutable)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing executable: $file"
    }
}

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportFolder = Join-Path $root 'output\next-smoke-manual'
    $ReportPath = Join-Path $reportFolder ('{0}-{1}.json' -f $Scenario, [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $ReportPath) -Force | Out-Null

$record = [ordered]@{
    schemaVersion = 1
    scenario = $Scenario
    startedUtc = [DateTime]::UtcNow.ToString('o')
    harnessExecutable = $HarnessExecutable
    snoopExecutable = $SnoopExecutable
    targetPid = $null
    targetStartUtc = $null
    targetMainWindowHandle = $null
    inspectorLauncherExitCode = $null
    inspectionObserved = $false
    detachObserved = $false
    status = 'NOT_RUN'
    note = 'Human-confirmed observations on a controlled non-elevated WPF test process only.'
}
$target = $null
try {
    Write-Host 'Only use locally built, trusted Snoop + WPF test harnesses.'
    $answer = Read-Host 'Type START to launch the controlled WPF harness'
    if ($answer -cne 'START') {
        $record.status = 'CANCELLED'
        return
    }

    $target = Start-Process -FilePath $HarnessExecutable -PassThru
    $record.targetPid = $target.Id
    $record.targetStartUtc = $target.StartTime.ToUniversalTime().ToString('o')
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $target.Refresh()
        if ($target.HasExited) { throw 'Test harness exited before inspection.' }
        if ($target.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    if ($target.MainWindowHandle -eq [IntPtr]::Zero) {
        throw 'No main window detected. This is not evidence of successful WPF attachment.'
    }
    $record.targetMainWindowHandle = $target.MainWindowHandle.ToInt64()
    Write-Host "Harness PID $($target.Id), instance start $($record.targetStartUtc)"
    $answer = Read-Host 'Type ATTACH to explicitly authorize Snoop inspect on THIS harness PID'
    if ($answer -cne 'ATTACH') {
        $record.status = 'CANCELLED'
        return
    }

    # Snoop's existing CLI: inspect --targetPID <PID>. Exit code alone is
    # not evidence that the inspector actually displayed the visual tree.
    $launcher = Start-Process -FilePath $SnoopExecutable -ArgumentList @('inspect', '--targetPID', "$($target.Id)") -PassThru
    if ($launcher.WaitForExit(15000)) {
        $record.inspectorLauncherExitCode = $launcher.ExitCode
    } else {
        Write-Warning 'Launcher is still running; confirm result from the actual UI.'
    }

    $target.Refresh()
    if ($target.HasExited) { throw 'Target exited during attempted attachment.' }
    Write-Host 'Inspect the actual Snoop window. Confirm visible WPF visual/logical tree, property values, and binding source.'
    $answer = Read-Host 'Type TREE_OK only if the inspector UI really showed the expected controls'
    if ($answer -cne 'TREE_OK') {
        $record.status = 'MANUAL_FAIL'
        return
    }
    $record.inspectionObserved = $true
    $answer = Read-Host 'Close the Snoop inspector, then type DETACHED only if the target remains responsive'
    if ($answer -cne 'DETACHED') {
        $record.status = 'INCOMPLETE'
        return
    }
    $target.Refresh()
    if ($target.HasExited) { throw 'Target exited during detach verification.' }
    $record.detachObserved = $true
    $record.status = 'MANUAL_PASS'
}
catch {
    $record.status = 'ERROR'
    $record.note = $_.Exception.Message
    throw
}
finally {
    $record.endedUtc = [DateTime]::UtcNow.ToString('o')
    if ($null -ne $target) {
        try {
            $target.Refresh()
            if (-not $target.HasExited) { Stop-Process -Id $target.Id -Force -ErrorAction Stop }
        }
        catch { Write-Warning "Could not close controlled harness process: $_" }
        $target.Dispose()
    }
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    Write-Host "Manual evidence report: $ReportPath"
}
