using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeConstructorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeReferencesBorrowScalarsAndOpaqueObjects(int optimization)
    {
        Run("""
            #lang cpp
            class Widget {
                int value;
            public:
                explicit Widget(const int& initial) : value(initial) {}
                void Add(int& amount) { value += amount; ++amount; }
                int Read() const { return value; }
            };
            int Read(const Widget& widget) { return widget.Read(); }
            using NumberRef = int&;
            void Increase(NumberRef value) { ++value; }
            void Increase(double& value) { value += 1.0; }
            #lang gflat
            int main()
            {
                int initial = 40;
                Widget* widget = new* Widget(ref initial);
                int amount = 1;
                Increase(ref amount);
                double fractional = 1.0;
                Increase(ref fractional);
                widget.Add(ref amount);
                int result = Read(ref *widget);
                delete widget;
                if (amount != 3 || initial != 40 || fractional != 2.0) return 1;
                return result;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("readonly(int) value = 42; Write(ref value);")]
    [InlineData("Write(ref 42);")]
    public void NativeReferencesKeepHostMutabilityAndAddressabilityChecks(string statement)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            #lang cpp
            void Write(int& value) { value = 0; }
            #lang gflat
            """ + "\nvoid Invalid() { " + statement + " }", options: Options()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeParameterNamesCannotCaptureGeneratedBridgeLocals(int optimization)
    {
        Run("""
            #lang cpp
            struct Pair { int value; };
            Pair Create(int __gflat_result, int immutable, int arg0) {
                return Pair{__gflat_result + immutable + arg0};
            }
            int Unnamed(int, int arg0) { return arg0; }
            #lang gflat
            int main()
            {
                Pair pair = Create(10, 20, 12);
                return Unnamed(0, pair.value);
            }
            """, optimization);
    }

    [Fact]
    public void NativeConstructionGuideRuns()
    {
        string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guides", "FOREIGN_SOURCE.md"));
        string code = System.Text.RegularExpressions.Regex.Matches(document, @"```gflat\r?\n(.*?)```",
            System.Text.RegularExpressions.RegexOptions.Singleline).Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Groups[1].Value).Single(example => example.Contains("new* Native::Counter", StringComparison.Ordinal));
        Run(code, 2, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UniqueOwnsNativeObjectsAndResetInvokesTheNativeDestructor(int optimization)
    {
        Run("""
            #lang cpp
            int destroyed = 0;
            class Widget {
                int value;
            public:
                explicit Widget(int initial) : value(initial) {}
                ~Widget() { ++destroyed; }
                int Read() const { return value; }
            };
            int Destroyed() { return destroyed; }
            #lang gflat
            using Standard::Memory;

            int main()
            {
                {
                    Unique<Widget> owner = new(new* Widget(42));
                    if (owner.Value.Read() != 42) return 1;
                    owner.Reset();
                    if (!owner.IsEmpty || Destroyed() != 1) return 2;
                    owner.Reset(new* Widget(42));
                    owner.Reset(new* Widget(43));
                    if (Destroyed() != 2 || owner.Value.Read() != 43) return 3;
                }
                if (Destroyed() != 3) return 4;
                return 42;
            }
            """, optimization, unique: true);
    }

    [Fact]
    public void ConstructorCallPermissionDoesNotExposeThePrivateFactoryToSourceCalls()
    {
        const string native = "#lang cpp\nclass Widget { public: explicit Widget(int value) {} };\n#lang gflat\n";
        var baseline = Compiler.Check(native + "int main() => 0;", options: Options());
        string factory = baseline.Ast.Members.OfType<ast.MethodDeclaration>()
            .Single(method => method.Name.StartsWith("__gflat_native_construct_", StringComparison.Ordinal)).Name;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(native +
            "int main() { Widget* value = new* Widget(42); " + factory + "(42); delete value; return 0; }", options: Options()));
        Assert.Contains("private to its source file", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PublicDefaultAndOverloadedNativeConstructorsUseNativeAllocation(int optimization)
    {
        Run("""
            #lang cpp
            class Default {
                int value;
            public:
                int Read() const { return value; }
            };
            class Choice {
                int value;
            public:
                explicit Choice(int initial) : value(initial + 1) {}
                explicit Choice(long long initial) : value((int)initial + 2) {}
                int Read() const { return value; }
            };
            #lang gflat
            namespace App {
                public int Run()
                {
                    Default* zero = new* Default();
                    if (zero.Read() != 0) return 1;
                    delete zero;
                    Choice* first = new* Choice(41);
                    Choice* second = new* Choice(40l);
                    if (first.Read() != 42 || second.Read() != 42) return 2;
                    delete first;
                    delete second;
                    Choice*? empty = null;
                    delete empty;
                    return 42;
                }
            }
            int main() => App::Run();
            """, optimization);
    }

    [Theory]
    [InlineData("Factory* value = new* Factory();", "public constructor")]
    [InlineData("Factory* value = Factory::Get()!; delete value;", "native destructor")]
    public void PrivateNativeLifetimesRequireTheirNativeFactoryAndReleaseApi(string statement, string expected)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            #lang cpp
            class Factory {
                Factory() {}
                ~Factory() {}
            public:
                static Factory* Get() { static Factory value; return &value; }
            };
            #lang gflat
            """ + "\nvoid Invalid() { " + statement + " }", options: Options()));
        Assert.Contains(expected, error.Description);
    }

    private static CompilationOptions Options() => new()
    {
        NoStdLib = true,
        ForeignCompilerResolver = () => NativeToolchain.FindClang(),
        ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
    };

    private static void Run(string code, int optimization, bool unique = false, int expectedExitCode = 42)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-constructor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new SourceFile("constructors.gf", code);
            var sources = unique ? StandardTestSources.Load("core/Memory/Unique.gf").Append(source) : [source];
            var program = Compiler.EmitProgram(sources, options: Options() with { NoStdLib = !unique });
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == expectedExitCode, $"Native constructor exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
