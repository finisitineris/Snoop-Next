# P0 baseline and validation status

**Initial repository snapshot taken on 2026-11-20. Later build evidence is recorded below. Actual WPF injection: NOT RUN.**

| Item | Baseline |
|---|---|
| Fork/default branch | `finisitineris/Snoop-Next`, `develop` |
| Fork HEAD, upstream `develop` HEAD | `2bf48f60c2bb1753001f6be9122395def9070fa9` (identical at inspection) |
| Other branches at initial inspection | None |
| `global.json` | SDK `10.0.400`, `rollForward: feature` |
| `Directory.build.props` | `net462` minimum, `net6.0-windows` core baseline, `LangVersion=10` globally |
| `Snoop.Core.csproj` | `net6.0-windows;net462` from props, `LangVersion=14.0` override |
| Tests | `Snoop.Core.Tests` targets `net6.0-windows;net8.0-windows;net10.0-windows` |
| `Snoop` launcher | `net462` |
| InjectorLauncher | `net462` default; x86/x64 and special .NET Core ARM64 build |
| CI | Windows `build.cmd CI` in `.github/workflows/continuous.yml` |
| License | Ms-PL, `License.txt` |

### Existing CI incompatibility candidate

`continuous.yml` installs SDK **10.0.100**, while `global.json` requires **10.0.400** with feature-band roll-forward. A preinstalled 10.0.400 on the runner may mask the mismatch; verify actual selected SDK and fix in a separate CI PR. Workflow also uses `actions/checkout@v1` and should be modernized separately.

### Reproducible baseline commands for Windows runners

Use a Windows machine with Visual Studio WPF tooling, applicable targeting packs and the SDK required by `global.json`.

```powershell
dotnet --info
dotnet --list-sdks
dotnet restore .\Snoop.sln
dotnet build .\Snoop.sln -c Release --no-restore
.\build.cmd CI
```

These are *commands to execute*, **not claimed successful runs**. Capture SDK selection, OS/build, return codes and CI logs. Some packaging steps may need more tools; failures are evidence, not grounds to silently change release settings.

### Injection smoke

On trusted Windows WPF harnesses, test: discover process → attach → enumerate visual/logical tree → inspect DependencyProperty value/source and bindings → safely change a property if applicable → detach → exit and reattach. Record target runtime, x86/x64, process identity, Snoop injector bitness, exceptions and responsiveness. Do not inject into production programs for testing.

### Gates not yet met

- Branch protection/ruleset applied on `develop`
- Actual Windows SDK/CI build pass
- net462 and net10 successful attach + inspect + detach, with evidence
- .NET 11 harness and DLL-mode PolyfillLib dependency-closure/loader validation
- P0 artifact-to-WPF acceptance traceability verified by a real implementation

Do not label these passed based on HTML simulations.

## P0 implementation evidence update — 2026-11-20

- [PR #7](https://github.com/finisitineris/Snoop-Next/pull/7) is **merged** into `develop` as commit `c36ee903651c64200efa03b8fe828b0e70fa0313`. SDK setup now follows `global.json`; the explicit vulnerable SourceLink 8.0.0 package was removed.
- [Windows CI #44692566884](https://github.com/finisitineris/Snoop-Next/actions/runs/44692566884) **passed**: SDK 10.0.403, `build.cmd CI`, ZIP/MSI packaging and NUnit **17/17 on each** net6/net8/net10 (51/51 test executions). `Snoop.Core` compiled for net462 and net6. Compilation is **not injection** evidence.
- Branch `develop` has an active PR/anti-force-push Ruleset. **Required CI status checks have not yet been configured**; add `windows-latest` after reviewing the rule.
- [SN-004 PR #9](https://github.com/finisitineris/Snoop-Next/pull/9) provides a separate compile-only harness matrix: net462 x86/x64, net6 and net10. [Run #44694680950](https://github.com/finisitineris/Snoop-Next/actions/runs/44694680950) passed six harness builds; actual interactive attach/inspect/detach remains **NOT RUN**.
- [SN-006 #8](https://github.com/finisitineris/Snoop-Next/issues/8) tracks remaining Nuke bootstrapper NU190x security warnings. No security auditing was disabled.
