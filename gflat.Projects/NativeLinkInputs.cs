using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace gflat.Projects;

public sealed class NativeLibraryInput
{
    public string Kind { get; }
    public string Value { get; }

    public NativeLibraryInput(string kind, string value)
    {
        if (kind != "Name" && kind != "File") throw new ArgumentException("Native library kind must be Name or File.");
        NativeLinkInputs.ValidateText(value, "Native library");
        if (kind == "Name" && (value[0] == '-' || value.Any(c => !(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '.' && c != '+' && c != '-')))
            throw new ArgumentException("Native library names may contain only letters, digits, underscore, dot, plus, and hyphen, and cannot start with a hyphen. Use NativeLibraryFile for paths.");
        Kind = kind;
        Value = value;
    }
}

/// <summary>Ordered native-link requirements; paths are normalized at their declaring context.</summary>
public sealed class NativeLinkInputs
{
    private static readonly StringComparer Paths = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static NativeLinkInputs Empty { get; } = new NativeLinkInputs(Array.Empty<NativeLibraryInput>(), Array.Empty<string>());
    public IReadOnlyList<NativeLibraryInput> Libraries { get; }
    public IReadOnlyList<string> Directories { get; }
    public bool HasLogicalLibraries => Libraries.Any(library => library.Kind == "Name");

    public NativeLinkInputs(IEnumerable<NativeLibraryInput> libraries, IEnumerable<string> directories)
    {
        if (libraries == null || directories == null) throw new ArgumentNullException(libraries == null ? nameof(libraries) : nameof(directories));
        var ordered = libraries.ToArray();
        if (ordered.Any(library => library == null)) throw new ArgumentException("Native libraries cannot contain null entries.");
        var paths = directories.ToArray();
        foreach (string directory in paths) ValidateText(directory, "Native library directory");
        Libraries = Array.AsReadOnly(ordered);
        Directories = Array.AsReadOnly(paths.Distinct(Paths).ToArray());
    }

    internal static void ValidateText(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw new ArgumentException(description + " must be nonempty and contain no control characters.");
    }

    private static string FullPath(string value, string directory)
        => Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(directory, value));

    public static NativeLinkInputs Create(IEnumerable<NativeLibraryInput> libraries, IEnumerable<string> directories, string baseDirectory)
        => new NativeLinkInputs(libraries, directories).Normalize(baseDirectory);

    public NativeLinkInputs Normalize(string baseDirectory)
    {
        string directory = Path.GetFullPath(baseDirectory);
        return new NativeLinkInputs(Libraries.Select(library => library.Kind == "File"
            ? new NativeLibraryInput("File", FullPath(library.Value, directory)) : library),
            Directories.Select(path => FullPath(path, directory)));
    }

    public NativeLinkInputs Append(NativeLinkInputs additional)
    {
        if (additional == null) throw new ArgumentNullException(nameof(additional));
        return new NativeLinkInputs(Libraries.Concat(additional.Libraries), Directories.Concat(additional.Directories));
    }

    public static NativeLinkInputs Read(string path)
    {
        try { return ReadXml(path); }
        catch (XmlException error) { throw new InvalidDataException("Invalid native-link input XML: " + error.Message, error); }
    }

    private static NativeLinkInputs ReadXml(string path)
    {
        using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
        {
            XElement root = XElement.Load(reader);
            if (root.Name != "NativeLinkInputs" || (string?)root.Attribute("Version") != "1" || root.Attributes().Any(attribute => attribute.Name != "Version"))
                throw new InvalidDataException("Unsupported native-link input manifest; expected NativeLinkInputs Version=1.");
            var groups = root.Elements().ToArray();
            if (groups.Length != 2 || groups[0].Name != "Libraries" || groups[1].Name != "Directories" || groups.Any(group => group.HasAttributes) ||
                root.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)) ||
                groups.Any(group => group.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))))
                throw new InvalidDataException("Native-link manifests require Libraries followed by Directories.");
            var libraries = new List<NativeLibraryInput>();
            foreach (XElement element in groups[0].Elements())
            {
                if (element.Name != "Library" || element.HasElements || element.Value.Length != 0 || element.Attributes().Count() != 2 || element.Attribute("Kind") == null || element.Attribute("Value") == null)
                    throw new InvalidDataException("Invalid native library manifest entry.");
                libraries.Add(new NativeLibraryInput((string)element.Attribute("Kind")!, (string)element.Attribute("Value")!));
            }
            var directories = new List<string>();
            foreach (XElement element in groups[1].Elements())
            {
                if (element.Name != "Directory" || element.HasElements || element.Value.Length != 0 || element.Attributes().Count() != 1 || element.Attribute("Path") == null)
                    throw new InvalidDataException("Invalid native library directory manifest entry.");
                directories.Add((string)element.Attribute("Path")!);
            }
            return Create(libraries, directories, Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
    }

    public void Write(string path, bool writeOnlyWhenDifferent = true)
    {
        string fullPath = Path.GetFullPath(path);
        NativeLinkInputs normalized = Normalize(Path.GetDirectoryName(fullPath)!);
        string text = new XElement("NativeLinkInputs", new XAttribute("Version", "1"),
            new XElement("Libraries", normalized.Libraries.Select(library => new XElement("Library", new XAttribute("Kind", library.Kind), new XAttribute("Value", library.Value)))),
            new XElement("Directories", normalized.Directories.Select(directory => new XElement("Directory", new XAttribute("Path", directory)))))
            .ToString(SaveOptions.DisableFormatting) + Environment.NewLine;
        if (writeOnlyWhenDifferent && File.Exists(fullPath) && File.ReadAllText(fullPath) == text) return;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text, new UTF8Encoding(false));
    }
}

internal sealed class NativeLinkProject
{
    internal string Path { get; }
    internal IReadOnlyList<string> References { get; }
    internal NativeLinkInputs Inputs { get; }
    internal NativeLinkProject(string path, IReadOnlyList<string> references, NativeLinkInputs inputs)
    { Path = path; References = references; Inputs = inputs; }
}

internal static class NativeLinkProjectOrder
{
    internal static NativeLinkInputs Aggregate(string root, IEnumerable<NativeLinkProject> projects)
    {
        var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var byPath = projects.ToDictionary(project => project.Path, comparer);
        var discovered = new List<NativeLinkProject>();
        var seen = new HashSet<string>(comparer);
        void Discover(string path)
        {
            if (!seen.Add(path)) return;
            var project = byPath[path];
            discovered.Add(project);
            foreach (string reference in project.References) Discover(reference);
        }
        Discover(root);
        var owners = discovered.ToDictionary(project => project.Path, _ => 0, comparer);
        foreach (var project in discovered)
            foreach (string dependency in project.References.Distinct(comparer)) owners[dependency]++;
        var pending = new HashSet<string>(owners.Where(pair => pair.Value == 0).Select(pair => pair.Key), comparer);
        var result = NativeLinkInputs.Empty;
        int processed = 0;
        while (pending.Count != 0)
        {
            NativeLinkProject project = discovered.First(candidate => pending.Contains(candidate.Path));
            pending.Remove(project.Path);
            processed++;
            result = result.Append(project.Inputs);
            foreach (string dependency in project.References.Distinct(comparer))
                if (--owners[dependency] == 0) pending.Add(dependency);
        }
        if (processed != discovered.Count) throw new ArgumentException("Cyclic project references in native link inputs.");
        return result;
    }
}
