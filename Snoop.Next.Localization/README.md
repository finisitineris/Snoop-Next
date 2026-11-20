# Snoop Next localization core (P1 / SN-007)

The minimal **WPF RESX + live indexer Binding** foundation is isolated in its own library. It targets net462 and net10.0-windows. **It is not yet referenced by Snoop.Core**, so the existing injector, Snoop UI and upstream compatibility remain unchanged.

- English fallback: Strings.resx
- Chinese: Strings.zh-CN.resx
- LocalizationManager: explicit UI culture only, \`INotifyPropertyChanged("Item[]")\`, missing-key diagnostic, optional "system" mode
- LocExtension: \`{local:Loc Shell.Title}\` resolves to a WPF Binding on the manager's indexer
- No calls to set \`Thread.CurrentUICulture\`, \`DefaultThreadCurrentUICulture\` or inspectee \`Application.Resources\`

## Scope of current CI

Windows builds net462 and net10.0-windows. Isolated unit tests run on net10.0-windows for resource parity, value switching, formatted messages, missing keys, thread Culture isolation, and Binding shape.

**Not yet verified:** actual WPF dispatcher/UI hot-rebind, Style Setter, DataTemplate unload, Freezable, DPI/accessibility, multi-dispatcher behavior, real Snoop injection, or net11 assembly behavior. Those need separate harness tests before linking this library to the injected core.

## XAML usage later (after UI harness validation)

\`\`\`xml
xmlns:local="clr-namespace:Snoop.Next.Localization;assembly=Snoop.Next.Localization"
Title="{local:Loc Shell.Title}"
\`\`\`

All user-facing text belongs in RESX, not in color/theme resources. The inspected app's original dependency-property names and binding paths must **not** be localized.
