# SN-004 desktop smoke, 26–27 November 2026

The tester reported **PASS on all six WPF targets** for Snoop `6.2.0-alpha.2154`, commit `e49c433f34bdf9dce42d6c075e666047a42f9ba9`. The tests ran in a non-elevated interactive session on Windows 11 25H2, build `26200.9168`, using portable Snoop and the patched HelloWorld fixture. The tester verified SHA-256 for 19 files from the original five builds and for five files from the additional .NET 11 build. The CI package for the same commit is available from the [Windows build](https://github.com/finisitineris/Snoop-Next/actions/runs/45785486559).

## Desktop results and target identities

A and B are separate launches of the same target. The identity columns give **PID / local start time / decimal window handle (HWND)**. Dates and times are UTC+8. The two net462-targeted processes ran on .NET Framework **4.8.1**, registry Release `533509`, with `clr.dll` version `4.8.9363.0` from Framework or Framework64, according to bitness. The modern rows give the observed CoreCLR and WPF version.

| Date | Target / actual runtime | Instance A | Instance B | Trees and value origins | Active Tag binding | ToolTip error and locate | Detach and reattach to A | Restart and reject stale A entry |
|---|---|---|---|---|---|---|---|---|
| Nov 26 | net462 x64 / 4.8.1 | 26304 / 17:39:48 / 1053302 | 36304 / 17:51:15 / 5051846 | PASS | PASS | PASS | PASS | PASS |
| Nov 26 | net462 x86 / 4.8.1 | 14080 / 17:53:17 / 12196424 | 42812 / 17:58:27 / 4263782 | PASS | PASS | PASS | PASS | PASS |
| Nov 26 | .NET 10 x64 / 10.0.15 | 2004 / 18:00:02 / 29361498 | 19940 / 18:04:15 / 8592386 | PASS | PASS | PASS | PASS | PASS |
| Nov 26 | .NET 8 x64 / 8.0.34 | 45244 / 18:06:09 / 12458568 | 44060 / 18:10:53 / 7084458 | PASS | PASS | PASS | PASS | PASS |
| Nov 27 | .NET 6 x64 / 6.0.36 | 40984 / 09:14:00 / 29623098 | 49668 / 09:19:09 / 13896352 | PASS | PASS | PASS | PASS | PASS |
| Nov 27 | .NET 11 x64 / 11.0.0 | 57192 / 09:20:59 / 15995996 | 40280 / 09:24:46 / 25237092 | PASS | PASS | PASS | PASS | PASS |

The .NET 6 fixture initially showed a missing-runtime dialog on Nov 26. All five checks passed on Nov 27 after installing the x64 Desktop Runtime 6.0.36. The .NET 11 process loaded WPF 11.0.0 and the portable package's `net6.0-windows/Snoop.Core.dll`.

## Observations to reproduce

1. Start the matching `HelloWorld.exe` without arguments and choose its PID in the Snoop launcher. In both the visual and logical trees, select `theTextBlock`. Its `Text` is `Hello World` from `Local`. Its `FontSize` of `48` and `Foreground` of `#FF00008B` (DarkBlue) come from `Style`.
2. Open the `Tag` value-source details. The tester saw path `Title`, source `HelloWorld.MainWindow`, status `Active`, effective value `Hello World WPF`, base source `Local` and `IsExpression=True`. Diagnostics listed `ToolTipService.ToolTip`, path `MissingForSnoopSmoke`, status `PathError`. **Locate** selected the same control. The diagnostics view also listed the fixture's original `Visibility` error.
3. Close the inspector, maximize the target and check repaint. Reattach to A and confirm the same control is accessible. Edit `Text` to `INSTANCE-A` and read it from the target window's own accessibility tree.
4. Close A while its inspector is open. The tester observed that its inspector disappeared. Start B and try the stale A row in the launcher. This opened no inspector for B. Refresh the launcher, confirm the old selection clears, then explicitly choose B. Its inspector read the fresh `Text=Hello World` and B remained responsive. The recorded A and B PIDs differ in all six runs. This exercises stale launcher selection across process exits.

## Rebuild the fixture

The original five targets use Linux SDK 10.0.403. With a clone containing the tested commit, run `bash rebuild-linux.sh /path/to/Snoop-Next /path/to/output` from this evidence directory, then copy the complete target directory to Windows. The fixture changes add the two diagnostic bindings to the existing HelloWorld XAML. [Original fixture](https://github.com/finisitineris/Snoop-Next/blob/e49c433f34bdf9dce42d6c075e666047a42f9ba9/TestHarnesses/HelloWorld/MainWindow.xaml) · [Fixture patch](fixture.patch) · [Original five-target build script](rebuild-linux.sh) · [Five-target build manifest and output hashes](Build-Verification.json)

For .NET 11 the tester reported a successful build with .NET SDK 11.0.100 on Windows, with zero warnings or errors. The following equivalent build commands use a temporary export of the tested source. Run them in PowerShell from the repository root with .NET SDK 11.0.100 installed. Keep the output files together and run the executable on Windows with x64 .NET Desktop Runtime 11.0.0 to match the recorded run.

```powershell
$commit = 'e49c433f34bdf9dce42d6c075e666047a42f9ba9'
$patch = (Resolve-Path 'docs/next/p0/evidence/sn004-2026-11-27/fixture.patch').Path
$tempDir = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
$source = Join-Path $tempDir 'source'
$zip = Join-Path $tempDir 'source.zip'
New-Item -ItemType Directory -Path $tempDir | Out-Null
git archive --format=zip -o $zip $commit
if ($LASTEXITCODE) { throw 'Source export failed.' }
Expand-Archive -LiteralPath $zip -DestinationPath $source
Push-Location $source
try {
    git apply $patch
    if ($LASTEXITCODE) { throw 'Fixture patch failed.' }
    '{"sdk":{"version":"11.0.100","rollForward":"disable"}}' | Set-Content global.json -Encoding ascii
    if ((dotnet --version) -ne '11.0.100') { throw 'Expected .NET SDK 11.0.100.' }
    dotnet build TestHarnesses/HelloWorld/HelloWorld.csproj -c Release -f net11.0-windows `
        -r win-x64 --self-contained false -p:SnoopTestHarnessTargetFrameworks=net11.0-windows `
        -p:PlatformTarget=x64 -p:ImportDirectoryBuildTargets=false -o .\out\net11-x64
    if ($LASTEXITCODE) { throw 'Fixture build failed.' }
    (Resolve-Path '.\out\net11-x64\HelloWorld.exe').Path
} finally {
    Pop-Location
}
```

The results above are transcribed from the tester's written return (SHA-256 `0096a68e42ad63a37f0c1420db2f91c978b6fc96810714567c9b59a46a8fe659`). The tester referenced screenshots, logs, a .NET 11 manifest and a separate Windows rebuild script. Those files did not accompany the written return. The .NET 11 command sequence above was reconstructed from the reported settings and the project configuration. This archive review did not independently run that sequence or repeat the desktop tests.
