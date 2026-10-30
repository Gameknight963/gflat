namespace gflat.Tests;

public sealed class ArrayValueConversionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReturnedArraysDecayForInitializersCastsAndArgumentsOnce(int optimization)
        => Run("""
            int calls = 0;
            byte[2] Make() { calls++; return [(byte)20, (byte)22]; }
            int[2] Words() { calls++; return [20, 22]; }
            int Sum(readonly(byte)* values) => (int)values[0] + (int)values[1];
            int main() {
                byte* pointer = Make();
                if (pointer.reinterpret<nuint>() == 0 || calls != 1) return 1;
                if ((((byte*)Make()).reinterpret<readonly(char)*>())[1] != (char)22 || calls != 2) return 2;
                if ((((int*)Words()).reinterpret<byte*>().reinterpret<int*>())[1] != 22 || calls != 3) return 3;
                if (Sum(Make()) != 42 || calls != 4) return 4;
                if (Sum((readonly(byte)*)Make()) != 42 || calls != 5) return 5;
                int[2] local = [20, 22];
                byte* localBytes = ((int*)local).reinterpret<byte*>();
                if ((localBytes.reinterpret<int*>())[1] != 22) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QualifiedStaticArraysCopyReturnAndPassByValue(int optimization)
        => Run("""
            namespace Values { int[2] data = [20, 22]; }
            int[2] Snapshot() => Values::data;
            int Sum(int[2] values) { values[0] = 0; return values[1]; }
            int main() {
                int[2] copy = Values::data;
                copy[0] = 1;
                if (Values::data[0] != 20 || copy[1] != 22) return 1;
                int[2] returned = Snapshot();
                returned[1] = 2;
                if (Values::data[1] != 22 || returned[0] != 20) return 2;
                if (Sum(Values::data) != 22 || Values::data[0] != 20) return 3;
                return 0;
            }
            """, optimization);

    public static IEnumerable<object[]> GuardedReads()
    {
        foreach (int optimization in new[] { 0, 2 })
        foreach (bool qualified in new[] { false, true })
        foreach (string operation in new[] { "copy", "return", "pointer-argument", "array-argument", "pointer-initializer", "cast" })
            yield return [optimization, qualified, operation];
    }

    [Theory]
    [MemberData(nameof(GuardedReads))]
    public void ArrayValueReadsRetainStaticInitializationGuards(int optimization, bool qualified, string operation)
    {
        string array = qualified ? "Values::second" : "second";
        string read = operation switch {
            "copy" => $"int*[1] copy = {array}; return copy[0];",
            "return" => "return Snapshot()[0];",
            "pointer-argument" => $"return PointerFirst({array});",
            "array-argument" => $"return ArrayFirst({array});",
            "pointer-initializer" => $"int** pointer = {array}; return pointer[0];",
            _ => $"int** pointer = (int**){array}; return pointer[0];"
        };
        string storage = qualified ? "namespace Values { int*[1] second = Make(); }" : "int*[1] second = Make();";
        var result = CompilerTestHelper.Run([new SourceFile("array-startup.gf", $$"""
            int storage = 42;
            int* first = ReadSecond();
            {{storage}}
            int*[1] Make() => [&storage];
            int*[1] Snapshot() => {{array}};
            int* PointerFirst(int** values) => values[0];
            int* ArrayFirst(int*[1] values) => values[0];
            int* ReadSecond() { {{read}} }
            int main() => 0;
            """)], optimization);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TakingStaticArrayAddressBeforeInitializationDoesNotReadIt(int optimization)
        => Run("""
            int storage = 42;
            void* address = Address();
            int*[1] second = Make();
            void* Address() => (void*)&second;
            int*[1] Make() => [&storage];
            int main() { if (address != (void*)&second) return 1; return 0; }
            """, optimization);

    private static void Run(string source, int optimization)
    {
        var result = CompilerTestHelper.Run([new SourceFile("array-conversions.gf", source)], optimization);
        Assert.True(result.ExitCode == 0, $"Array conversion exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
