using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeOpaqueClassTests
{
    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void NativeUnionsRemainOpaqueAndPreservePointerStride(string language, int optimization)
    {
        Run("#lang " + language + "\n" + """
            union Payload { int number; double floating; };
            typedef union Payload PayloadAlias;
            static union Payload values[2];
            union Payload* First() { values[0].number = 20; values[1].number = 42; return values; }
            PayloadAlias* Second() { return &values[1]; }
            int Read(const union Payload* value) { return value->number; }
            int Size() { return sizeof(union Payload); }
            #lang gflat
            int main()
            {
                Payload* first = First()!;
                if ((int)sizeof(Payload) != Size()) return 1;
                if (first + 1 != Second()) return 2;
                if (Read(first) != 20) return 3;
                return Read(&first[1]);
            }
            """, optimization);
    }

    [Theory]
    [InlineData("Payload value;")]
    [InlineData("Payload copy = *value;")]
    [InlineData("*value = *value;")]
    public void NativeUnionStorageCannotBeCopiedAsAHostValue(string statement)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            #lang c
            union Payload { int number; double floating; };
            #lang gflat
            """ + "\nvoid Invalid(Payload* value) { " + statement + " }", options: Options()));
        Assert.Contains("pointer-only", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeUnionMethodsAndBorrowedReferencesUseNativeStorage(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                union alignas(32) Payload {
                    int number;
                    double floating;
                    explicit Payload(int value) : number(value) {}
                    int Read() const { return number; }
                };
                int ReadBorrowed(const Payload& value) { return value.number; }
            }
            #lang gflat
            int main()
            {
                Native::Payload* value = new* Native::Payload(42);
                if (sizeof(Native::Payload) != 32 || alignof(Native::Payload) != 32) return 1;
                int result = Native::ReadBorrowed(ref *value);
                if (value.Read() != result) return 2;
                delete value;
                return result;
            }
            """, optimization);
    }

    [Fact]
    public void NativeUnionWithDeletedImplicitConstructionCanStillBeBorrowed()
    {
        Run("""
            #lang cpp
            struct Nontrivial { Nontrivial() {} };
            union Payload { Nontrivial object; int number; };
            Payload* Missing() { return nullptr; }
            #lang gflat
            int main()
            {
                if (Missing() == null) return 42;
                return 1;
            }
            """, 0);
    }

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
                explicit Widget(int initial) : value(initial) { if (initial < 0) throw 7; }
                ~Widget() override { ++destroyed; }
                int Read() const override { return value; }
                void Set(int next) { value = next; }
                static Widget* Create(int value) { return new Widget(value); }
                static void Dispose(Widget* value) { delete value; }
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
            int evaluations = 0;
            int Next() { evaluations++; return 42; }
            Native::Widget* Once(Native::Widget* value) { evaluations++; return value; }
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
                Native::Widget* created = Native::Widget::Create(42)!;
                if (created.Read() != 42) return 9;
                Native::Widget::Dispose(created);
                if (Native::Destroyed() != 3) return 10;
                Native::Widget* owned = new* Native::Widget(Next());
                if (owned.Read() != 42 || evaluations != 1) return 11;
                delete Once(owned);
                if (Native::Destroyed() != 4 || evaluations != 2) return 12;
                try { owned = new* Native::Widget(-1); }
                catch
                {
                    if (Native::Destroyed() != 4) return 13;
                    return 42;
                }
                return 14;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("Native::Widget value;")]
    [InlineData("Native::Widget copy = *value;")]
    [InlineData("Native::Widget copy = default(Native::Widget);")]
    [InlineData("Native::Widget created = new Native::Widget(42);")]
    [InlineData("Native::Widget[2] values;")]
    [InlineData("*value = *value;")]
    [InlineData("move *value;")]
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
