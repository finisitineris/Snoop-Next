# P0 target-runtime compatibility smoke matrix

**Every cell is NOT RUN until evidence is attached from an actual Windows machine.** The goal is future support of .NET Framework 4.6.2 through .NET 11; existing compilation settings alone are not proof of support.

| WPF target runtime | Bitness | Build | Attach + inspect | Exit/re-attach | Bindings/property origins |
|---|---|---|---|---|---|
| net462 | x86 | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| net462 | x64 | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| net6 | x64 | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| net8 | x64 | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| net10 | x64 | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| net11 | x64 | NOT RUN | NOT RUN | NOT RUN | NOT RUN |

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

Record the MSI and fixture artifact links, Windows and CLR versions, same PID and AppDomain, distinct thread IDs, both workbench languages, and the culture and resource comparison. Keep the manual result pending until the real attach and reattach sequence is complete.

## .NET 10 x64 target load and language reattach

On a Windows x64 machine with the .NET 10 Desktop Runtime (x64), extract all files from the framework-dependent `sn009-net10-x64-multi-dispatcher-harness` artifact. Run `./MultipleDispatchers.exe --multi-dispatcher`. Confirm that both windows report `Runtime: 10.0.x (64-bit)`, share a PID and AppDomain ID, and have distinct STA thread IDs.

Use the same attach and reattach sequence and target-state comparison as the CLR4 multi-Dispatcher case above. In each workbench, select the target root window and confirm that its `Title` property matches the corresponding fixture window. Record the actual runtime version, both workbench languages, and the before and after target state. Keep the .NET 10 attach and reattach results pending until those steps are run against this target.
