# SN-004 — Multi-runtime WPF smoke validation

This is a **two-layer test**. Automated Windows Actions builds existing WPF test harnesses; actual injection/inspection runs **only in a non-elevated interactive Windows desktop session**, with explicit approval and human observation. No CI job launches or injects into a test process.

## Build-only CI

The workflow in [.github/workflows/next-harness-build.yml](../../../.github/workflows/next-harness-build.yml) invokes [Build-SnoopHarnesses.ps1](../../../scripts/next/Build-SnoopHarnesses.ps1). It compiles the existing upstream \`BasicTestHarness\` and \`HelloWorld\` for:

| Harness | Framework | PlatformTarget | What this proves |
|---|---|---|---|
| BasicTestHarness | net462 | x86 | Compile only |
| BasicTestHarness | net462 | x64 | Compile only |
| BasicTestHarness | net6.0-windows | x64 | Compile only |
| BasicTestHarness | net10.0-windows | x64 | Compile only |
| HelloWorld | net462 | x64 | Compile only |
| HelloWorld | net10.0-windows | x64 | Compile only |

The project \`TestHarnesses/Directory.Build.props\` already provides \`SnoopTestHarnessTargetFrameworks\`; the script overrides this **for each build**, rather than editing upstream project files. Outputs are architecture-separated under \`output/next-harness-build\`.

The JSON manifest always marks \`launch\`, \`attach\` and \`detach\` as \`NOT_RUN\`. Passing CI must **never** make those values PASS.

## Manual interactive verification

Prerequisites: an isolated Windows desktop VM, installed target runtime(s), trusted local Snoop build from the same commit, test harness built with the matching platform, non-elevated account, normal interactive desktop (not a headless CI service/session 0). Do not use real or sensitive applications as targets.

From the repo root in **PowerShell** (local example paths; adjust to actual build output):

1. Run \`.\scripts\next\Build-SnoopHarnesses.ps1\` and inspect \`output\next-harness-build\manifest.json\`.
2. Build original Snoop launcher and injector with \`.\build.cmd CI\` (or obtain trusted matching CI artifact), and verify executable paths.
3. Run \`.\scripts\next\Start-SnoopSmokeSession.ps1 -HarnessExecutable "output\next-harness-build\BasicTestHarness\net462\x64\BasicTestHarness.exe" -SnoopExecutable "bin\Release\Snoop.exe" -Scenario "net462-x64"\`.
4. The script requires two explicit approvals (START and ATTACH) and later manual confirmation TREE_OK and DETACHED. It records a PID **and process start time** so a recycled PID cannot be mistaken for the same target instance.
5. Save a screenshot of the tree and properties from the controlled harness (redact local paths/usernames). Run a **new** script invocation to test a different runtime or process instance. Include process bitness, native injection launcher used, OS build and CLR runtime in the evidence.
6. Compare \`DependencyProperty\` value origins and binding diagnostics. Where practical, manually edit a harmless property in the controlled sample, confirm the target view changes, then close Snoop; ensure the target remains responsive.

## Acceptance evidence and exclusions

Follow [SMOKE-MATRIX.md](SMOKE-MATRIX.md) for matrix rows. Mark a row PASS **only when** actual UI observations, the correct runtime/bitness, logs and screenshots are present. The standalone script does not prove binding-source correctness, safe property edits, stale handle invalidation or PID reuse; record those as separate observations and test cases. Any row lacking evidence stays NOT RUN or BLOCKED.

No \`.NET 11\` target is part of the automated matrix until a supported .NET 11 SDK, runtime and WPF harness is available; it remains NOT RUN. Self-contained single-file and ARM64 scenarios also need separate explicit investigation. Do not open privileged production processes from a build runner.

Do not close SN-004 based only on a green harness compilation workflow.
