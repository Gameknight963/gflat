using System.Xml.Linq;

namespace gflat.Tests;

public sealed class BinaryEnumExportTests
{
    [Theory]
    [InlineData("private enum Hidden { Value = 42 } public Hidden Read() => Hidden::Value;", "not exported")]
    [InlineData("public enum @class { Value = 42 } public int Read() => 42;", "C++ header")]
    [InlineData("public enum State { @default = 42 } public int Read() => 42;", "C++ header")]
    [Trait("Optimization", "Independent")]
    public void UnsupportedBinaryEnumSurfacesProduceFocusedDiagnostics(string code, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("binary-enum-diagnostic.gf"), code);
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.EmitProgram([source],
            options: new() { NoStdLib = true, ExportSources = [source.Path] }));
        Assert.Contains(expected, error.Description);
    }

    [Theory]
    [InlineData("StaticLibrary")]
    [InlineData("DynamicLibrary")]
    [Trait("Optimization", "Independent")]
    public void BinaryEnumsRetainStorageConstantsAndReferenceCallsWithoutImplementationSources(string kind)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-binary-enums-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "library.gf");
            File.WriteAllText(source, """
                namespace API::Types;

                alias Storage = ushort;
                public enum Status : Storage { Start = 7, Next, Answer = 42 }
                public enum Small : sbyte { Minimum = -128 }
                public enum Wide : ulong { Maximum = 18446744073709551615ul }
                public enum Signed : long { Minimum = -9223372036854775808l }
                private enum Hidden { Secret = 1 }
                public Status Echo(Status input = Status::Answer) nothrow => input;
                public void Set(ref Status input) nothrow { input = Status::Answer; }
                public Wide Maximum() nothrow => Wide::Maximum;
                public Signed Minimum() nothrow => Signed::Minimum;
                public Small Smallest() nothrow => Small::Minimum;
                private Status current = Status::Answer;
                public readonly(Status)* GetCurrent() nothrow => &current;
                """);
            string library = Path.Combine(root, "answer" + (OperatingSystem.IsWindows() ? kind == "StaticLibrary" ? ".lib" : ".dll" : kind == "StaticLibrary" ? ".a" : ".so"));
            Assert.Equal(0, CompilerProgram.Main([source, "--no-stdlib", "--output-type", kind, "--optimization", "2", "-o", library]));
            var metadata = XElement.Load(library + ".gfi");
            Assert.Equal("2", (string?)metadata.Attribute("Version"));
            Assert.Equal(4, metadata.Elements("Enum").Count());
            string header = File.ReadAllText(library + ".gfi.gfh");
            Assert.DoesNotContain("Hidden", header);
            Assert.DoesNotContain("alias", header);
            Assert.Contains("Status : ushort", header);
            File.Delete(source);
            string consumer = Path.Combine(root, "consumer.gf");
            File.WriteAllText(consumer, """
                using API::Types;

                int main()
                {
                    if (sizeof(Status) != 2 || sizeof(Small) != 1 || sizeof(Wide) != 8) return 1;
                    if (Echo() != Status::Answer) return 4;
                    if ((int)Status::Next != 8 || (int)Smallest() != -128 || *GetCurrent() != Status::Answer) return 2;
                    if ((ulong)Maximum() != 18446744073709551615ul || (long)Minimum() != -9223372036854775808l) return 3;
                    Status value = Echo(Status::Start);
                    Set(ref value);
                    return (int)value;
                }
                """);
            string executable = Path.Combine(root, "consumer.exe");
            Assert.Equal(0, CompilerProgram.Main([consumer, "--no-stdlib", "--library-interface", library + ".gfi", "--optimization", "2", "-o", executable]));
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
            string cpp = Path.Combine(root, "consumer.cpp");
            File.WriteAllText(cpp, "#include \"" + Path.GetFileName(library) + ".hpp\"\n" + """
                using namespace API::Types;
                int main()
                {
                    static_assert(sizeof(Status) == 2 && sizeof(Small) == 1 && sizeof(Wide) == 8);
                    if ((int)Status::Next != 8 || (int)Smallest() != -128 || *GetCurrent() != Status::Answer) return 1;
                    if ((std::uint64_t)Maximum() != UINT64_MAX || (std::int64_t)Minimum() != INT64_MIN) return 2;
                    Status value = Echo(Status::Start);
                    Set(value);
                    return (int)value;
                }
                """);
            string link = kind == "DynamicLibrary" && OperatingSystem.IsWindows() ? Path.ChangeExtension(library, ".lib") : library;
            string native = Path.Combine(root, "cpp-consumer.exe");
            var compiled = NativeToolchain.CompileCpp(cpp, native, [link]);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(native, []).ExitCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
