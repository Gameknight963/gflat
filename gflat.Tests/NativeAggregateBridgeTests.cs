namespace gflat.Tests;

public sealed class NativeAggregateBridgeTests
{
    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void SmallMixedAndLargeRecordsCrossByValueWithoutNativeAggregateAbiAssumptions(string language, int optimization)
    {
        Run("#lang " + language + "\n" + """
            struct Small { unsigned char value; };
            struct Mixed { double x; int y; };
            struct Large { long long a, b, c, d, e; };
            struct ConstMember { const int code; double x; };
            struct Small EchoSmall(struct Small value) { return value; }
            struct Mixed Increase(struct Mixed value, int amount) { value.y += amount; return value; }
            long long SumLarge(struct Large value) { return value.a + value.b + value.c + value.d + value.e; }
            struct Large MakeLarge(long long value) { struct Large result = {value, 0, 0, 0, 0}; return result; }
            struct ConstMember MakeConst(void) { struct ConstMember result = {42, 2.5}; return result; }
            #lang gflat
            int main()
            {
                Small small = default(Small);
                small.value = (byte)42;
                Small echoed = EchoSmall(small);
                if (echoed.value != (byte)42) return 1;
                Mixed mixed = default(Mixed);
                mixed.x = 20.5;
                mixed.y = 20;
                Mixed changed = Increase(mixed, 22);
                if (changed.x != 20.5 || changed.y != 42 || mixed.y != 20) return 2;
                Large large = MakeLarge(42l);
                if (SumLarge(large) != 42l) return 3;
                ConstMember constant = MakeConst();
                if (constant.code != 42 || constant.x != 2.5) return 4;
                return changed.y;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeMethodsReturnIndependentRecordsAndThrowWithoutPublishingPartialResults(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                struct Counter {
                    int value;
                    Counter Add(Counter other) const { return Counter{value + other.value}; }
                    Counter Fail() const { throw 7; }
                };
            }
            #lang gflat
            int main()
            {
                Native::Counter first = default(Native::Counter);
                Native::Counter second = default(Native::Counter);
                first.value = 20;
                second.value = 22;
                Native::Counter result = first.Add(second);
                if (result.value != 42 || first.value != 20 || second.value != 22) return 1;
                try { result = first.Fail(); }
                catch { return result.value; }
                return 2;
            }
            """, optimization);
    }

    private static void Run(string code, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-aggregate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new CompilationOptions
            {
                NoStdLib = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(),
                ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
            };
            var program = Compiler.EmitProgram([new SourceFile("aggregate.gf", code)], options: options);
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 42, $"Native aggregate exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
