using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeOpaqueClassTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeclaredNativeCopyConstructorsCreateIndependentOwnedObjects(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                static int live = 0;
                static int copies = 0;
                static int destroyed = 0;
                class Counter {
                    int value;
                public:
                    Counter(int input) : value(input) { ++live; }
                    Counter(const Counter& input) : value(input.value) { ++live; ++copies; }
                    ~Counter() { --live; ++destroyed; }
                    int Read() const { return value; }
                    void Set(int input) { value = input; }
                };
                int Live() { return live; }
                int Copies() { return copies; }
                int Destroyed() { return destroyed; }
            }
            #lang gflat
            int main()
            {
                Native::Counter* source = new* Native::Counter(42);
                Native::Counter* copy = new* Native::Counter(ref *source);
                if (Native::Live() != 2 || Native::Copies() != 1) return 1;
                source.Set(7);
                if (copy.Read() != 42) return 2;
                delete source;
                if (Native::Live() != 1 || Native::Destroyed() != 1 || copy.Read() != 42) return 3;
                delete copy;
                if (Native::Live() != 0 || Native::Destroyed() != 2) return 4;
                return 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingNativeCopyConstructionLeavesTheSourceAlive(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                static int live = 0;
                static int destroyed = 0;
                class Counter {
                    int value;
                public:
                    Counter(int input) : value(input) { ++live; }
                    Counter(const Counter& input) : value(input.value) { throw 17; }
                    ~Counter() { --live; ++destroyed; }
                    int Read() const { return value; }
                };
                int Live() { return live; }
                int Destroyed() { return destroyed; }
            }
            #lang gflat
            int main()
            {
                Native::Counter* source = new* Native::Counter(42);
                try
                {
                    Native::Counter* copy = new* Native::Counter(ref *source);
                    delete copy;
                    return 1;
                }
                catch { }
                if (Native::Live() != 1 || Native::Destroyed() != 0 || source.Read() != 42) return 2;
                delete source;
                if (Native::Live() != 0 || Native::Destroyed() != 1) return 3;
                return 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("private: Counter(const Counter&) {}")]
    [InlineData("public: Counter(const Counter&) = delete;")]
    [InlineData("")]
    public void PrivateDeletedAndImplicitCopyConstructorsAreNotImported(string copyDeclaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            #lang cpp
            class Counter {
            public:
                Counter(int value) {}
            """ + "\n" + copyDeclaration + "\n};\n" + """
            #lang gflat
            void Invalid(Counter* source)
            {
                Counter* copy = new* Counter(ref *source);
            }
            """, options: Options()));
        Assert.Contains("constructor", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void ForwardDeclaredHandlesCanBePassedWithoutKnowingTheirLayout(string language, int optimization)
    {
        Run("#lang " + language + "\n" + """
            struct Handle;
            typedef struct Handle HandleAlias;
            static int storage = 42;
            HandleAlias* Open() { return (HandleAlias*)&storage; }
            int Read(const struct Handle* handle) { return *(const int*)handle; }
            #lang gflat
            int main()
            {
                Handle* handle = Open()!;
                return Read(&*handle);
            }
            """, optimization, debug: true);
    }

    [Theory]
    [InlineData("sizeof(Handle)")]
    [InlineData("alignof(Handle)")]
    [InlineData("handle + 1")]
    [InlineData("handle - handle")]
    [InlineData("&handle[0]")]
    [InlineData("++handle")]
    public void ForwardDeclaredHandlesRejectOperationsRequiringTheirLayout(string expression)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            #lang c
            struct Handle;
            #lang gflat
            """ + "\nvoid Invalid(Handle* handle) { " + expression + "; }", options: Options()));
        Assert.True(error.Description.Contains("incomplete") || error.Description.Contains("complete native"), error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CompleteNativeDefinitionReplacesItsForwardDeclaration(int optimization)
    {
        Run("""
            #lang cpp
            struct Handle;
            struct Handle { int value; };
            Handle Make() { return Handle{42}; }
            #lang gflat
            int main()
            {
                Handle value = Make();
                if (sizeof(Handle) != sizeof(int)) return 1;
                return value.value;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ForwardDeclaredCppHandlesSupportBorrowedReferenceArguments(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                class Handle;
                static int storage = 41;
                Handle* Open() { return reinterpret_cast<Handle*>(&storage); }
                void Increment(Handle& handle) { ++reinterpret_cast<int&>(handle); }
                int Read(const Handle& handle) { return reinterpret_cast<const int&>(handle); }
            }
            #lang gflat
            int main()
            {
                Native::Handle* handle = Native::Open()!;
                Native::Increment(ref *handle);
                return Native::Read(ref *handle);
            }
            """, optimization, debug: true);
    }

    [Theory]
    [InlineData("Handle value;")]
    [InlineData("Handle copy = *handle;")]
    [InlineData("*handle = *handle;")]
    public void ForwardDeclaredHandlesCannotBeCopiedAsHostValues(string statement)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            #lang c
            struct Handle;
            #lang gflat
            """ + "\nvoid Invalid(Handle* handle) { " + statement + " }", options: Options()));
        Assert.Contains("pointer-only", error.Description);
    }

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
    [InlineData("Native::Widget copy = default(Native::Widget);", "default")]
    [InlineData("Native::Widget[2] values;", "destructor-bearing")]
    [InlineData("Native::Widget^? tracked = null;", "cannot use managed allocation")]
    public void NativeLifetimesCannotBeBypassedByUninitializedStorageOrManagedAllocation(string statement, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Native +
            "void Invalid(Native::Widget* value) { " + statement + " }", options: Options()));
        Assert.Contains(diagnostic, error.Description);
    }

    [Theory]
    [InlineData("void Invalid(Native::Widget value) {}")]
    [InlineData("struct Container { public Native::Widget value; }")]
    public void NativeTypesCanAppearInHostValueDeclarations(string declaration)
    {
        _ = Compiler.Check(Native + declaration, options: Options());
    }

    [Fact]
    public void NativeValueResultsCannotBeCreatedByImplicitZeroInitialization()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Native +
            "Native::Widget Invalid() { return default; }", options: Options()));
        Assert.Contains("default", error.Description);
    }

    [Theory]
    [InlineData("Native::Widget value;")]
    [InlineData("Native::Widget copy = *value;")]
    [InlineData("Native::Widget created = new Native::Widget(42);")]
    [InlineData("*value = *value;")]
    [InlineData("move *value;")]
    [InlineData("value.~Native::Widget();")]
    public void NativeValueOperationsResolveNativeLifetimeMembers(string statement)
    {
        _ = Compiler.Check(Native + "void Use(Native::Widget* value) { " + statement + " }", options: Options());
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

    private static void Run(string code, int optimization, bool debug = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-opaque-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var program = Compiler.EmitProgram([new SourceFile("opaque.gf", code)], options: Options() with { DebugSymbols = debug });
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
