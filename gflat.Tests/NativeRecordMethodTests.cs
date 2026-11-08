using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeRecordMethodTests
{
    private const string Native = """
        #lang cpp
        namespace Native {
            struct Counter {
                int value;
                int Read() const noexcept(true) { return value; }
                int Add(int amount) { value += amount; return value; }
                int Over(int amount) const { return value + amount; }
                double Over(double amount) const { return value + amount; }
                static int Twice(int value) { return value * 2; }
                static double Twice(double value) { return value * 2; }
            private:
                int Secret() const { return 99; }
            };
        }
        #lang gflat

        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeMembersWorkWithoutNamespaceImportsAndPreserveReceiverStorage(int optimization)
    {
        Run(Native + """
            extension Native::Counter
            {
                public readonly int Read() => -999;
            }
            int main()
            {
                Native::Counter counter = default(Native::Counter);
                if (counter.Read() != 0) return 1;
                if (counter.Add(40) != 40 || counter.value != 40) return 2;
                readonly(Native::Counter)* borrowed = &counter;
                if (borrowed.Read() != 40) return 3;
                if (counter.Over(2) != 42 || borrowed.Over(2.5) != 42.5) return 4;
                if (Native::Counter::Twice(21) != 42 || Native::Counter::Twice(21.25) != 42.5) return 5;
                return borrowed.Over(2);
            }
            """, optimization);
    }

    [Theory]
    [InlineData("readonly(Native::Counter)* value", "value.Add(1)")]
    [InlineData("Native::Counter* value", "value.Secret()")]
    public void ConstReceiversAndPrivateMembersKeepNativeAccessRules(string parameter, string expression)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Native +
            "int Invalid(" + parameter + ") { return " + expression + "; }", options: Options()));
    }

    private static CompilationOptions Options() => new()
    {
        NoStdLib = true,
        ForeignCompilerResolver = () => NativeToolchain.FindClang(),
        ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
    };

    private static void Run(string code, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-record-method-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var program = Compiler.EmitProgram([new SourceFile("record-methods.gf", code)], options: Options());
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 42, $"Native method exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
