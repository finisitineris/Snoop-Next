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
