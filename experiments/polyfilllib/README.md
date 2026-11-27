# SN-003 — PolyfillLib compiled DLL compatibility experiment

**Scope:** deliberate, isolated test of [SimonCropp/Polyfill **PolyfillLib**](https://github.com/SimonCropp/Polyfill/blob/master/polyfill-lib.md) version **11.4.5**. This is the **compiled library** package; do not confuse it with the source-only package named Polyfill.

This project is **not referenced** by Snoop or any injected code and does not alter Snoop's production NuGet graph. Its local Directory.Build.targets prevents inherited build-time references from contaminating the experimental dependency closure; its csproj opts out of root Central Package Management for this single experiment.

## Automated Windows test

Workflow: .github/workflows/polyfilllib-compat.yml, triggered only by changes to this experiment or by manual dispatch.

It checks the pinned dependency graph, compiles net462 and net10.0 console executables, then launches them locally to confirm runtime loading and reflection metadata traversal. A net462-targeted exe may execute under the later CLR 4.x installed on the Windows runner; that is **not** a guarantee it will run on a machine with only .NET Framework 4.6.2.

Expected evidence from CI:
- Full restore / NuGet security output, list of direct and transitive package IDs and versions
- Binary runtime asset list and sizes for each framework, including Polyfill*.dll where applicable
- Runtime CLR version and successful Assembly.LoadFrom/GetTypes (or precise exception)
- Separate success/failure for net462 and net10.0, plus a human-readable dependency-closure report artifact

## WPF host and CLR4 binding policy

The same workflow builds a separate `WpfHost` executable for .NET Framework 4.6.2 x86, .NET 6 x64 and .NET 10 x64. Each job opens the fixture's own WPF window on its STA, runs the loader check and exits after a second Dispatcher callback. The runtime log records the PID, AppDomain, architecture, CLR and WPF versions, assembly identities and paths, and type count. The CLR4 log also records the installed Framework release. On modern .NET, the log identifies the assembly load context for the fixture, PolyfillLib, System.Memory and any Snoop.Core present. The workflow archives the executable and adjacent files, the effective CLR4 configuration, runtime log, package list, commit and SDK details.

The fixture takes PolyfillLib as a runtime-only dependency. On CLR4, exposing the package to the compiler can make ordinary C# string interpolation use its handler before the test begins. The fixture reports observed loads and fails if its own assembly references PolyfillLib or if the DLL has already loaded before the experiment.

The CLR4 fixture models an older host request for strongly named `System.Memory` 4.0.1.2. Its executable configuration redirects that identity to the deployed 4.0.5.0 assembly. It checks the CLR policy and actual application-local path before loading the pinned PolyfillLib DLL. Each target compares the object returned for PolyfillLib's recorded System.Memory reference with the host's preloaded assembly, then traverses the library's types. The modern hosts preload their framework-provided System.Memory. The .NET 6 package graph includes Microsoft.Bcl.Memory 10.0.12, which emits a support warning for this target. Record the loader result and the package warning separately.

To repeat a run, download `polyfilllib-wpf-net462`, `polyfilllib-wpf-net6.0-windows` or `polyfilllib-wpf-net10.0-windows` from a successful compatibility workflow for the commit under test. Extract the whole artifact. In PowerShell, run `& .\fixture\WpfHost.exe` on Windows with the matching runtime. An exit code of zero and a `PASS: fixture loader` line indicate completion. For a source build, use `dotnet build experiments/polyfilllib/WpfHost/WpfHost.csproj -c Release -f net462 -o output/wpf-host` and run the resulting executable from that directory. Substitute the corresponding Windows TFM for a modern host.

For a desktop test with Snoop present, start a fresh fixture with `& .\fixture\WpfHost.exe --interactive`. Select the PID shown in the window title in the normal Snoop launcher. After its inspector opens, click **Run check** in the fixture window. It waits for a loaded `Snoop.Core` in its own AppDomain, reports its identity and path before loading PolyfillLib, and leaves the result visible. Save the text and the target runtime, Snoop commit and artifact URLs. Repeat in a fresh process for each run.

The automated result covers loading initiated by this fixture in its own AppDomain. The manual mode additionally identifies Snoop.Core in that AppDomain. Production Snoop has no dependency on PolyfillLib. Deployment by the inspector, other hosts' binding policies and an actual Framework 4.6.2-only installation still need separate evidence.

## Desktop results, 27 November 2026

The [SN-003 desktop report](https://github.com/finisitineris/Snoop-Next/issues/4) records three successful interactive runs on Windows 11 25H2 build 26200.9168. The tester used portable Snoop 6.2.0-alpha.2154 (`e49c433`) with the fixtures from the [Windows compatibility run](https://github.com/finisitineris/Snoop-Next/actions/runs/45930788892), which checked out `05d285ef`. In each fresh process the tester attached Snoop before selecting **Run check**. The host saw Snoop.Core in its default AppDomain before loading PolyfillLib 11.4.5.

| Target | Installed runtime | System.Memory resolved by the host and the library | Types loaded | Exit code |
| --- | --- | --- | ---: | ---: |
| net462 x86 | .NET Framework 4.8.1, Release 533509 | The CLR redirected 4.0.1.2 to the application's 4.0.5.0 assembly | 420 | 0 |
| .NET 6 x64 | 6.0.36 | Framework assembly 6.0.0.0 in the default load context | 263 | 0 |
| .NET 10 x64 | 10.0.15 | Framework assembly 10.0.0.0 in the default load context | 104 | 0 |

All three runs completed the subsequent Dispatcher callback. The tester selected the host's output TextBox in the inspector's logical tree, detached, maximized the host and observed it repaint. Reattaching to the same PID restored access to the tree and the host's log preview. The issue records the per-run identities from the submitted report. The three desktop logs and nine screenshots referenced by the tester were unavailable for independent review.

## Remaining coverage

- Deployment and loading initiated by the production inspector. In these runs the fixture owned both the DLLs and the load call.
- A machine with only .NET Framework 4.6.2 installed, separate AppDomains and other application binding policies.
- PolyfillLib size/transitive dependency **acceptance** for the Snoop probe, where minimizing DLL deployment matters
- .NET 11 build and actual WPF target compatibility
- C# compiler-syntax polyfills: a package cannot backport a C# 15 language parser. C# 15 union syntax requires the .NET 11 compiler; this experiment does not test it.

Do not conclude full net462-to-net11 injection compatibility from an isolated console smoke. Do **not** add PolyfillLib to Snoop.Core until a deployment/loader design is agreed.
