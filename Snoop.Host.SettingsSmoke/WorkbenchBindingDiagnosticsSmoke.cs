namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using Snoop.Infrastructure.Diagnostics.Providers;
using Snoop.Localization;
using Snoop.Views;
using Snoop.Windows;
using CoreSettings = Snoop.Core.Settings;

internal static class WorkbenchBindingDiagnosticsSmoke
{
    public static void Verify()
    {
        var settings = CoreSettings.Default;
        var originalFile = settings.SettingsFile;
        var fixtureFile = Path.Combine(Path.GetDirectoryName(originalFile)!, "WorkbenchBindingDiagnosticsSmoke.xml");
        var originalPlacement = settings.SnoopUIWindowPlacement;
        var originalTreeFraction = settings.WorkbenchTreeFraction;
        var originalDiagnosticsFraction = settings.WorkbenchDiagnosticsFraction;
        var originalShowDefaults = settings.ShowDefaults;
        var originalShowPreviewer = settings.ShowPreviewer;
        var originalTraceLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        settings.SettingsFile = fixtureFile;
        try
        {
            var inspector = new SnoopUI();
            // This hidden fixture window does not run OnSourceInitialized.
            CacheManager.Instance.IncreaseUsageCount();
            try
            {
                VerifyInspector(inspector);
            }
            finally
            {
                inspector.Close();
            }
        }
        finally
        {
            settings.SettingsFile = originalFile;
            settings.SnoopUIWindowPlacement = originalPlacement;
            settings.WorkbenchTreeFraction = originalTreeFraction;
            settings.WorkbenchDiagnosticsFraction = originalDiagnosticsFraction;
            settings.ShowDefaults = originalShowDefaults;
            settings.ShowPreviewer = originalShowPreviewer;
            PresentationTraceSources.DataBindingSource.Switch.Level = originalTraceLevel;
            InspectorLocalization.Default.SetLanguage(originalLanguage);
            File.Delete(fixtureFile);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: local binding failures identify the target property without replacing its binding");
    }

    private static void VerifyInspector(SnoopUI inspector)
    {
        var context = inspector.TreeService.DiagnosticContext;
        var provider = context.DiagnosticProviders.SingleOrDefault(candidate => candidate.Name == "Local binding failures")
            ?? throw new InvalidOperationException("Missing the local binding failure provider.");
        var scope = new StackPanel();
        var nameInput = new TextBox { Name = "NameInput" };
        var healthy = new TextBlock { Name = "HealthyName" };
        var waiting = new TextBlock { Name = "WaitingName" };
        var source = new { DisplayText = "Available" };
        var pendingSource = new PendingBindingSource();
        scope.Children.Add(nameInput);
        scope.Children.Add(healthy);
        scope.Children.Add(waiting);

        var brokenBinding = BindingOperations.SetBinding(nameInput, FrameworkElement.ToolTipProperty, new Binding("MissingToolTip")
        {
            Source = source,
            FallbackValue = "Fallback",
        });
        var goodBinding = BindingOperations.SetBinding(healthy, TextBlock.TextProperty, new Binding(nameof(source.DisplayText)) { Source = source });
        var pendingBinding = BindingOperations.SetBinding(waiting, TextBlock.TextProperty, new Binding("Result.DisplayText")
        {
            Source = pendingSource,
            IsAsync = true,
        });

        try
        {
            DrainDispatcher();
            Require(brokenBinding.Status == BindingStatus.PathError && goodBinding.Status == BindingStatus.Active,
                $"The fixture requires a real path error and a healthy binding. Got {brokenBinding.Status} and {goodBinding.Status}.");
            Require(pendingBinding.Status == BindingStatus.AsyncRequestPending,
                $"The fixture requires an incomplete asynchronous binding. Got {pendingBinding.Status}.");

            inspector.ScopeTo(scope);
            var traceSource = PresentationTraceSources.DataBindingSource;
            var traceLevel = traceSource.Switch.Level;
            var traceListeners = traceSource.Listeners.Cast<TraceListener>().ToArray();
            SnoopUI.RefreshCommand.Execute(null, inspector);
            DrainDispatcher();

            var item = context.DiagnosticItems.Single(diagnostic => ReferenceEquals(diagnostic.DiagnosticProvider, provider));
            var treeItem = inspector.RootTreeItem?.FindNode(nameInput)
                ?? throw new InvalidOperationException("Missing NameInput in the scoped tree.");
            Require(ReferenceEquals(item.TreeItem, treeItem) && ReferenceEquals(item.SourceObject, nameInput)
                    && ReferenceEquals(item.TargetProperty, FrameworkElement.ToolTipProperty)
                    && item.Level == DiagnosticLevel.Error && item.Area == DiagnosticArea.Binding,
                "The binding diagnostic must identify the exact failing element and dependency property.");

            var view = inspector.FindName("WorkbenchDiagnosticsView") as DiagnosticsView
                ?? throw new InvalidOperationException("Missing the workbench diagnostics view.");
            var list = view.FindName("diagnostics") as ListView
                ?? throw new InvalidOperationException("Missing the workbench diagnostics list.");
            Require(list.Items.Contains(item), "The binding error must appear in the workbench diagnostics list.");
            VerifyTechnicalDescription(item.Description);
            VerifyLocalizedDiagnostic(item, "en-US", "Binding error");
            VerifyLocalizedDiagnostic(item, "zh-CN", "绑定错误");

            var locate = SnoopUI.LocateDiagnosticElementCommand;
            Require(locate.CanExecute(item, inspector), "The binding diagnostic must be navigable.");
            locate.Execute(item, inspector);
            DrainDispatcher();
            Require(ReferenceEquals(inspector.CurrentSelection, treeItem) && ReferenceEquals(inspector.ScopedRootObject, scope),
                "Locating the binding diagnostic must select NameInput within the existing scope.");
            Require(ReferenceEquals(BindingOperations.GetBindingExpressionBase(nameInput, FrameworkElement.ToolTipProperty), brokenBinding)
                    && brokenBinding.Status == BindingStatus.PathError,
                "Diagnosing and locating an error must preserve its original binding expression.");
            Require(traceSource.Switch.Level == traceLevel && traceSource.Listeners.Cast<TraceListener>().SequenceEqual(traceListeners),
                "Diagnosing and locating an error must preserve the application's trace configuration.");

            var selectionDiagnostic = new DiagnosticItem(MissingAdornerLayerDiagnosticProvider.Instance);
            context.Add(selectionDiagnostic);
            context.AnalyzeTree();
            DrainDispatcher();
            var replacement = context.DiagnosticItems.Single(diagnostic => ReferenceEquals(diagnostic.DiagnosticProvider, provider));
            Require(!ReferenceEquals(item, replacement) && !locate.CanExecute(item, inspector)
                    && context.DiagnosticItems.Contains(selectionDiagnostic),
                "Analyzing the same tree again must replace provider rows and preserve selection diagnostics.");

            var repairedBinding = BindingOperations.SetBinding(nameInput, FrameworkElement.ToolTipProperty,
                new Binding(nameof(source.DisplayText)) { Source = source });
            DrainDispatcher();
            Require(repairedBinding.Status == BindingStatus.Active, "The repaired binding must be active before refresh.");
            SnoopUI.RefreshCommand.Execute(null, inspector);
            DrainDispatcher();
            Require(!context.DiagnosticItems.Any(diagnostic => ReferenceEquals(diagnostic.DiagnosticProvider, provider))
                    && !locate.CanExecute(replacement, inspector),
                "Refreshing a repair must remove the prior diagnostic, and its old row must no longer be navigable.");
        }
        finally
        {
            pendingSource.Release();
        }
    }

    private static void VerifyLocalizedDiagnostic(DiagnosticItem item, string language, string name)
    {
        InspectorLocalization.Default.SetLanguage(language);
        var converter = DiagnosticLabelConverter.Default;
        var caption = converter.Convert(new object[] { item, language }, typeof(string), nameof(DiagnosticItem.Name), CultureInfo.InvariantCulture);
        var description = converter.Convert(new object[] { item, language }, typeof(string), nameof(DiagnosticItem.Description), CultureInfo.InvariantCulture);
        Require(Equals(caption, name), $"The diagnostic caption for {language} must be '{name}'.");
        VerifyTechnicalDescription((string)description);
    }

    private static void VerifyTechnicalDescription(string description)
    {
        Require(description.IndexOf("NameInput", StringComparison.Ordinal) >= 0
                && description.IndexOf("ToolTip", StringComparison.Ordinal) >= 0
                && description.IndexOf("PathError", StringComparison.Ordinal) >= 0
                && description.IndexOf("MissingToolTip", StringComparison.Ordinal) >= 0,
            $"The description must identify the target, property, binding path and WPF failure status. Got '{description}'.");
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class PendingBindingSource
    {
        private readonly TaskCompletionSource<bool> release = new();

        public object Result
        {
            get
            {
                this.release.Task.Wait(TimeSpan.FromSeconds(30));
                return new { DisplayText = "Available" };
            }
        }

        public void Release()
        {
            this.release.TrySetResult(true);
        }
    }
}
