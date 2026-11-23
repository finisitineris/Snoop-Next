#Requires -Version 5.1
<#
.SYNOPSIS
Builds existing WPF test harnesses under isolated target/architecture paths.
.DESCRIPTION
COMPILE-ONLY verification. Never launches Snoop or injects into a process.
#>
[CmdletBinding()]
param(
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'WPF harness build requires Windows.'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'output\next-harness-build'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

# Reuse upstream harnesses. .NET 10 is supplied through the shared
# SnoopTestHarnessTargetFrameworks MSBuild property; no csproj retargeting.
$matrix = @(
    @{ Project = 'BasicTestHarness'; Framework = 'net462'; Architecture = 'x86' },
    @{ Project = 'BasicTestHarness'; Framework = 'net462'; Architecture = 'x64' },
    @{ Project = 'BasicTestHarness'; Framework = 'net6.0-windows'; Architecture = 'x64' },
    @{ Project = 'BasicTestHarness'; Framework = 'net10.0-windows'; Architecture = 'x64' },
    @{ Project = 'HelloWorld'; Framework = 'net462'; Architecture = 'x64' },
    @{ Project = 'HelloWorld'; Framework = 'net10.0-windows'; Architecture = 'x64' },
    @{ Project = 'MultipleDispatchers'; Framework = 'net462'; Architecture = 'x86' },
    @{ Project = 'MultipleDispatchers'; Framework = 'net462'; Architecture = 'x64' },
    @{ Project = 'MultipleDispatchers'; Framework = 'net10.0-windows'; Architecture = 'x64' }
)
$results = @()
$commit = (& git -C $repoRoot rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify repository commit.' }
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'dotnet CLI is not available.' }

try {
    foreach ($case in $matrix) {
        $name = $case.Project
        $framework = $case.Framework
        $arch = $case.Architecture
        $projectFile = Join-Path $repoRoot ('TestHarnesses\{0}\{0}.csproj' -f $name)
        if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
            throw "Missing upstream harness project: $projectFile"
        }

        $casePath = '{0}\{1}\{2}' -f $name, $framework, $arch
        $out = Join-Path $OutputRoot $casePath
        # A shared intermediate directory can reuse the first architecture's binary.
        $intermediate = Join-Path $OutputRoot "obj\$casePath"
        New-Item -ItemType Directory -Path $out -Force | Out-Null
        Write-Host "BUILD-ONLY: $name / $framework / $arch"
        $dotnetArgs = @(
            'build', $projectFile,
            '--configuration', 'Release',
            '--framework', $framework,
            '--nologo',
            "-p:SnoopTestHarnessTargetFrameworks=$framework",
            "-p:PlatformTarget=$arch",
            "-p:IntermediateOutputPath=$intermediate/",
            '-p:AppendTargetFrameworkToOutputPath=false',
            "-p:OutputPath=$out"
        )
        & dotnet @dotnetArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build $name $framework $arch (dotnet exit $LASTEXITCODE)"
        }

        $exe = Join-Path $out ($name + '.exe')
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
            throw "Build finished, but harness executable not found: $exe"
        }
        $results += [pscustomobject]@{
            project = $name
            targetFramework = $framework
            platformTarget = $arch
            executable = $exe
            compilation = 'PASS'
            launch = 'NOT_RUN'
            attach = 'NOT_RUN'
            detach = 'NOT_RUN'
        }
    }
}
finally {
    # Mark the evidence as compilation-only even if a build fails mid-matrix.
    $manifest = [pscustomobject]@{
        schemaVersion = 1
        commit = "$commit".Trim()
        dotnetSdk = $sdk
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        note = 'This manifest proves compilation only; no WPF process injection was attempted.'
        cases = $results
    }
    $manifestPath = Join-Path $OutputRoot 'manifest.json'
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Compile-only manifest: $manifestPath"
}
