using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace gflat.Projects
{
    public enum ProjectKind { Executable, SourceLibrary }

    public sealed class ProjectException : Exception
    {
        public string ProjectPath { get; }
        public int Line { get; }
        public int Column { get; }
        public ProjectException(string path, string message, int line = 1, int column = 1)
            : base(message) { ProjectPath = path; Line = Math.Max(1, line); Column = Math.Max(1, column); }
        public override string ToString() => $"{ProjectPath}({Line},{Column}): error GFPROJ: {Message}";
    }

    // This file is also compiled into the VS client. Keep it independent of the
    // compiler, Visual Studio, MSBuild, and platform-specific runtime APIs.
    public sealed class ProjectModel
    {
        public static readonly StringComparer Paths = Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        public string FilePath { get; }
        public string DirectoryPath => Path.GetDirectoryName(FilePath)!;
        public string Name => Path.GetFileNameWithoutExtension(FilePath);
        public ProjectKind Kind { get; }
        public IReadOnlyList<string> Includes { get; }
        public IReadOnlyList<string> Excludes { get; }
        public IReadOnlyList<string> References { get; }

        private ProjectModel(string path, ProjectKind kind, List<string> includes, List<string> excludes, List<string> references)
        { FilePath = path; Kind = kind; Includes = includes; Excludes = excludes; References = references; }

        public static ProjectModel Load(string path)
        {
            path = Path.GetFullPath(path);
            try { return Parse(path, File.ReadAllText(path)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { throw new ProjectException(path, e.Message); }
        }

        public static ProjectModel Parse(string path, string xml)
        {
            path = Path.GetFullPath(path);
            try
            {
                using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }))
                {
                    var root = XDocument.Load(reader, LoadOptions.SetLineInfo).Root;
                    if (root == null || root.Name != "GflatProject") throw new ProjectException(path, "Expected <GflatProject>.");
                    ValidateAttributes(root, "Kind");
                    if (!Enum.TryParse((string?)root.Attribute("Kind"), false, out ProjectKind kind) ||
                        ((string?)root.Attribute("Kind") != "Executable" && (string?)root.Attribute("Kind") != "SourceLibrary"))
                        Fail(root, "Kind must be Executable or SourceLibrary.");
                    var includes = new List<string>();
                    var excludes = new List<string>();
                    var references = new List<string>();
                    foreach (var node in root.Nodes())
                    {
                        if (node is XComment || node is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value)) continue;
                        if (!(node is XElement element)) { Fail(root, "Only Sources and Reference elements are allowed."); continue; }
                        if (element.Nodes().Any(n => !(n is XComment) && !(n is XText t && string.IsNullOrWhiteSpace(t.Value))))
                            Fail(element, "Project elements cannot contain nested content.");
                        if (element.Name == "Sources")
                        {
                            ValidateAttributes(element, "Include", "Exclude");
                            string? include = (string?)element.Attribute("Include"), exclude = (string?)element.Attribute("Exclude");
                            if ((include == null) == (exclude == null)) Fail(element, "Sources requires exactly one Include or Exclude attribute.");
                            string pattern = Normalize(include ?? exclude!);
                            if (string.IsNullOrWhiteSpace(pattern) || Path.IsPathRooted(pattern) || pattern.Contains(":") ||
                                pattern.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                                Fail(element, "Source patterns must be relative paths inside the project, using / separators.");
                            (include != null ? includes : excludes).Add(pattern);
                        }
                        else if (element.Name == "Reference")
                        {
                            ValidateAttributes(element, "Path");
                            string? rawReference = (string?)element.Attribute("Path");
                            string? reference = rawReference == null ? null : Normalize(rawReference);
                            if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference) || reference!.Contains(":") ||
                                reference.IndexOfAny(new[] { '*', '?' }) >= 0 || !reference.EndsWith(".gfproj", StringComparison.OrdinalIgnoreCase))
                                Fail(element, "Reference Path must be a relative .gfproj path.");
                            string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, Normalize(reference!).Replace('/', Path.DirectorySeparatorChar)));
                            if (references.Contains(resolved, Paths)) Fail(element, "Duplicate project reference.");
                            references.Add(resolved);
                        }
                        else Fail(element, "Unknown project element: " + element.Name);
                    }
                    if (includes.Count == 0) includes.Add("**/*.gf");
                    return new ProjectModel(path, kind, includes, excludes, references);

                    void ValidateAttributes(XElement element, params string[] names)
                    {
                        foreach (var attribute in element.Attributes())
                            if (!names.Contains(attribute.Name.ToString())) Fail(element, "Unknown attribute: " + attribute.Name);
                    }
                    void Fail(XElement element, string message)
                    {
                        var info = (IXmlLineInfo)element;
                        throw new ProjectException(path, message, info.LineNumber, info.LinePosition);
                    }
                }
            }
            catch (XmlException e) { throw new ProjectException(path, e.Message, e.LineNumber, e.LinePosition); }
        }

        public bool IncludesFile(string path)
        {
            string root = DirectoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            path = Path.GetFullPath(path);
            if (!path.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
            string relative = Normalize(path.Substring(root.Length));
            if (!relative.EndsWith(".gf", StringComparison.OrdinalIgnoreCase) || relative.Split('/').Any(IsGeneratedDirectory)) return false;
            return Includes.Any(p => Matches(relative, p)) && !Excludes.Any(p => Matches(relative, p));
        }

        public IReadOnlyList<string> SourceFiles() => EnumerateFiles(DirectoryPath)
            .Where(IncludesFile).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        public static IEnumerable<string> EnumerateFiles(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory)) yield return file;
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (IsGeneratedDirectory(Path.GetFileName(child)) ||
                    (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                // A nested project owns its own sources.
                if (Directory.EnumerateFiles(child, "*.gfproj").Any()) continue;
                foreach (string file in EnumerateFiles(child)) yield return file;
            }
        }

        public static bool IsGeneratedDirectory(string name) =>
            name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            name == ".git" || name == ".vs" || name == ".local-notes";

        public static string Normalize(string path) => path.Replace('\\', '/');
        public static bool Matches(string relative, string pattern)
        {
            string regex = "^";
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == '*')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/') { i++; regex += "(?:.*/)?"; }
                        else regex += ".*";
                    }
                    else regex += "[^/]*";
                }
                else regex += pattern[i] == '?' ? "[^/]" : Regex.Escape(pattern[i].ToString());
            }
            return Regex.IsMatch(relative, regex + "$", Path.DirectorySeparatorChar == '\\' ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(1));
        }
    }

    public sealed class ProjectGraph
    {
        public ProjectModel Root { get; }
        public IReadOnlyList<ProjectModel> Projects { get; }
        public IReadOnlyList<string> Sources { get; }
        private ProjectGraph(ProjectModel root, List<ProjectModel> projects, List<string> sources)
        { Root = root; Projects = projects; Sources = sources; }

        public static ProjectGraph Load(string path, Func<string, ProjectModel>? loader = null)
        {
            loader = loader ?? ProjectModel.Load;
            var active = new HashSet<string>(ProjectModel.Paths);
            var visited = new HashSet<string>(ProjectModel.Paths);
            var projects = new List<ProjectModel>();
            var sources = new List<string>();
            var sourceSet = new HashSet<string>(ProjectModel.Paths);
            ProjectModel Visit(string file)
            {
                file = Path.GetFullPath(file);
                if (!active.Add(file)) throw new ProjectException(file, "Cyclic project reference.");
                var project = loader(file);
                foreach (string reference in project.References)
                {
                    if (active.Contains(reference)) throw new ProjectException(file, "Cyclic project reference: " + reference);
                    if (visited.Contains(reference)) continue;
                    var dependency = Visit(reference);
                    if (dependency.Kind != ProjectKind.SourceLibrary)
                        throw new ProjectException(file, "Only SourceLibrary projects can be referenced: " + reference);
                }
                active.Remove(file);
                visited.Add(file);
                projects.Add(project);
                foreach (string source in project.SourceFiles()) if (sourceSet.Add(source)) sources.Add(source);
                return project;
            }
            var root = Visit(path);
            return new ProjectGraph(root, projects, sources);
        }
    }
}
