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

## Still unverified after this CI

- Actual **Snoop injected-process** assembly loading and target application assembly resolution / binding redirects. This is a separate **human-authorized isolated Windows desktop** test under SN-004; never inject into arbitrary processes on a hosted CI runner.
- Minimum **4.6.2-only machine** compatibility, x86 injection, Windows AppDomain collisions and binding redirect interactions
- PolyfillLib size/transitive dependency **acceptance** for the Snoop probe, where minimizing DLL deployment matters
- .NET 11 build and actual WPF target compatibility
- C# compiler-syntax polyfills: a package cannot backport a C# 15 language parser. C# 15 union syntax requires the .NET 11 compiler; this experiment does not test it.

Do not conclude full net462-to-net11 injection compatibility from an isolated console smoke. Do **not** add PolyfillLib to Snoop.Core until a deployment/loader design is agreed.
