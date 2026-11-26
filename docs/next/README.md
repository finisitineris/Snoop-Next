# Snoop Next — Engineering baseline

This is the approved **design and compatibility reference** for the independent [Snoop WPF fork](https://github.com/finisitineris/Snoop-Next). It is not a completed WPF/MCP implementation.

## Reference materials

- [Fork lineage and development rules](adr/0001-fork-governance.md)
- [Actual source baseline and Windows verification gate](p0/BASELINE.md)
- [Runtime/architecture smoke matrix](p0/SMOKE-MATRIX.md)
- [Approved interactive HTML v1.1](prototype/Snoop_Next_Interactive_Prototype_v1.1.html) — **mock data; no real process or MCP server**
- [42 prototype acceptance requirements](prototype/Snoop_Next_Prototype_Acceptance_v1.1.md)
- [Architecture and compatibility](specs/01-architecture-and-compatibility.md)
- [WPF UI mapping](specs/02-ui-interaction-mapping.md)
- [RESX localization](specs/03-resx-localization.md)
- [MCP/IPC contract and consent rules](specs/04-mcp-host-and-ipc-contract.md)
- [Implementation backlog SN-001—SN-054](specs/05-implementation-backlog-and-qa.md)

## Non-negotiable decisions

- Support targets: **net462** through modern WPF/.NET 11, confirmed by actual target-runtime tests, not merely compilation.
- Use modern C# where it does not require incompatible CLR behavior; **PolyfillLib** is a DLL-mode library dependency to validate separately on net462.
- UI: Visual Studio workbench layout + Fluent appearance + DevTools property/value-origin interactions.
- Localization: **RESX + lightweight dynamic WPF binding**, English and zh-CN without changing inspected process global Culture or Application.Resources.
- MCP: external host, manually started, default read-only. AI attach requires consent; writes require once/scoped-session approval and execution-time revalidation.
- Retain upstream Ms-PL copyright/attribution notices and selective upstream mergeability.

This P0 documentation does **not** modify source targets or injector behavior. The HTML prototype's successful browser/Node tests do not establish Windows WPF compatibility. Every real implementation PR must reference the acceptance IDs and record verified test evidence.

## Current test status

- Source baseline: merged Windows CI fix [PR #7](https://github.com/finisitineris/Snoop-Next/pull/7) and [passing run #44692566884](https://github.com/finisitineris/Snoop-Next/actions/runs/44692566884).
- Windows CI ran 51 core NUnit test executions on .NET 6, 8 and 10. Testers separately confirmed attach and same-process reattach on net462-targeted CLR4 fixtures (x86 and x64) and a .NET 10 x64 fixture. The .NET 11 desktop lane remains untested. [Runtime results and remaining checks](p0/SMOKE-MATRIX.md)
- Non-elevated desktop smoke protocol / compile-only harnesses prepared in [PR #9](https://github.com/finisitineris/Snoop-Next/pull/9).
- Remaining inherited Nuke bootstrapper advisory cleanup is tracked in [SN-006](https://github.com/finisitineris/Snoop-Next/issues/8).
