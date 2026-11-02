using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace gflat.Projects;

public sealed class PreprocessorInputs
{
    public static PreprocessorInputs Empty { get; } = new([], []);
    public IReadOnlyList<string> Directories { get; }
    public IReadOnlyDictionary<string, string> Defines { get; }

    public PreprocessorInputs(IEnumerable<string> directories, IEnumerable<string> defines)
    {
        Directories = directories.Distinct(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string definition in defines)
        {
            int equals = definition.IndexOf('=');
            string name = equals < 0 ? definition : definition.Substring(0, equals);
            if (name.Length == 0 || !(IsLetter(name[0]) || name[0] == '_') || name.Any(c => !(IsLetter(c) || c >= '0' && c <= '9' || c == '_')))
                throw new ArgumentException("Invalid preprocessor definition: " + definition);
            values[name] = equals < 0 ? "1" : definition.Substring(equals + 1);
        }
        Defines = values;
    }

    private static bool IsLetter(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';

    public PreprocessorInputs Append(PreprocessorInputs other) => new(Directories.Concat(other.Directories),
        Defines.Select(p => p.Key + "=" + p.Value).Concat(other.Defines.Select(p => p.Key + "=" + p.Value)));

    public static PreprocessorInputs Read(string path)
    {
        var root = XDocument.Load(path).Root;
        if (root?.Name != "PreprocessorInputs") throw new InvalidDataException("Invalid preprocessor inputs manifest");
        return new(root.Elements("IncludeDirectory").Select(element => element.Value), root.Elements("Define").Select(element => element.Value));
    }

    public void Write(string path)
    {
        var document = new XDocument(new XElement("PreprocessorInputs",
            Directories.Select(directory => new XElement("IncludeDirectory", directory)),
            Defines.Select(pair => new XElement("Define", pair.Key + "=" + pair.Value))));
        string text = document.ToString();
        WriteChanged(path, text);
    }

    public static void WriteDependencies(string path, IEnumerable<string> paths)
    {
        var values = paths.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray();
        WriteChanged(path, string.Concat(values.Select(value => value + Environment.NewLine)));
        WriteDependencyState(path, values);
    }

    public static void WriteDependencyState(string path, IEnumerable<string> paths)
        => WriteChanged(path + ".state", string.Concat(paths.Select(value => (File.Exists(value) ? "present|" : "missing|") + value + Environment.NewLine)));

    private static void WriteChanged(string path, string text)
    { if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text); }
}
