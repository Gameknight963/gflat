using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace gflat.Tests;

public sealed class StandardNatvisTests
{
    private static readonly XNamespace Natvis = "http://schemas.microsoft.com/vstudio/debugger/natvis/2010";

    [Fact]
    public void VisualizersUseCountedStorageAndTypedPointers()
    {
        var visualizers = ReadVisualizers();
        Assert.Equal(4, visualizers.Length);
        var expected = new Dictionary<string, (string Size, string Pointer)>
        {
            ["Standard::Text::String"] = ("length", "data"),
            ["Standard::Collections::Vector<*>"] = ("count", "data"),
            ["Standard::Memory::Span<*>"] = ("length", "data"),
            ["Standard::IO::MemoryStream"] = ("buffer.count", "buffer.data")
        };
        Assert.Equal(expected.Keys.OrderBy(name => name, StringComparer.Ordinal),
            visualizers.Select(type => type.Attribute("Name")!.Value).OrderBy(name => name, StringComparer.Ordinal));
        foreach (var type in visualizers)
        {
            string name = type.Attribute("Name")!.Value;
            Assert.True(expected.TryGetValue(name, out var storage), name);
            XElement items = Assert.Single(type.Descendants(Natvis + "ArrayItems"));
            Assert.Equal(storage.Size, items.Element(Natvis + "Size")!.Value);
            Assert.Equal(storage.Pointer, items.Element(Natvis + "ValuePointer")!.Value);
            Assert.Contains(storage.Pointer + " != 0", items.Attribute("Condition")!.Value);
        }
        XElement text = visualizers.Single(type => type.Attribute("Name")!.Value == "Standard::Text::String");
        Assert.Equal("data,[length]s8", text.Element(Natvis + "StringView")!.Value);
        Assert.Contains(text.Elements(Natvis + "DisplayString"), display => display.Value == "{data,[length]s8}");
    }

    [Fact]
    public void VisualizerPatternsAndFieldPathsMatchFreshNativeDebugMetadata()
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("standard-natvis.gf", """
                using Standard::Text;
                using Standard::Collections;
                using Standard::Memory;
                using Standard::IO;

                int main()
                {
                    String text = s"A\0B";
                    Vector<int> values = new();
                    values.Add(42);
                    Span<int> view = values.AsSpan();
                    Span<readonly(int)> readOnly = values.AsReadOnly();
                    MemoryStream stream = new();
                    stream.Write("abc", 3);
                    stream.Position = 1l;
                    return (int)(text.Length + values.Count + view.Length + readOnly.Length) + (int)stream.Position;
                }
                """));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, DebugSymbols = true, InternalizeFunctions = true });
        Assert.Contains("language: DW_LANG_C_plus_plus", ir);
        Assert.Contains(OperatingSystem.IsWindows() ? "!\"CodeView\"" : "!\"Dwarf Version\"", ir);
        var metadata = Regex.Matches(ir, @"^(!\d+) = (.+)$", RegexOptions.Multiline)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        var types = new Dictionary<string, string>
        {
            ["Standard::Text::String"] = "Standard::Text::String",
            ["Standard::Collections::Vector<*>"] = "Standard::Collections::Vector<int>",
            ["Standard::Memory::Span<*>"] = "Standard::Memory::Span<int>",
            ["Standard::IO::MemoryStream"] = "Standard::IO::MemoryStream"
        };
        foreach (XElement visualizer in ReadVisualizers())
        {
            string pattern = visualizer.Attribute("Name")!.Value;
            string name = types[pattern];
            Assert.Matches("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$", name);
            string definition = Composite(metadata, name);
            foreach (XElement expression in visualizer.Descendants()
                .Where(element => element.Name == Natvis + "Size" || element.Name == Natvis + "ValuePointer" || element.Name == Natvis + "Item"))
            {
                string field = ResolveFieldPath(metadata, definition, expression.Value);
                if (expression.Name == Natvis + "ValuePointer")
                {
                    string type = Regex.Match(field, @"baseType: (!\d+)").Groups[1].Value;
                    Assert.Contains("DW_TAG_pointer_type", metadata[type]);
                }
            }
        }

        AssertFieldOffsets(metadata, "Standard::Text::String", ("data", 0), ("length", 64), ("capacity", 128));
        AssertFieldOffsets(metadata, "Standard::Collections::Vector<int>", ("data", 0), ("count", 64), ("capacity", 128));
        AssertFieldOffsets(metadata, "Standard::Memory::Span<int>", ("data", 0), ("length", 64));
        AssertFieldOffsets(metadata, "Standard::IO::MemoryStream", ("buffer", 0), ("position", 192));
        Composite(metadata, "Standard::Collections::Vector<char>");
        Composite(metadata, "Standard::Memory::Span<readonly(int)>");
    }

    private static XElement[] ReadVisualizers()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Debugging", "gflat.natvis"));
        Assert.Equal(Natvis + "AutoVisualizer", document.Root!.Name);
        return document.Root.Elements(Natvis + "Type").ToArray();
    }

    private static string Composite(IReadOnlyDictionary<string, string> metadata, string name)
        => Assert.Single(metadata.Values, value => value.Contains("!DICompositeType(", StringComparison.Ordinal) &&
            value.Contains("name: \"" + name + "\"", StringComparison.Ordinal));

    private static Dictionary<string, string> Fields(IReadOnlyDictionary<string, string> metadata, string definition)
        => Regex.Matches(Regex.Match(definition, @"elements: !\{([^}]*)\}").Groups[1].Value, @"!\d+")
            .Select(match => metadata[match.Value])
            .ToDictionary(field => Regex.Match(field, "name: \"([^\"]*)\"").Groups[1].Value);

    private static string ResolveFieldPath(IReadOnlyDictionary<string, string> metadata, string definition, string path)
    {
        string field = "";
        foreach (string name in path.Split('.'))
        {
            var fields = Fields(metadata, definition);
            Assert.True(fields.ContainsKey(name), "Missing debug field " + path);
            field = fields[name];
            definition = metadata[Regex.Match(field, @"baseType: (!\d+)").Groups[1].Value];
        }
        return field;
    }

    private static void AssertFieldOffsets(IReadOnlyDictionary<string, string> metadata, string name, params (string Name, int Offset)[] expected)
    {
        var fields = Fields(metadata, Composite(metadata, name));
        foreach (var field in expected)
            Assert.Contains("offset: " + field.Offset, fields[field.Name]);
    }
}
