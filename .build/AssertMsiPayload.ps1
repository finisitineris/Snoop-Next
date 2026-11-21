[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $MsiPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$invokeMethod = [System.Reflection.BindingFlags]::InvokeMethod
$getProperty = [System.Reflection.BindingFlags]::GetProperty

function Read-MsiRows($Database, [string] $Query, [string[]] $Columns) {
    $view = $Database.GetType().InvokeMember('OpenView', $invokeMethod, $null, $Database, @($Query))
    try {
        $null = $view.GetType().InvokeMember('Execute', $invokeMethod, $null, $view, $null)
        while ($null -ne ($record = $view.GetType().InvokeMember('Fetch', $invokeMethod, $null, $view, $null))) {
            try {
                $row = @{}
                for ($i = 0; $i -lt $Columns.Length; $i++) {
                    $row[$Columns[$i]] = $record.GetType().InvokeMember('StringData', $getProperty, $null, $record, @([int] ($i + 1)))
                }
                [pscustomobject] $row
            }
            finally {
                $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($record)
            }
        }
    }
    finally {
        $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    }
}

function Get-InstallRelativePath($File, [hashtable] $Directories) {
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add(($File.FileName -split '\|')[-1])
    $directoryId = $File.DirectoryId
    while ($directoryId -and $directoryId -ne 'INSTALLDIR') {
        if (!$Directories.ContainsKey($directoryId) -or $parts.Count -gt $Directories.Count) {
            throw "Invalid MSI directory chain for '$($File.FileName)' at '$directoryId'."
        }
        $directory = $Directories[$directoryId]
        # DefaultDir can encode short|long:source. Only target names are installed.
        $targetName = (($directory.DefaultDir -split ':', 2)[0] -split '\|')[-1]
        $parts.Insert(0, $targetName)
        $directoryId = $directory.Parent
    }
    if ($directoryId -eq 'INSTALLDIR') {
        ($parts | Where-Object { $_ -ne '.' }) -join '/'
    }
}

$msi = Get-Item -LiteralPath $MsiPath
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
try {
    # Open the generated MSI database read-only. This does not run the installer.
    $database = $installer.GetType().InvokeMember('OpenDatabase', $invokeMethod, $null, $installer, @($msi.FullName, 0))
    $directories = @{}
    foreach ($directory in (Read-MsiRows $database 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`' @('Id', 'Parent', 'DefaultDir'))) {
        $directories[$directory.Id] = $directory
    }
    $fileQuery = 'SELECT `File`.`FileName`, `Component`.`Directory_` FROM `File`, `Component` WHERE `File`.`Component_` = `Component`.`Component`'
    $actualPaths = @(foreach ($file in (Read-MsiRows $database $fileQuery @('FileName', 'DirectoryId'))) {
        Get-InstallRelativePath $file $directories
    })
}
finally {
    if ($null -ne $database) {
        $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    }
    $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
}

$hostLocalization = @(
    'Snoop.Next.Localization.dll'
    'zh-CN/Snoop.Next.Localization.resources.dll'
)
$requiredPaths = @(
    'Snoop.exe'
    'Snoop.exe.config'
    'Snoop.com'
    'Snoop.com.config'
    'Snoop.Core.dll'
    'CommandLine.dll'
    'Scripts/Snoop.psm1'
    'net462/Snoop.Core.dll'
    'net462/System.Management.Automation.dll'
    'net6.0-windows/Snoop.Core.dll'
    'net6.0-windows/Snoop.Core.deps.json'
    'Snoop.GenericInjector.x86.dll'
    'Snoop.GenericInjector.x64.dll'
    'Snoop.GenericInjector.ARM64.dll'
    'Snoop.InjectorLauncher.x86.exe'
    'Snoop.InjectorLauncher.x86.exe.config'
    'Snoop.InjectorLauncher.x64.exe'
    'Snoop.InjectorLauncher.x64.exe.config'
    'Snoop.InjectorLauncher.ARM64.exe'
    'Snoop.InjectorLauncher.ARM64.dll'
    'Snoop.InjectorLauncher.ARM64.dll.config'
    'Snoop.InjectorLauncher.ARM64.deps.json'
    'Snoop.InjectorLauncher.ARM64.runtimeconfig.json'
) + $hostLocalization

Write-Host "MSI File table paths under INSTALLDIR in $($msi.Name):"
$actualPaths | Sort-Object | ForEach-Object { Write-Host "  $_" }

$problems = @(
    $requiredPaths | Where-Object { $_ -notin $actualPaths } | ForEach-Object { "Missing: $_" }
    $actualPaths |
        Where-Object { $_ -match '(^|/)Snoop\.Next\.Localization(\.resources)?\.dll$' -and $_ -notin $hostLocalization } |
        ForEach-Object { "Unexpected localization placement: $_" }
)
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  $_" }
    throw "MSI payload verification failed with $($problems.Count) mismatches."
}
Write-Host "Verified $($requiredPaths.Count) required MSI install paths."
