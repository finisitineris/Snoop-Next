# SN-009 — Host-to-injected Snoop language transport

This PR is stacked on [SN-008 PR #16](https://github.com/finisitineris/Snoop-Next/pull/16).
It establishes only the **transport contract**; it does not localize the injected
SnoopUI or load the localization assembly into the inspected process.

## Existing transport reused

- Host \`Snoop/ProcessInfo.CreateTransientSettingsData()\` creates the payload.
- \`TransientSettingsData.WriteToFile()\` serializes it to the temporary XML file.
- \`Snoop.InjectorLauncher\` passes that path to the generic injector.
- \`SnoopManager.StartSnoop(settingsFile)\` deserializes it before creating Snoop UI.
- Multiple-AppDomain injection reuses the same settings file via
  \`Snoop.SettingsFile\`.

## Contract change

- Add \`LanguageTag\` to \`TransientSettingsData\`.
- Host copies its committed \`Settings.Default.LanguageTag\` into the transient
  payload.
- Default is \`system\`, so old transient XML files without this field remain
  compatible and old external callers retain system-language behavior.
- No target thread Culture, CurrentUICulture or Application.Resources is
  modified by this transport-only change.

## Tests

CLR4 host smoke serializes an en-US transient payload, deserializes it,
and deserializes a legacy payload with no LanguageTag to verify the default
is \`system\`. The existing localization/SettingsView tests continue to run.

## Not included / not claimed

- No reference to \`Snoop.Next.Localization\` from Snoop.Core yet.
- No injected UI translations, assembly resolver changes or package changes.
- No actual target-process injection is run by CI.
- SN-009's next step is to consume the transported LanguageTag inside
  Snoop.Core and perform controlled attach/inspect/detach tests for net462
  and net10, including AppDomain and target Culture/Resources isolation.
