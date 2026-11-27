using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Nuke.Common;
using Nuke.Common.ProjectModel;

internal static class ParserProbe
{
    private const int ManifestMismatch = unchecked((int)0x80131040);
    private const string ExpectedIdentity =
        "NuGet.Frameworks, Version=7.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";

    public static int Main()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Run()
    {
        if (Environment.GetEnvironmentVariable("NUKE_TELEMETRY_OPTOUT") != "1"
            || Environment.GetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT") != "1")
        {
            throw new InvalidOperationException("Set both NUKE_TELEMETRY_OPTOUT and DOTNET_CLI_TELEMETRY_OPTOUT to 1.");
        }

        string expected = Path.GetFullPath(Path.Combine(".build", "_build.csproj"));
        string project = NukeBuild.BuildProjectFile
            ?? throw new InvalidOperationException("NUKE did not locate the bootstrapper project.");
        if (!string.Equals(expected, project, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected {expected}, found {project}.");

        Console.WriteLine($"Project: {project}");
        Console.WriteLine($"Bootstrapper NuGet.Frameworks: {AssemblyName.GetAssemblyName(Path.Combine(AppContext.BaseDirectory, "NuGet.Frameworks.dll"))}");
        RegisterProjectModel();
        bool matchingLoadException = false;
        void ObserveLoad(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is FileLoadException fileLoad && fileLoad.HResult == ManifestMismatch
                && string.Equals(fileLoad.FileName, ExpectedIdentity, StringComparison.OrdinalIgnoreCase))
                matchingLoadException = true;
        }

        AppDomain.CurrentDomain.FirstChanceException += ObserveLoad;
        try
        {
            string? version = ReadAwarenessVersion(project);
            if (version != "1")
                throw new InvalidDataException($"Unexpected NukeTelemetryVersion value '{version}'.");
            Console.WriteLine("SN006_RESULT=PARSED (NukeTelemetryVersion=1)");
            return 0;
        }
        catch (Exception ex) when (matchingLoadException
                                   && ex.ToString().Contains(ExpectedIdentity, StringComparison.OrdinalIgnoreCase)
                                   && ex.ToString().Contains("ProjectModelTasks.ParseProject", StringComparison.Ordinal))
        {
            Console.WriteLine($"SN006_RESULT=KNOWN_MANIFEST_MISMATCH (0x{ManifestMismatch:X8}, {ExpectedIdentity})");
            Console.WriteLine(ex);
            return 0;
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= ObserveLoad;
        }
    }

    // NUKE resolves its build file and host before invoking telemetry build-initialization events.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterProjectModel()
    {
        ProjectModelTasks.Initialize();
        Console.WriteLine($"Nuke.ProjectModel: {typeof(ProjectModelTasks).Assembly.Location}");
    }

    // Keep MSBuild types out of Main's JIT scope until the NUKE locator has registered the SDK.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? ReadAwarenessVersion(string projectFile)
    {
        var project = ProjectModelTasks.ParseProject(projectFile);
        return project.Properties.SingleOrDefault(p =>
            string.Equals(p.Name, "NukeTelemetryVersion", StringComparison.OrdinalIgnoreCase))?.EvaluatedValue;
    }
}
