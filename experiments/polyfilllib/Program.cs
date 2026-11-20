namespace SnoopNext.PolyfillCompat;

using System;
using System.IO;
using System.Linq;
using System.Reflection;

internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("SN-003 PolyfillLib compiled-library load smoke");
        Console.WriteLine("Process CLR version: " + Environment.Version);
        Console.WriteLine("Architecture: " + (Environment.Is64BitProcess ? "x64" : "x86"));
        Console.WriteLine("Base directory: " + AppDomain.CurrentDomain.BaseDirectory);

        var files = Directory.GetFiles(
            AppDomain.CurrentDomain.BaseDirectory, "PolyfillLib.dll", SearchOption.TopDirectoryOnly);
        Console.WriteLine("PolyfillLib runtime DLL copies: " + files.Length);

        // This pinned PolyfillLib DLL-mode package deploys a binary on both
        // tested TFMs. Do not mistake the test app for the library itself.
        if (files.Length == 0)
        {
            Console.Error.WriteLine("FAIL: pinned PolyfillLib package must deploy its compiled runtime assembly.");
            return 2;
        }

        try
        {
            foreach (var file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine("Loading: " + Path.GetFileName(file));
                var assembly = Assembly.LoadFrom(file);
                // Force discovery of referenced metadata and dependent assemblies.
                var types = assembly.GetTypes();
                Console.WriteLine("Loaded " + assembly.GetName().FullName + "; types=" + types.Length);
            }

            // Record the actual output DLL closure; it must be evaluated before
            // deploying this dependency into any process being inspected.
            foreach (var file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll")
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine("RUNTIME_ASSET " + Path.GetFileName(file) +
                                  " " + new FileInfo(file).Length + " bytes");
            }

            Console.WriteLine("PASS: local runtime load and metadata traversal");
            return 0;
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.Error.WriteLine("FAIL: ReflectionTypeLoadException: " + ex);
            foreach (var loader in ex.LoaderExceptions.Where(x => x != null))
            {
                Console.Error.WriteLine("LOADER: " + loader);
            }
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 4;
        }
    }
}
