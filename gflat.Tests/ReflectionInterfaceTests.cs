namespace gflat.Tests;

public sealed class ReflectionInterfaceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InterfaceMethodsCanBeInspectedAndDispatchedThroughTheirReference(int optimization)
    {
        string ir = Compiler.Emit("""
            interface IReader { int Read(int offset); }
            struct Counter : IReader {
                public int Value;
                public Counter(int value) { Value = value; }
                public int Read(int offset) => Value + offset;
            }
            int main() {
                const reflection method = reflect(IReader::Read);
                static if (reflect(IReader).Methods.Length != 1 || reflect(IReader).Methods[0] != method) { wrongMethod(); }
                static if (method.ParameterTypes[0] != reflect(int)) { wrongParameter(); }
                Counter counter = new(40);
                IReader* view = &counter;
                return view.[:method:](2) - 42;
            }
            """, options: new() { NoStdLib = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
