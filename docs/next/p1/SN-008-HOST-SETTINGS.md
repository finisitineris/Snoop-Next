# SN-008 — Snoop host settings integration (stacked on PR #14)

This branch is based on [PR #14](https://github.com/finisitineris/Snoop-Next/pull/14),
the RESX library. It is a **host-only** language preference integration.
It does **not** reference the library from Snoop.Core or InjectorLauncher.

## User interaction semantics

- On startup, Snoop's App.OnStartup sets LocalizationManager.Default from
  Settings.Default.LanguageTag on the **host UI dispatcher**.
- SettingsView offers three preferences: zh-CN, en-US, system. The selector
  is intentionally non-previewing: choosing a value alone has no effect.
- Save & close commits the preference to SnoopSettings.xml, then applies the
  new language to Snoop-host-owned UI bindings.
- Discard changes reloads settings from disk and returns the selector to the
  persisted value without applying an uncommitted language.
- Reset all settings resets the other in-memory settings immediately, but
  preserves the last committed `LanguageTag` in `Settings.Default`. The picker
  moves to the default `zh-CN` as a pending language choice. Save commits and
  applies it; Discard reloads the saved settings and restores the picker.
  Closing the settings view without Discard retains the existing Snoop behavior
  for other in-memory settings; this PR only guarantees the language choice
  itself is not committed until Save.
- Null, legacy, or malformed LanguageTag XML values normalize to zh-CN, with
  case-insensitive canonicalization of supported language tags.
- This integration localizes initial SettingsView headers, tooltips and
  Save/Discard/Reset labels. WPF dependency-property names, bindings and
  arbitrary inspectee strings are never translated.

## Verification

The path-scoped next-localization Windows workflow:
1. Compiles the library for net462 and net10.0-windows.
2. Compiles the independent WPF language demo.
3. Compiles Snoop host and executes a **non-injecting** CLR4 XML settings
   smoke, verifying save/reload/reset and old/invalid XML fallback.
4. Confirms the **project references** include localization only from
   the host Snoop project, not Snoop.Core or InjectorLauncher.
5. Runs existing WPF STA Dispatcher / RESX NUnit tests.

The normal continuous workflow independently builds/tests/packages Snoop.

## Gates not yet cleared

- Manual interactive Windows screenshots / keyboard/DPI testing for the
  host's SettingsView and resulting AppChooser language.
- Packaging audit ensuring no extra localization DLL gets copied into the
  injected probe's assembly directory. Source graph checks do not prove the
  runtime loader's final closure.
- Real attached WPF process behavior and multi-AppDomain isolation, SN-004.
- Broader translations and shell/window resource localization beyond the
  initial host Settings view.

No GitHub-hosted step injects or launches arbitrary WPF inspectee processes.


## Packaging correction

The upstream Chocolatey nuspec and WiX MSI define explicit file lists,
not a wildcard for every dependency of the host. Both packaging manifests
now include the root-level Snoop.Next.Localization.dll alongside Snoop.exe
plus the zh-CN/Snoop.Next.Localization.resources.dll satellite assembly.
Neither injected Snoop.Core net462 nor net6 runtime directory is modified.

The targeted CI checks the build output paths and manifest references.
The full Nuke CI must additionally produce Chocolatey ZIP/nupkg and MSI.
This proves expected package inclusion, but a separate installation and
real process attach smoke is required to certify the runtime loader.


## SettingsView interaction regression (latest CLR4 smoke)

The host smoke constructs the actual SettingsView on an STA thread and raises
the real WPF Button Click routed events against a temporary XML settings file.
It verifies:

- Selecting en-US is pending: persisted setting and live LocalizationManager
  remain zh-CN.
- Discard restores the persisted selection.
- Save writes en-US and applies it to the host LocalizationManager.
- Reset changes the picker to zh-CN but preserves the last committed
  LanguageTag and live manager until Save.
- Save after Reset persists/applies zh-CN.

The smoke restores the original Settings.Default path/value and manager
language in a finally block. It does not display the window or inject into
another process.
