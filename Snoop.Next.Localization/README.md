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


## Real WPF dispatcher tests (not process injection)

The WpfLocalizationBindingTests fixture uses actual STA WPF objects, XamlReader.Parse,
and a pumped Dispatcher to verify live RESX updates. It covers TextBlock.Text,
MenuItem.Header and a TextBlock created by DataTemplate.LoadContent().

Style Setter has a supported fallback: set the attached property
loc:LocText.Key to a static semantic key on TextBlock. The behavior binds
TextBlock.Text to the selected resource and clears its binding when the style
is removed. Example:

    <Style TargetType="{x:Type TextBlock}">
      <Setter Property="loc:LocText.Key" Value="Tree.Title" />
    </Style>

These tests do not attach to an existing process, exercise multi-dispatcher
windows, or establish dependency safety inside an inspected WPF application.
