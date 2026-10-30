using System.Text;
using System.Xml.Linq;
using gflat.Projects;

namespace gflat;

/// <summary>Creates the same MSBuild project consumed by the CLI and Visual Studio.</summary>
public static class ProjectCreation
{
    private const string Usage = "gflat new <directory> [--name Name] [--library] [--no-stdlib]";
    public static int Run(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            Console.WriteLine("Create a project in a new or empty directory. --library creates a source library; --no-stdlib omits the Standard reference.");
            return 0;
        }
        string? directory = null, name = null;
        bool library = false, useStd = true;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name":
                    name = ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for --name.");
                    break;
                case "--library": library = true; break;
                case "--no-stdlib": useStd = false; break;
                default:
                    if (args[i].StartsWith('-') || directory != null)
                        throw new ArgumentException($"Unexpected project creation argument '{args[i]}'.");
                    directory = args[i]; break;
            }
        }
        if (directory == null) throw new ArgumentException("Use " + Usage + ".");
        string project = Create(directory, name, library, useStd);
        Console.WriteLine($"Created {project}");
        Console.WriteLine(library ? $"Check: gflat check \"{project}\"" : $"Run: gflat build \"{project}\" --run");
        return 0;
    }

    public static string Create(string directory, string? name = null, bool library = false,
        bool useStd = true, string? toolchainRoot = null)
    {
        directory = Path.GetFullPath(directory);
        name ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') ||
            name.IndexOfAny([';', '$', '%', '*', '?', ':', '<', '>', '"', '|']) >= 0 ||
            name.Any(char.IsControl) || name.EndsWith('.') || name.EndsWith(' ') || IsReservedName(name))
            throw new ArgumentException("Project name must be a filename without MSBuild expressions or wildcards.");
        if (File.Exists(directory)) throw new ArgumentException("The project directory is an existing file.");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new ArgumentException("Project creation requires a new or empty directory; existing files will not be overwritten.");
        bool explicitRoot = toolchainRoot != null;
        toolchainRoot = toolchainRoot == null ? FindToolchainRoot() : Path.GetFullPath(toolchainRoot);
        if (!ProjectSdk.IsRoot(toolchainRoot))
            throw new ArgumentException("Cannot locate gflat build rules: expected build/Gflat.props and build/Gflat.targets.");
        var roots = new XElement("PropertyGroup");
        if (!explicitRoot)
            roots.Add(new XElement("GflatSdkRoot", new XAttribute("Condition", "'$(GflatSdkRoot)' == '' and '$(GflatDefaultSdkRoot)' != ''"), "$(GflatDefaultSdkRoot)"));
        roots.Add(new XElement("GflatSdkRoot", new XAttribute("Condition", "'$(GflatSdkRoot)' == ''"),
            ProjectSdk.EscapePropertyValue(Path.GetRelativePath(directory, toolchainRoot).Replace('\\', '/'))));
        var properties = new XElement("PropertyGroup", new XElement("OutputType", library ? "SourceLibrary" : "Executable"));
        if (!useStd) properties.Add(new XElement("NoStdLib", "true"));
        var xml = new XElement("Project", new XAttribute("DefaultTargets", "Build"), roots,
            new XElement("Import", new XAttribute("Project", "$(GflatSdkRoot)/build/Gflat.props")), properties,
            new XElement("Import", new XAttribute("Project", "$(GflatSdkRoot)/build/Gflat.targets")));
        string source = library ? "// Add reusable types and functions here.\nnamespace Library;\n\npublic int Answer() => 42;\n"
            : useStd ? "int main()\n{\n    Console::WriteLine(\"Hello from gflat!\");\n    return 0;\n}\n"
            : "int main() => 0;\n";
        Directory.CreateDirectory(directory);
        string project = Path.Combine(directory, name + ".gfproj");
        // CreateNew protects against another process creating these files after validation.
        WriteNew(Path.Combine(directory, "main.gf"), source);
        WriteNew(project, xml + Environment.NewLine);
        WriteNew(Path.Combine(directory, ".gitignore"), "bin/\nobj/\n.vs/\n*.gfproj.user\n");
        return project;
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static bool IsReservedName(string name)
    {
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9';
    }

    public static string FindToolchainRoot()
    {
        if (ProjectSdk.FindRoot(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("GflatSdkRoot")) is string root) return root;
        throw new ArgumentException("Cannot locate gflat build rules. Keep build/ alongside the compiler distribution.");
    }
}
