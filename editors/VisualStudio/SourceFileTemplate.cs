using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace gflat.VisualStudio;

// Kept independent of VS automation so the generated source is checked by CLI tests.
internal static class SourceFileTemplate
{
    private static readonly HashSet<string> Keywords = new HashSet<string>((
        "managed namespace using class struct interface enum public private protected internal new delete return if else while for foreach in repeat break continue switch case default unsafe const static inline readonly immutable virtual override expect weak replace abstract null true false void int float double bool char long extralong uint ulong nint nuint byte sbyte short ushort extern defer alias global operator base sizeof alignof typeof reflect reflection nameof throw nothrow try catch " +
        "this ref move copy finalizer extension implementation constrained explicit get set at with var").Split(' '), StringComparer.Ordinal);

    public static string Generate(string fileName, string rootNamespace, string relativeFolder,
        bool generateClass, bool generateNamespace, bool bracesOnNewLine, string indentation = "    ")
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        bool isType = generateClass && name.Length > 0 && char.IsUpper(name[0]) &&
            name.All(c => char.IsLetterOrDigit(c)) && name.All(c => c <= 127);
        var parts = new List<string>();
        if (generateNamespace)
        {
            parts.AddRange(rootNamespace.Replace("::", ".").Split('.').Where(p => p.Length > 0).Select(Identifier));
            parts.AddRange(relativeFolder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(Identifier));
        }
        string qualifiedName = string.Join("::", parts);
        var text = new StringBuilder();
        string indent = qualifiedName.Length > 0 ? indentation : "";
        if (qualifiedName.Length > 0)
        {
            text.Append("namespace ").Append(qualifiedName);
            text.Append(bracesOnNewLine ? "\r\n{\r\n" : " {\r\n");
        }
        if (isType)
        {
            text.Append(indent).Append("class ").Append(Identifier(name));
            text.Append(bracesOnNewLine ? "\r\n" + indent + "{\r\n" : " {\r\n");
            text.Append(indent).Append("}\r\n");
        }
        if (qualifiedName.Length > 0) text.Append("}\r\n");
        return text.ToString();
    }

    private static string Identifier(string name)
    {
        name = name.TrimStart('@');
        string identifier = new string(name.Select(c => c <= 127 && (char.IsLetterOrDigit(c) || c == '_') ? c : '_').ToArray());
        if (identifier.Length == 0) identifier = "_";
        if (char.IsDigit(identifier[0])) identifier = "_" + identifier;
        return Keywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    public static string RelativeFolder(string projectFile, string file, string namespaceRoot)
    {
        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
        string root = Path.GetFullPath(Path.IsPathRooted(namespaceRoot) ? namespaceRoot : Path.Combine(projectDirectory, namespaceRoot));
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.GetDirectoryName(Path.GetFullPath(file))! + Path.DirectorySeparatorChar;
        return directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? directory.Substring(root.Length).TrimEnd(Path.DirectorySeparatorChar) : "";
    }
}
