# P0 target-runtime compatibility smoke matrix

The CI column records WPF fixture builds. Runtime results come from tester reports for the linked Snoop and fixture builds. `NOT RUN` means this matrix has no recorded result for that check.

| WPF fixture target | Bitness | Fixture build (CI) | Attach and inspect (manual) | Reattach to same PID | Restart target and reject stale handles | Inspect bindings and property origins |
|---|---|---|---|---|---|---|
| net462 (CLR4) | x86 | PASS | PASS | PASS | PASS | PASS |
| net462 (CLR4) | x64 | PASS | PASS | PASS | PASS | PASS |
| net6.0-windows | x64 | PASS · BasicTestHarness | PASS | PASS | PASS | PASS |
| net8.0-windows | x64 | NOT RUN | PASS | PASS | PASS | PASS |
| net10.0-windows | x64 | PASS | PASS | PASS | PASS | PASS |
| net11.0-windows | x64 | NOT RUN | PASS | PASS | PASS | PASS |

Windows CI built nine fixture configurations and checked the CLR4 executable architectures against their PE headers and CLR flags at `ac3ef8f`. The workflow includes the .NET 6 x64 BasicTestHarness. Its manifest records compilation separately from interactive checks. [Harness CI](https://github.com/finisitineris/Snoop-Next/actions/runs/45096083280/job/137436074864) · [Build matrix at the tested commit](https://github.com/finisitineris/Snoop-Next/blob/ac3ef8ffe52d917ca374694e6a9dae59384965c3/scripts/next/Build-SnoopHarnesses.ps1)

Earlier Windows desktop runs also verified language switching and same-process reattach on three target configurations.

- **CLR4 x86.** The net462 fixture showed two 32-bit windows. After reattaching to the same process, the tester read each window's `Title`, saw both inspectors switch to Chinese, and confirmed the target cultures and resources stayed unchanged. [PR #27](https://github.com/finisitineris/Snoop-Next/pull/27)
- **CLR4 x64.** The tester confirmed Chinese on reattach in two AppDomains using HelloWorld and, in a separate run, in two dispatchers using MultipleDispatchers. Both runs preserved the observed target cultures and resources. The reports identify CLR4 and the net462 build target. The exact .NET Framework installation on the tester's machine was not recorded. [PR #23](https://github.com/finisitineris/Snoop-Next/pull/23) · [PR #25](https://github.com/finisitineris/Snoop-Next/pull/25)
- **.NET 10 x64.** The tester confirmed same-PID reattach, read each window's `Title`, and observed the expected language switch with target cultures and resources preserved. The separate fixture launch check recorded Desktop Runtime 10.0.15. [PR #26](https://github.com/finisitineris/Snoop-Next/pull/26)

On November 26–27, 2026, the tester ran the external HelloWorld fixture on Windows 11 25H2 build 26200.9168 with portable Snoop 6.2.0-alpha.2154 (`e49c433`). Both CLR4 rows used net462 targets on installed .NET Framework 4.8.1. The four modern x64 rows ran WPF 6.0.36, 8.0.34, 10.0.15 and 11.0.0. All six rows passed the tree, value-source, binding, diagnostic, detach/reattach and target-restart checks. The .NET 8 and .NET 11 samples were built locally. The fixture-build column tracks the separate CI workflow. [Desktop observations, process identities and reproduction](evidence/sn004-2026-11-27/README.md) · [SN-004](https://github.com/finisitineris/Snoop-Next/issues/5)

The full matrix will later include .NET 7/9, available x86/ARM64 paths, DPI and launcher compatibility. Upstream documents a restriction on self-contained single-file WPF processes; do not assume support without a tested design.

Every test run must capture commit SHA, Windows version, installed SDK and target runtime, process/injector architecture, exact command, logs, elapsed time, status (PASS/FAIL/BLOCKED/NOT RUN) and relevant screenshots. Record any target culture/resource mutation as a blocker.

Run fast compiler/static checks on small PRs. Reserve costly multi-runtime injection testing for P0 gates, core loader changes, MCP/probe security changes and release readiness. Self-hosted runners must be isolated; never run arbitrary PR code with privileged production credentials.

## CLR4 multi-AppDomain language reattach

Use the repository-owned HelloWorld target for the secondary AppDomain reattach case in [SN-009 #17](https://github.com/finisitineris/Snoop-Next/issues/17). The `next-harness-build` workflow compiles the target and uploads the `sn009-clr4-multi-appdomain-harness` artifact for this manual test.

1. Extract the CLR4 x64 harness artifact on a Windows test machine and run `./HelloWorld.exe --multi-appdomain`. Confirm that the Primary and Secondary windows show distinct AppDomains and the same process ID. Record both windows' culture, UI culture, resource marker, and resource counts.
2. In the Snoop build under test, save English as the host language and attach to that HelloWorld process through the Snoop UI. Accept the multiple AppDomain prompt and confirm that the workbench for each target appears in English.
3. Close both workbench windows and leave both HelloWorld windows running. Save Simplified Chinese in the host and reattach to the same HelloWorld process. Both workbenches should display Chinese, including `文件` in the secondary AppDomain.
4. Click **Refresh target state** in both HelloWorld windows and compare the displayed cultures, resource markers, and counts with step 1. Close the primary HelloWorld window to finish.

Record the Snoop and harness artifact links, Windows and CLR versions, PID, and the result for each AppDomain. Mark the row after running the real attach/detach/reattach sequence.

## CLR4 multi-Dispatcher language reattach

Use the repository-owned CLR4 x64 `sn009-clr4-multi-dispatcher-harness` artifact from `next-harness-build` for [SN-009 #17](https://github.com/finisitineris/Snoop-Next/issues/17).

1. Extract the artifact and run `./MultipleDispatchers.exe --multi-dispatcher`. Confirm that **Primary** and **Secondary** have the same PID and AppDomain ID and different STA thread IDs. Record both windows' culture values and the shared application resource marker and counts shown by Primary.
2. In Snoop settings, set `MultipleDispatcherMode` to `Ask` or `AlwaysUse` and save English as the UI language. Open the workbench for that fixture PID. If prompted to enter multiple Dispatcher mode, choose **Yes**. Confirm that both workbenches show **File**. Use the target tree to identify `PrimaryDispatcherWindow` and `SecondaryDispatcherWindow`.
3. Close both workbenches while leaving both fixture windows open. Save Simplified Chinese in Snoop and reattach to the same PID in multiple Dispatcher mode. Both workbenches should now show **文件** and still inspect the separate target windows.
4. Select **Refresh target state** on both fixture windows. Compare cultures and Primary's shared application resource marker and counts with step 1. Confirm both targets remain responsive, then close the workbenches and fixture windows.

For each repeat run, record the MSI and fixture artifact links, Windows and CLR versions, same PID and AppDomain, distinct thread IDs, both workbench languages, and the culture and resource comparison.

## .NET 10 x64 target load and language reattach

On a Windows x64 machine with the .NET 10 Desktop Runtime (x64), extract all files from the framework-dependent `sn009-net10-x64-multi-dispatcher-harness` artifact. Run `./MultipleDispatchers.exe --multi-dispatcher`. Confirm that both windows report `Runtime: 10.0.x (64-bit)`, share a PID and AppDomain ID, and have distinct STA thread IDs.

Use the same attach and reattach sequence and target-state comparison as the CLR4 multi-Dispatcher case above. In each workbench, select the target root window and confirm that its `Title` property matches the corresponding fixture window. Record the actual runtime version, both workbench languages, and the before and after target state.

## net462 x86 target load and language reattach

Extract the `sn009-clr4-x86-multi-dispatcher-harness` artifact on the Windows test machine and run `./MultipleDispatchers.exe --multi-dispatcher`. Confirm that both windows report CLR 4 and `32-bit`, with one PID and AppDomain ID and distinct STA thread IDs. Record the installed .NET Framework version separately from the net462 build target.

Repeat the CLR4 multi-Dispatcher attach and same-PID reattach sequence above with the x86 target. Check that each workbench reads the `Title` of its corresponding window, that both menus switch from **File** to **文件**, and that the target cultures and application resources stay unchanged.
