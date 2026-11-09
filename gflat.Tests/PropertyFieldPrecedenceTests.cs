namespace gflat.Tests;

public sealed class PropertyFieldPrecedenceTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void UnqualifiedFieldsTakePrecedenceOverNamespaceProperties(string kind)
    {
        var result = CompilerTestHelper.Run($$"""
            int reads = 0;
            int writes = 0;
            int Value
            {
                get { reads++; return 100; }
                set { writes++; }
            }
            {{kind}} Reader
            {
                private int Value;
                public Reader(int initial) { Value = initial; }
                public int Read() => Value;
                public int* Address() => &Value;
            }
            int main()
            {
                Reader reader = new(42);
                if (reader.Read() != 42) return 1;
                if (*reader.Address() != 42) return 5;
                if (reads != 0 || writes != 0) return 2;
                if (global::Value != 100 || reads != 1) return 3;
                global::Value = 7;
                if (writes != 1) return 4;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }
}
