using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeVoidPointerTests
{
    private const string Native = """
        typedef const void ConstVoid;
        typedef ConstVoid* ConstPointer;
        static const int stored = 42;
        ConstPointer Read(void) { return &stored; }
        int Get(ConstVoid* value) { return *(const int*)value; }
        void Copy(void* destination, const void* source) { *(int*)destination = *(const int*)source; }
        const void Ignore(void) {}
        typedef const void (*VoidAction)(void);
        VoidAction Action(void) { return Ignore; }
        """;

    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void ConstVoidPointersPreserveReadonlyAccessAcrossCallsAndAliases(string language, int optimization)
        => Run("#lang " + language + "\n" + Native + "\n" + """
            #lang gflat
            int main()
            {
                readonly(void)*? source = Read();
                if (source == null) return 1;
                int value = 0;
                Copy(&value, source);
                if (value != 42) return 2;
                Ignore();
                void()* action = Action()!;
                action();
                return Get(source);
            }
            """, optimization);

    [Theory]
    [InlineData("c", "const void*")]
    [InlineData("c", "ConstPointer")]
    [InlineData("cpp", "const void*")]
    [InlineData("cpp", "ConstPointer")]
    public void AConstNativeVoidResultCannotBecomeAMutablePointer(string language, string result)
    {
        string code = "#lang " + language + "\ntypedef const void* ConstPointer;\n" + result + " Read(void) { return 0; }\n" + """
            #lang gflat
            void*? Mutable() => Read();
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(code, options: Options()));
        Assert.Contains("readonly(void)*?", error.Description);
        Assert.Contains("void*?", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeMemoryFunctionsAcceptReadonlyByteStorage(int optimization)
        => Run("""
            #lang c
            #include <string.h>
            #lang gflat
            int main()
            {
                readonly(char)* source = "*";
                char[2] output;
                memcpy(&output[0], source, 2);
                if (memcmp(&output[0], source, 2) != 0 || (nuint)strlen(&output[0]) != (nuint)1) return 1;
                return (int)output[0];
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeMethodsDistinguishConstVoidPointersFromVoidResults(int optimization)
        => Run("""
            #lang cpp
            struct Counter {
                int value;
                const void Ignore() const {}
                const void* Read() const { return &value; }
            };
            int Get(const void* value) { return *(const int*)value; }
            #lang gflat
            int main()
            {
                Counter counter = new();
                counter.value = 42;
                counter.Ignore();
                readonly(void)*? value = counter.Read();
                if (value == null) return 1;
                return Get(value);
            }
            """, optimization);

    private static CompilationOptions Options() => new()
    {
        NoStdLib = true,
        InternalizeFunctions = true,
        DebugSymbols = true,
        ForeignCompilerResolver = () => NativeToolchain.FindClang(),
        ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
    };

    [Fact]
    public void ReadonlyVoidPointeesRemainConstInDebugMetadata()
    {
        string code = "#lang c\n" + Native + "\n" + """
            #lang gflat
            int main()
            {
                readonly(void)*? value = Read();
                if (value == null) return 1;
                return 42;
            }
            """;
        var program = Compiler.EmitProgram([new SourceFile("native-void-debug.gf", code)], options: Options());
        Assert.Contains("!DIDerivedType(tag: DW_TAG_const_type, baseType: null)", program.Ir);
    }

    private static void Run(string code, int optimization)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-void-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var program = Compiler.EmitProgram([new SourceFile("native-void.gf", code)], options: Options());
            string ir = Path.Combine(root, "program.ll"), executable = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.False(compiled.StandardError.Contains("invalid debug info", StringComparison.OrdinalIgnoreCase), compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 42, $"Native void pointer exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
