using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeOpaqueClassTests
{
    private const string Native = """
        #lang cpp
        namespace Native {
            int destroyed = 0;
            class Base {
            public:
                virtual int Read() const = 0;
                virtual ~Base() {}
            };
            class Widget : public Base {
                int value;
            public:
                explicit Widget(int initial) : value(initial) {}
                ~Widget() override { ++destroyed; }
                int Read() const override { return value; }
                void Set(int next) { value = next; }
            };
            Base* Create(int value) { return new Widget(value); }
            void Destroy(Base* value) { delete value; }
            Widget* CreateWidget(int value) { return new Widget(value); }
            void DestroyWidget(Widget* value) { delete value; }
            int Destroyed() { return destroyed; }
            int Size() { return sizeof(Widget); }
            int Alignment() { return alignof(Widget); }
        }
        #lang gflat

        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeFactoriesVirtualDispatchAndDestructionPreserveNativeObjects(int optimization)
    {
        Run(Native + """
            int ReadBorrowed(ref Native::Base value) => value.Read();
            int main()
            {
                Native::Base* value = Native::Create(42)!;
                readonly(Native::Base)* borrowed = value;
                if (borrowed.Read() != 42 || (*value).Read() != 42) return 1;
                if (ReadBorrowed(ref *value) != 42) return 8;
                if (&(*value) != value) return 2;
                Native::Destroy(value);
                if (Native::Destroyed() != 1) return 3;
                Native::Widget* widget = Native::CreateWidget(20)!;
                widget.Set(42);
                if (widget.Read() != 42) return 4;
                if ((int)sizeof(Native::Widget) != Native::Size()) return 5;
                if ((int)alignof(Native::Widget) != Native::Alignment()) return 6;
                Native::DestroyWidget(widget);
                if (Native::Destroyed() != 2) return 7;
                return 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("Native::Widget value;")]
    [InlineData("Native::Widget copy = *value;")]
    [InlineData("Native::Widget copy = default(Native::Widget);")]
    [InlineData("Native::Widget* created = new* Native::Widget();")]
    [InlineData("Native::Widget[2] values;")]
    [InlineData("*value = *value;")]
    [InlineData("move *value;")]
    [InlineData("delete value;")]
    [InlineData("value.~Native::Widget();")]
    [InlineData("Native::Widget^? tracked = null;")]
    public void NativeLifetimesCannotBeBypassedByHostValueOperations(string statement)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Native +
            "void Invalid(Native::Widget* value) { " + statement + " }", options: Options()));
        Assert.Contains("pointer-only", error.Description);
    }

    [Theory]
    [InlineData("void Invalid(Native::Widget value) {}")]
    [InlineData("Native::Widget Invalid() { return default; }")]
    [InlineData("struct Container { public Native::Widget value; }")]
    public void NativeTypesRequireIndirectionInHostDeclarations(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Native + declaration, options: Options()));
        Assert.Contains("pointer-only", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AlignedNativeArraysUseTheNativeStrideWhenBorrowedThroughPointers(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                class alignas(32) Aligned {
                    int value;
                public:
                    explicit Aligned(int initial) : value(initial) {}
                    int Read() const { return value; }
                };
                static Aligned values[2] = {Aligned(20), Aligned(42)};
                Aligned* First() { return &values[0]; }
                Aligned* Second() { return &values[1]; }
            }
            #lang gflat
            int main()
            {
                Native::Aligned* first = Native::First()!;
                if (sizeof(Native::Aligned) != 32 || alignof(Native::Aligned) != 32) return 1;
                if (first + 1 != Native::Second()) return 2;
                return first[1].Read();
            }
            """, optimization);
    }

    private static CompilationOptions Options() => new()
    {
        NoStdLib = true,
        ForeignCompilerResolver = () => NativeToolchain.FindClang(),
        ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
    };

    private static void Run(string code, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-opaque-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var program = Compiler.EmitProgram([new SourceFile("opaque.gf", code)], options: Options());
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 42, $"Native opaque exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
