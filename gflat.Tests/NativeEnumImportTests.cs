namespace gflat.Tests;

public sealed class NativeEnumImportTests
{
    [Fact]
    public void RepeatedAndCachedImportsShareOneEnumDeclaration()
    {
        const string native = """
            #lang cpp
            namespace Native {
                enum class Mode : unsigned short;
                enum class Mode : unsigned short { Answer = 42 };
            }
            #lang gflat
            """;
        SourceFile[] sources = [new("first.gf", native + "\nint main() => (int)Native::Mode::Answer;"), new("second.gf", native)];
        var options = new CompilationOptions { NoStdLib = true, ForeignCompilerResolver = () => NativeToolchain.FindClang() };
        for (int pass = 0; pass < 2; pass++)
            Compiler.Check(sources, options: options);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FixedEnumsPreserveConstantsStorageAndNativeCalls(int optimization)
    {
        const string code = """
            #lang cpp
            namespace Native {
                enum class Small : signed char { First = -128, Next, Last = 127 };
                enum class Wide : unsigned long long { High = 0xffffffffffffffffULL };
                enum class Mode : unsigned short { First = 7, Next, Answer = 42 };
                enum class Default { Answer = 42 };
                Mode Echo(Mode value) { return value; }
                void Set(Mode& value) { value = Mode::Answer; }
                Wide Max() { return Wide::High; }
                Small Minimum() { return Small::First; }
                class Widget {
                    Mode value;
                public:
                    explicit Widget(Mode initial) : value(initial) {}
                    Mode Read() const { return value; }
                };
            }
            #lang gflat
            int main()
            {
                if (sizeof(Native::Small) != 1 || sizeof(Native::Mode) != 2 || sizeof(Native::Wide) != 8) return 1;
                if ((int)Native::Small::Next != -127 || (int)Native::Mode::Next != 8) return 2;
                if (Native::Minimum() != Native::Small::First || Native::Max() != Native::Wide::High) return 3;
                if ((ulong)Native::Wide::High != 18446744073709551615ul) return 4;
                if ((int)Native::Default::Answer != 42) return 5;
                Native::Mode value = Native::Echo(Native::Mode::First);
                Native::Set(ref value);
                Native::Widget* widget = new* Native::Widget(value);
                int result = (int)widget.Read();
                delete widget;
                return result;
            }
            """;
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var program = Compiler.EmitProgram([new SourceFile("native-enum.gf", code)], options: new()
            {
                NoStdLib = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(),
                ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
            });
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.Equal(42, result.ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
