using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace gflat.Projects;

public sealed class BinaryProjectReference
{
    public string InterfacePath { get; }
    public string LibraryPath { get; }
    public string LinkPath { get; }
    public NativeLinkInputs NativeInputs { get; }
    public IReadOnlyList<string> RuntimeFiles { get; }

    public BinaryProjectReference(string targetPath, bool shared)
    {
        LibraryPath = Path.GetFullPath(targetPath);
        InterfacePath = LibraryPath + ".gfi";
        LinkPath = shared && Path.DirectorySeparatorChar == '\\' ? Path.ChangeExtension(LibraryPath, ".lib") : LibraryPath;
        var libraries = new List<NativeLibraryInput> { new("File", LinkPath) };
        var directories = new List<string>();
        var runtimeFiles = new List<string>();
        if (shared) runtimeFiles.Add(LibraryPath);
        if (File.Exists(InterfacePath))
        {
            string directory = Path.GetDirectoryName(InterfacePath)!;
            var root = XElement.Load(InterfacePath);
            if (root.Name != "GflatInterface" || (string?)root.Attribute("Version") != "1")
                throw new InvalidDataException("Unsupported binary library interface: " + InterfacePath);
            foreach (var library in root.Element("NativeLibraries")?.Elements("Library") ?? Enumerable.Empty<XElement>())
            {
                string kind = (string?)library.Attribute("Kind") ?? throw new InvalidDataException("Missing native library kind");
                string value = (string?)library.Attribute("Value") ?? throw new InvalidDataException("Missing native library path");
                libraries.Add(new(kind, kind == "File" ? FullPath(value, directory) : value));
            }
            directories.AddRange((root.Element("NativeDirectories")?.Elements("Directory") ?? Enumerable.Empty<XElement>())
                .Select(element => FullPath(element.Value, directory)));
            runtimeFiles.AddRange((root.Element("RuntimeFiles")?.Elements("File") ?? Enumerable.Empty<XElement>())
                .Select(element => FullPath(element.Value, directory)));
            if ((bool?)root.Attribute("CppRuntime") == true && Path.DirectorySeparatorChar != '\\')
            {
                libraries.Add(new("Name", "stdc++"));
                libraries.Add(new("Name", "m"));
            }
        }
        NativeInputs = new(libraries, directories);
        RuntimeFiles = runtimeFiles.Distinct(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
    }

    private static string FullPath(string path, string directory) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
}
