## Scope

- Related issue: <!-- SN-xxx / #... -->
- Approved HTML prototype acceptance IDs (if UI/MCP): <!-- e.g. A-03, M-09 -->
- Changes to original upstream XAML, injector or launcher? Explain conflict surface:
- Design screenshots Light/Dark, or N/A:

## Compatibility and verification

- [ ] `net462` restore/compile tested, or explain why not run
- [ ] Affected modern WPF runtime and x86/x64 smoke tested, or explain why not run
- [ ] Dependency closure/PolyfillLib DLL compatibility checked when applicable
- [ ] RESX resources in neutral and zh-CN; runtime switch isolated from target process, or N/A
- [ ] Functional behavior compared against HTML acceptance IDs, or N/A
- [ ] Failures, cancellation, target-exit and re-attach paths considered

## Safety and upstream maintenance

- [ ] Never modify inspected application's global Culture or Application.Resources
- [ ] MCP attach/write consent, scoped session, execution-time recheck, and audit preserved, or N/A
- [ ] Ms-PL license, copyright and attribution preserved
- [ ] Source changes are mergeable from upstream; conflicts documented
- [ ] Commands executed, OS/SDK/architecture, results and log links included
- [ ] **Not-run checks** explicitly declared with reason

Do not merge a PR based only on passing HTML/Node simulation tests. WPF injection and MCP security need separate evidence.
