using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;

namespace gflat.Projects;

// Shared by the successful-build task and the IDE's pre-build check. It
// evaluates the source-library graph and reads file metadata without building.
public sealed class BuildUpToDateState
{
    private static readonly StringComparer Paths = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly object FailureGate = new object();
    private static readonly Dictionary<string, DateTime> Failures = new Dictionary<string, DateTime>(Paths);
    private readonly string fingerprint;
    private readonly Dictionary<string, string> globals;
    private readonly Dictionary<string, string> rootValues;
    private readonly string rootXml;
    private readonly bool cacheable;

    private BuildUpToDateState(string fingerprint, Dictionary<string, string> globals, Dictionary<string, string> rootValues, string rootXml, bool cacheable)
    {
        this.fingerprint = fingerprint;
        this.globals = globals;
        this.rootValues = rootValues;
        this.rootXml = rootXml;
        this.cacheable = cacheable;
    }

    public static BuildUpToDateState Capture(string projectFile, IDictionary<string, string> globalProperties,
        DateTime? buildStartedUtc = null, CancellationToken cancellationToken = default)
    {
        var properties = new Dictionary<string, string>(globalProperties, StringComparer.OrdinalIgnoreCase);
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(Paths);
        var inputs = new HashSet<string>(Paths);
        var requiredInputs = new HashSet<string>(Paths);
        var outputs = new HashSet<string>(Paths);
        var state = new StringBuilder();
        string xml = "";
        bool canCache = true;
        bool nativeOutput = false;
        string configuration = "", platform = "";
        string Absolute(string value, string directory) => Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(directory, value));
        void Value(string key, string value) => state.Append(key.Length).Append(':').Append(key).Append(value.Length).Append(':').Append(value).Append('\n');
        void Required(string path) { inputs.Add(path); requiredInputs.Add(path); }
        bool Flag(Project project, string name) => project.GetPropertyValue(name).Equals("true", StringComparison.OrdinalIgnoreCase);
        using (var collection = new ProjectCollection())
        {
            void Visit(string path, bool reference)
            {
                cancellationToken.ThrowIfCancellationRequested();
                path = Path.GetFullPath(path);
                if (!visited.Add(path)) return;
                var context = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
                if (reference)
                {
                    context["GflatAsReference"] = "true";
                    context["Configuration"] = configuration;
                    context["Platform"] = platform;
                }
                var project = new Project(path, context, null, collection);
                string directory = Path.GetDirectoryName(path)!;
                Value("Project", path);
                if (!reference)
                {
                    xml = project.Xml.RawXml;
                    nativeOutput = project.GetPropertyValue("OutputType").Equals("Executable", StringComparison.OrdinalIgnoreCase) && !Flag(project, "GflatEmitIR");
                    configuration = project.GetPropertyValue("Configuration");
                    platform = project.GetPropertyValue("Platform");
                    // Defaults stay local to their declaring project. Promoting
                    // a relative SDK root would change referenced imports.
                    foreach (string name in new[] { "GflatSdkRoot", "GflatCompilerPath", "GflatBundledCompiler" })
                        defaults[name] = project.GetPropertyValue(name);
                }
                Required(path);
                foreach (var import in project.Imports) Required(import.ImportedProject.FullPath);
                // Include the evaluated item lists to catch additions, removals,
                // reordered search paths, and newly active conditional imports.
                foreach (string name in new[]
                {
                    "OutputType", "Configuration", "Platform", "TargetPath", "GflatIROutput", "NoStdLib", "NoBuiltins",
                    "GflatExceptionReserveBytes", "GflatOptimizationLevel", "DebugSymbols", "GflatEmitIR", "GflatClang",
                    "GflatDefaultClangInput", "GflatCompilerPath", "GflatCompilerCommand", "GflatBundledCompiler", "GflatBuildTasksPath",
                    "GflatNativeLinkOverrides", "GflatPreprocessorOverrides", "GflatSdkRoot", "DisableFastUpToDateCheck", "_GflatKnownCompilerCommand"
                })
                    Value("Property:" + name, project.GetPropertyValue(name));
                foreach (string itemType in new[] { "Compile", "ProjectReference", "NativeLibrary", "NativeLibraryFile", "NativeLibraryDirectory", "IncludeDirectory", "Define", "GflatCompilerInputs", "UpToDateCheckInput", "UpToDateCheckOutput" })
                {
                    foreach (var item in project.GetItems(itemType))
                    {
                        Value("Item:" + itemType, item.EvaluatedInclude);
                        if (itemType == "Compile" || itemType == "GflatCompilerInputs" || itemType == "UpToDateCheckInput" || itemType == "NativeLibraryFile" && nativeOutput)
                            Required(item.GetMetadataValue("FullPath"));
                        if (itemType == "UpToDateCheckOutput") outputs.Add(item.GetMetadataValue("FullPath"));
                        if (itemType == "NativeLibrary" && nativeOutput) canCache = false;
                    }
                }
                if (!reference)
                {
                    string kind = project.GetPropertyValue("OutputType");
                    if (kind.Equals("Executable", StringComparison.OrdinalIgnoreCase))
                    {
                        bool ir = Flag(project, "GflatEmitIR");
                        string output = Absolute(project.GetPropertyValue(ir ? "GflatIROutput" : "TargetPath"), directory);
                        outputs.Add(output);
                        if (!ir && Path.DirectorySeparatorChar == '\\' && Flag(project, "DebugSymbols"))
                            outputs.Add(Path.ChangeExtension(output, ".pdb"));
                        if (!ir)
                        {
                            string? clang = ResolveClang(project.GetPropertyValue("GflatClang"), directory);
                            if (clang == null) canCache = false;
                            else Required(clang);
                        }
                    }
                    else if (!kind.Equals("SourceLibrary", StringComparison.OrdinalIgnoreCase)) canCache = false;
                    if (project.GetPropertyValue("DisableFastUpToDateCheck").Equals("true", StringComparison.OrdinalIgnoreCase)) canCache = false;
                    string compiler = project.GetPropertyValue("GflatCompilerPath");
                    string knownCommand = project.GetPropertyValue("_GflatKnownCompilerCommand");
                    if (compiler.Length == 0 || project.GetPropertyValue("GflatCompilerCommand") != knownCommand)
                    {
                        if (project.GetItems("GflatCompilerInputs").Count == 0) canCache = false;
                    }
                    if (compiler.Length != 0)
                    {
                        compiler = Absolute(compiler, directory);
                        Required(compiler);
                        string compilerDirectory = Path.GetDirectoryName(compiler)!;
                        foreach (string pattern in new[] { "*.dll", "*.json" })
                            if (Directory.Exists(compilerDirectory))
                                foreach (string file in Directory.EnumerateFiles(compilerDirectory, pattern)) Required(file);
                        foreach (string relative in new[] { "Runtime", "Standard" })
                        {
                            string folder = Path.Combine(compilerDirectory, relative);
                            if (!Directory.Exists(folder)) continue;
                            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                                if (relative == "Standard" ? file.EndsWith(".gf", StringComparison.OrdinalIgnoreCase) :
                                    file.EndsWith(".c", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)) Required(file);
                        }
                    }
                    foreach (string property in new[] { "GflatBuildTasksPath", "GflatNativeLinkOverrides", "GflatPreprocessorOverrides" })
                    {
                        string file = project.GetPropertyValue(property);
                        if (file.Length != 0) Required(Absolute(file, directory));
                        if (property == "GflatNativeLinkOverrides" && nativeOutput && file.Length != 0) canCache = false;
                    }
                    string dependencies = project.GetPropertyValue("_GflatIncludeDependencies");
                    if (File.Exists(dependencies))
                    {
                        // The preprocessor records missing candidates as well as
                        // resolved headers, so a newly shadowing header invalidates.
                        foreach (string file in File.ReadAllLines(dependencies))
                            if (file.Length != 0) inputs.Add(file);
                    }
                }
                foreach (var referenceItem in project.GetItems("ProjectReference"))
                {
                    // The MSBuild task accepts per-project evaluation overrides.
                    // Leave those builds to MSBuild until the snapshot supports them.
                    foreach (string metadata in new[] { "Properties", "AdditionalProperties", "GlobalPropertiesToRemove", "RemoveProperties", "ToolsVersion" })
                        if (referenceItem.GetMetadataValue(metadata).Length != 0) canCache = false;
                    Visit(referenceItem.GetMetadataValue("FullPath"), true);
                }
            }
            Visit(projectFile, false);
        }
        foreach (string name in new[] { "PATH", "LIB", "LIBPATH", "INCLUDE", "GFLAT_CLANG", "GFLAT_OPT_LEVEL", "CC", "CL", "_CL_", "LINK" })
            Value("Environment:" + name, Environment.GetEnvironmentVariable(name) ?? "");
        foreach (string path in inputs.OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            Value("Input", path);
            Value("Exists", file.Exists.ToString());
            if (!file.Exists) { if (requiredInputs.Contains(path)) canCache = false; continue; }
            Value("Stamp", file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            Value("Length", file.Length.ToString(CultureInfo.InvariantCulture));
            if (buildStartedUtc.HasValue && file.LastWriteTimeUtc > buildStartedUtc.Value) canCache = false;
        }
        foreach (string path in outputs.OrderBy(path => path, StringComparer.Ordinal))
        {
            var file = new FileInfo(path);
            Value("Output", path);
            if (!file.Exists) { canCache = false; Value("Exists", "false"); continue; }
            Value("Stamp", file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            Value("Length", file.Length.ToString(CultureInfo.InvariantCulture));
        }
        using (var hash = SHA256.Create())
            return new BuildUpToDateState(Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(state.ToString()))), properties, defaults, xml, canCache);
    }

    public void Write(string path)
    {
        if (!cacheable) return;
        var root = new XElement("GflatBuildState", new XAttribute("Version", "1"),
            new XElement("Fingerprint", fingerprint), new XElement("ProjectXml", Convert.ToBase64String(Encoding.UTF8.GetBytes(rootXml))),
            new XElement("Globals", globals.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                new XElement("Property", new XAttribute("Name", pair.Key), new XAttribute("Value", pair.Value)))));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, root.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
    }

    public static bool IsUpToDate(string path, string projectFile, string currentProjectXml, CancellationToken cancellationToken,
        IEnumerable<KeyValuePair<string, string>>? requiredGlobalProperties = null, TextWriter? logger = null)
    {
        try { return ReadAndCompare(path, projectFile, currentProjectXml, cancellationToken, requiredGlobalProperties, logger); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is FormatException || error is XmlException || error is Microsoft.Build.Exceptions.InvalidProjectFileException)
        { logger?.WriteLine("Up-to-date check could not read the build state: " + error.Message); return false; }
    }

    private static bool ReadAndCompare(string path, string projectFile, string currentProjectXml, CancellationToken cancellationToken,
        IEnumerable<KeyValuePair<string, string>>? requiredGlobalProperties, TextWriter? logger)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path)) { logger?.WriteLine("Build state does not exist."); return false; }
        if (File.GetLastWriteTimeUtc(path) <= LastFailure(projectFile))
        { logger?.WriteLine("The last build failed after this state was recorded."); return false; }
        XElement root;
        using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) root = XElement.Load(reader);
        if (root.Name != "GflatBuildState" || (string?)root.Attribute("Version") != "1" || root.Element("ProjectXml") == null || root.Element("Globals") == null) return false;
        if (Encoding.UTF8.GetString(Convert.FromBase64String((string)root.Element("ProjectXml")!)) != currentProjectXml) { logger?.WriteLine("Project contents have changed since the last successful build."); return false; }
        var entries = root.Element("Globals")!.Elements("Property").ToArray();
        if (entries.Any(property => property.Attribute("Name") == null || property.Attribute("Value") == null)) return false;
        var properties = entries.ToDictionary(property => (string)property.Attribute("Name")!, property => (string)property.Attribute("Value")!, StringComparer.OrdinalIgnoreCase);
        var deferredDefaults = new List<KeyValuePair<string, string>>();
        if (requiredGlobalProperties != null)
            foreach (var pair in requiredGlobalProperties)
            {
                if (!properties.TryGetValue(pair.Key, out string? value) && new[] { "GflatSdkRoot", "GflatCompilerPath", "GflatBundledCompiler" }.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    deferredDefaults.Add(pair);
                    continue;
                }
                if (value == null || ProjectCollection.Unescape(value) != pair.Value)
                {
                    logger?.WriteLine("Build property '" + pair.Key + "' has changed since the last successful build" + (value == null ? " (not recorded)." : "."));
                    if (new[] { "Configuration", "Platform", "GflatSdkRoot", "GflatCompilerPath", "GflatBundledCompiler", "DesignTimeBuild", "BuildingProject", "SkipCompilerExecution", "ProvideCommandLineArgs" }.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                        logger?.WriteLine("Recorded value: '" + (value == null ? "<missing>" : ProjectCollection.Unescape(value)) + "'. Current value: '" + pair.Value + "'.");
                    return false;
                }
            }
        var current = Capture(projectFile, properties, cancellationToken: cancellationToken);
        foreach (var pair in deferredDefaults)
            if (!current.rootValues.TryGetValue(pair.Key, out string? value) || value != pair.Value)
            { logger?.WriteLine("Project default '" + pair.Key + "' has changed since the last successful build."); return false; }
        bool matches = current.cacheable && current.fingerprint == (string?)root.Element("Fingerprint");
        if (!matches) logger?.WriteLine("Build inputs or outputs have changed since the last successful build.");
        return matches;
    }

    public static IReadOnlyList<string> RecordedGlobalPropertyNames(string path)
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            return XElement.Load(reader).Element("Globals")?.Elements("Property").Select(property => (string?)property.Attribute("Name") ?? "").ToArray() ?? Array.Empty<string>();
    }

    public static void RecordBuildFailure(string projectFile, DateTime timestampUtc)
    {
        projectFile = Path.GetFullPath(projectFile);
        lock (FailureGate)
        {
            DateTime latest = LastFailure(projectFile);
            if (latest > timestampUtc) timestampUtc = latest;
            Failures[projectFile] = timestampUtc;
            string marker = FailureMarker(projectFile);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, timestampUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            File.SetLastWriteTimeUtc(marker, timestampUtc);
        }
    }

    private static DateTime LastFailure(string projectFile)
    {
        projectFile = Path.GetFullPath(projectFile);
        lock (FailureGate)
        {
            Failures.TryGetValue(projectFile, out DateTime failure);
            string marker = FailureMarker(projectFile);
            DateTime written = File.Exists(marker) ? File.GetLastWriteTimeUtc(marker) : DateTime.MinValue;
            return failure > written ? failure : written;
        }
    }

    private static string FailureMarker(string projectFile) =>
        Path.Combine(Path.GetDirectoryName(projectFile)!, ".vs", "gflat", Path.GetFileName(projectFile) + ".build.failed");

    private static string? ResolveClang(string requested, string directory)
    {
        if (requested.Length == 0) requested = Environment.GetEnvironmentVariable("GFLAT_CLANG") ?? "";
        if (requested.Length != 0)
        {
            if (requested != Path.GetFileName(requested)) return Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(directory, requested));
            string name = Path.DirectorySeparatorChar == '\\' && Path.GetExtension(requested).Length == 0 ? requested + ".exe" : requested;
            string local = Path.Combine(directory, name);
            if (File.Exists(local)) return local;
            return OnPath(name);
        }
        string? found = OnPath(Path.DirectorySeparatorChar == '\\' ? "clang.exe" : "clang");
        if (found != null) return found;
        if (Path.DirectorySeparatorChar != '\\') return null;
        foreach (string programs in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct(Paths))
        {
            string root = Path.Combine(programs, "Microsoft Visual Studio");
            if (!Directory.Exists(root)) continue;
            foreach (string version in Directory.EnumerateDirectories(root))
                foreach (string edition in Directory.EnumerateDirectories(version))
                {
                    string candidate = Path.Combine(edition, "VC", "Tools", "Llvm", "x64", "bin", "clang.exe");
                    if (File.Exists(candidate)) return candidate;
                }
        }
        return null;
    }

    private static string? OnPath(string name)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }
}
