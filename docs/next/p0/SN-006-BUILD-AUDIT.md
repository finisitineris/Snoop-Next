# SN-006 build bootstrapper audit

The NUKE project in `.build/_build.csproj` builds Snoop's release artifacts. Its package graph is separate from the dependencies loaded in a WPF target.

## Dependency results on 26 November 2026

An isolated restore and `dotnet list .build/_build.csproj package --vulnerable --include-transitive` used NuGet.org as the package and advisory source. The table compares the old and patched dependency graphs against the same advisory data.

| Build dependency | Historical version at `07b26a0` | Advisory matches | Patched version at `e49c433` | Advisory matches |
|---|---|---:|---|---:|
| NuGet.Packaging | 6.12.1 | 1 low | 6.12.5 | 0 |
| System.Security.Cryptography.Pkcs | 9.0.6 | 1 high, 1 moderate | 9.0.23 | 0 |
| System.Security.Cryptography.Xml | 9.0.0 | 11 high | 9.0.23 | 0 |

The historical restore used SDK 10.0.403, and the patched restore used SDK 10.0.404. The patched graph contained 63 resolved PackageReference packages. The two separately downloaded tool packages, GitVersion.Tool 6.5.1 and NuGet.CommandLine 6.9.1, also had zero package-version matches. These results cover the named package versions and the advisory snapshot from that day. [Package fix](https://github.com/finisitineris/Snoop-Next/pull/11)

`dotnet nuget why` showed paths from Nuke.Common 10.1.0 through Nuke.Tooling to NuGet.Packaging and through Nuke.ProjectModel / Microsoft.Build.Tasks.Core to the XML and PKCS packages. NuGet.Packaging also contributes a path to PKCS. The bootstrapper pins the three patched packages with `PrivateAssets="all"`. Its evaluated defaults were `NuGetAudit=true`, mode `all`, level `low`, and `TreatWarningsAsErrors=false` under its project-local props. The product's root props set `TreatWarningsAsErrors=true`. [Bootstrapper](../../../.build/_build.csproj) · [Root properties](../../../Directory.build.props)

## NUKE project parsing

On a Windows SDK 10.0.403 run, NUKE 10.1.0 caught a `NuGet.Frameworks, Version=7.9.0.0` assembly load failure in `ProjectModelTasks.ParseProject` while checking the project's `NukeTelemetryVersion`. The complete CI build succeeded. On SDK 10.0.404, `e49c433` passed the full Windows build and MSI verification with telemetry opted out. Its three NUnit runs each passed all 17 tests. A separate Windows desktop run with the same commit and SDK completed `build.cmd CheckSums` with telemetry opted out and exit code 0. [Historical exception and successful build](https://github.com/finisitineris/Snoop-Next/actions/runs/44696255687/job/136153597879) · [Patched Windows build](https://github.com/finisitineris/Snoop-Next/actions/runs/45785486559/job/139791072775)

In the NUKE 10.1.0 source, the optional telemetry initialization first registers the project model and then evaluates the build project in `CheckAwareness`. Its telemetry client is created after that check. This ordering allows the assembly-resolution path to be tested directly while telemetry stays disabled. The normal CI workflow keeps `NUKE_TELEMETRY_OPTOUT=1`. [NUKE implementation](https://github.com/nuke-build/nuke/blob/10.1.0/source/Nuke.Build/Telemetry/Telemetry.cs#L36-L79)

The [isolated parser diagnostic](../../../.github/workflows/nuke-project-model.yml) compiles the real build project with an alternate test entry point. It invokes the same NUKE project-model initialization and parsing calls, with both NUKE and .NET CLI telemetry disabled. On Linux and Windows under SDK 10.0.404 it reproduced the request for assembly version 7.9.0.0 with HRESULT `0x80131040` during target-framework evaluation while the bootstrapper carried assembly version 6.12.5.1. The diagnostic distinguishes an exact reproduction from a successful parse and fails on unrecognized errors. The regular full Windows build is the control for the build and packaging steps. [Windows parser result](https://github.com/finisitineris/Snoop-Next/actions/runs/45930044995/job/140280327771)

## Artifacts and signing

The verified `e49c433` GitHub build uploaded seven files in the release artifact: ZIP, MSI, Chocolatey NUPKG, their three SHA-256 files, and a WiX symbol database. A second artifact held three TRX files. The original release artifact's SHA-256 is `a0128f7a98300ac8868fa3532903c1e7a2b412129c534a47ede804f9cb5de184`. Its WiX symbol database contains installer metadata and absolute paths from the GitHub-hosted build machine. The portable ZIP includes managed PDBs. The GitHub release-artifact file patterns retain these existing outputs. [Release artifact](https://github.com/finisitineris/Snoop-Next/actions/runs/45785486559/artifacts/15099179634) · [Test results](https://github.com/finisitineris/Snoop-Next/actions/runs/45785486559/artifacts/15099678727)

GitHub Actions runs with read-only repository contents permissions. Live signing belongs to a separate AppVeyor release path with a SignPath credential and branch/version conditions. The Windows CI build above skipped `SignArtifacts`. The build explicitly rejects AppVeyor pull requests. The helper only uses a bearer for the SignPath HTTPS origin. External HTTPS artifact-download URLs receive no bearer. HTTP status errors and signing progress messages omit response bodies and signed URLs. The audit covers the repository's eligibility checks, logging and artifact selection. A live signing request, the certificate and the external service's access policies are outside this verification. [Build configuration](../../../.build/Build.cs) · [GitHub workflow](../../../.github/workflows/continuous.yml)
