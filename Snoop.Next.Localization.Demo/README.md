# Isolated WPF localization demonstration (SN-007)

This sample is a real native WPF window, not the injected Snoop inspector.

On an interactive Windows desktop, run:

    dotnet run --project Snoop.Next.Localization.Demo/Snoop.Next.Localization.Demo.csproj -c Release

Change the ComboBox between zh-CN and en-US. The menu, TextBlock, Style
Setter, DataTemplate and Window title should update without restarting.
The thread UI culture readout should remain unchanged.

GitHub CI compiles this project but never starts the interactive window;
the separate STA NUnit fixture executes actual WPF binding updates.
