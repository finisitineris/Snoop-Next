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
