#if DEBUG
using System.Diagnostics;
using System.Reflection;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.PlugIns;

namespace RhinoLayoutFoundry.Rhino;

/// <summary>Local development diagnostics; deliberately absent from Release builds.</summary>
[CommandStyle(Style.ScriptRunner)]
public sealed class LayoutFoundryDevCommand : Command
{
    private static readonly Guid[] PluginIds =
    [
        new("619638d4-b9f2-47b1-a081-c7447a2aa26b"),
        new("a4217268-64b2-40ce-aef0-2a770ac94b3d")
    ];

    public override string EnglishName => "LayoutFoundryDev";

    protected override Result RunCommand(RhinoDoc document, RunMode mode)
    {
        try
        {
            PrintDiagnostics();
            using var options = new GetOption();
            options.SetCommandPrompt("Layout Foundry development tools (Enter to finish)");
            options.AcceptNothing(true);
            var layoutFolder = options.AddOption("LayoutFolder");
            var aiFolder = options.AddOption("AIFolder");
            var packages = options.AddOption("PackageManager");
            var plugins = options.AddOption("PlugInManager");
            var instructions = options.AddOption("InstallTesting");
            var result = options.Get();
            if (result == GetResult.Nothing)
                return Result.Success;
            if (result != GetResult.Option)
                return options.CommandResult();
            var selected = options.OptionIndex();
            if (selected == layoutFolder)
                return OpenPluginFolder(PluginIds[0]);
            if (selected == aiFolder)
                return OpenPluginFolder(PluginIds[1]);
            if (selected == packages || selected == plugins)
                return RhinoApp.RunScript(selected == packages ? "_PackageManager" : "_PlugInManager", false)
                    ? Result.Success : Result.Failure;
            if (selected == instructions)
                PrintInstallTesting();
            return Result.Success;
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("LayoutFoundryDev: {0}", exception.Message);
            return Result.Failure;
        }
    }

    private static bool IsFoundry(Assembly assembly) =>
        assembly.GetName().Name?.StartsWith("RhinoLayoutFoundry", StringComparison.Ordinal) == true ||
        assembly.GetName().Name?.StartsWith("RhinoFoundry.UI", StringComparison.Ordinal) == true;

    private static void PrintDiagnostics()
    {
        RhinoApp.WriteLine("LayoutFoundryDev — Debug tools; diagnostics do not change documents or installations.");
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && IsFoundry(a)).OrderBy(a => a.GetName().Name).ToArray();
        foreach (var assembly in loaded)
        {
            var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";
            RhinoApp.WriteLine($"Loaded: {assembly.GetName().Name} {assembly.GetName().Version} ({configuration}) | {assembly.Location}");
        }
        foreach (var group in loaded.GroupBy(a => a.GetName().Name).Where(g => g.Count() > 1))
            RhinoApp.WriteLine("WARNING: multiple loaded assemblies named {0}.", group.Key);

        var paths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var id in PluginIds)
        {
            var info = PlugIn.GetPlugInInfo(id);
            RhinoApp.WriteLine("Registered: {0} | {1} | {2}", info?.Name ?? id.ToString(),
                info?.IsLoaded == true ? "loaded" : "not loaded", info?.FileName ?? "not registered");
            if (info is not null && File.Exists(info.FileName))
                paths.Add(Path.GetFullPath(info.FileName));
        }
        foreach (var assembly in loaded.Where(a => a.Location.EndsWith(".rhp", StringComparison.OrdinalIgnoreCase)))
            paths.Add(Path.GetFullPath(assembly.Location));

        var userRoot = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "McNeel", "Rhinoceros")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McNeel", "Rhinoceros");
        var roots = new List<string>
        {
            Path.Combine(userRoot, "MacPlugIns"),
            Path.Combine(userRoot, "8.0", "MacPlugIns"),
            Path.Combine(userRoot, "8.0", "Plug-ins"),
            Path.Combine(userRoot, "packages", "8.0")
        };
        var custom = Environment.GetEnvironmentVariable("RHINO_PACKAGE_DIRS");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            RhinoApp.WriteLine("RHINO_PACKAGE_DIRS: {0}", custom);
            roots.AddRange(custom.Split(new[] { ';', Path.PathSeparator },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        foreach (var root in roots.Distinct())
            Scan(root, paths);
        foreach (var path in paths.Order())
            RhinoApp.WriteLine("Installation candidate: {0}", path);
        foreach (var group in paths.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            RhinoApp.WriteLine("WARNING: multiple copies of {0}; inspect paths above before package testing.", group.Key);
        RhinoApp.WriteLine("Scan scope: standard Rhino 8 user folders and current RHINO_PACKAGE_DIRS, depth 4, no symlink traversal. " +
            "Candidates are not proof of active loading; other accounts, system folders and debugger paths may contain more copies.");
        RhinoApp.WriteLine("Use InstallTesting for the park/install/uninstall/restore workflow.");
    }

    private static void Scan(string root, HashSet<string> paths)
    {
        if (!Directory.Exists(root))
            return;
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                RhinoApp.WriteLine("Scan skipped linked directory: {0}", root);
                return;
            }
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 4,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false
            };
            foreach (var file in Directory.EnumerateFiles(root, "*.rhp", options))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("RhinoLayoutFoundry.rhp", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("RhinoLayoutFoundry.AI.Rhino.rhp", StringComparison.OrdinalIgnoreCase))
                    paths.Add(Path.GetFullPath(file));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RhinoApp.WriteLine("Incomplete scan of {0}: {1}", root, exception.Message);
        }
    }

    private static Result OpenPluginFolder(Guid id)
    {
        var assembly = id == PluginIds[0] ? typeof(LayoutFoundryDevCommand).Assembly
            : AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RhinoLayoutFoundry.AI.Rhino");
        var file = assembly?.Location ?? PlugIn.GetPlugInInfo(id)?.FileName;
        var directory = string.IsNullOrEmpty(file) ? null : Path.GetDirectoryName(file);
        if (directory is null || !Directory.Exists(directory))
        {
            RhinoApp.WriteLine("No existing installation folder found for this plugin.");
            return Result.Nothing;
        }
        if (OperatingSystem.IsMacOS())
        {
            // A development directory can itself end in .rhp. Reveal its inner
            // assembly in Finder instead of invoking that bundle's file association.
            var reveal = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            reveal.ArgumentList.Add("-R");
            reveal.ArgumentList.Add(file!);
            Process.Start(reveal);
        }
        else
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        return Result.Success;
    }

    private static void PrintInstallTesting()
    {
        RhinoApp.WriteLine("For first-install qualification use a separate OS user account with no developer load paths.");
        RhinoApp.WriteLine("Quit Rhino before changing bundles. In the Layout repository on macOS run:");
        RhinoApp.WriteLine("  python3 scripts/dev-install-macos.py park --apply");
        RhinoApp.WriteLine("Install the combined local Yak; restart and check loaded paths. Uninstall through PackageManager, " +
            "restart and confirm both plugins are absent; then reinstall and restart.");
        RhinoApp.WriteLine("To resume development: uninstall the test Yak, quit Rhino, then run:");
        RhinoApp.WriteLine("  python3 scripts/dev-install-macos.py restore --apply");
        RhinoApp.WriteLine("On Windows, remove developer search paths in the test profile and follow docs/WINDOWS_BETA_TESTING.md.");
        RhinoApp.WriteLine("Full checklist: docs/DEVELOPER_INSTALL_TESTING.md. User documents, credentials and conversations are preserved. " +
            "This command does not uninstall, rebuild or publish anything.");
    }
}
#endif
