namespace gflat.Tests;

public sealed class ExtensionTypeShapeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitReadonlyTypeParameterQualificationIsNotInferredIntoTheSubstitution(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct View<T> {
                public T* data;
                public View(T* storage) { data = storage; }
            }
            void Copy<T>(this View<readonly(T)> source, View<T> destination) {
                T* output = destination.data;
                output[0] = source.data[0];
            }
            void CopyScalar<T>(readonly(T)* source, T* destination) { *destination = *source; }
            int main() {
                readonly(int) source = 42;
                int destination = 0;
                View<readonly(int)> input = new(&source);
                View<int> output = new(&destination);
                input.Copy(output);
                if (destination != 42) return 1;
                destination = 0;
                CopyScalar(&source, &destination);
                return destination;
            }
            """, optimization).ExitCode);

    [Fact]
    public void UnqualifiedTypeParameterStillPreservesReadonlyTypeArgument()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct View<T> {
                public T* data;
                public View(T* storage) { data = storage; }
            }
            View<T> Identity<T>(this View<T> source) => source;
            int main() {
                readonly(int) value = 42;
                View<readonly(int)> input = new(&value);
                View<readonly(int)> output = input.Identity();
                return *output.data;
            }
            """).ExitCode);

    [Theory]
    [InlineData("int(int)", false)]
    [InlineData("int(int)", true)]
    [InlineData("readonly(int(int))", false)]
    [InlineData("readonly(int(int))", true)]
    [InlineData("int(int)*", false)]
    [InlineData("int(int)*", true)]
    [InlineData("int(int)*?", false)]
    [InlineData("int(int)*?", true)]
    public void ExtensionCallsPreserveFunctionSignatureAndPointerTypeArguments(string type, bool direct)
    {
        string call = direct ? "Read<int>(handler, 0)" : "handler.Read(0)";
        Assert.Equal(42, CompilerTestHelper.Run($$"""
            struct Handler<F>{public int value;}
            int Read<T>(this Handler<{{type}}> self,T value)=>42;
            int main(){Handler<{{type}}> handler=new Handler<{{type}}>();return {{call}};}
            """).ExitCode);
    }

    [Theory]
    [InlineData("int(int)", 42)]
    [InlineData("int(int)*", 1)]
    public void InferredTypeArgumentsRetainTheirLayoutMeaning(string type, int expected)
        => Assert.Equal(expected, CompilerTestHelper.Run($$"""
            struct Handler<F>{public int value;}
            int HasValueLayout<F>(this Handler<F> self){if(typeof(F).HasLayout)return 1;return 42;}
            int main(){Handler<{{type}}> handler=new Handler<{{type}}>();return handler.HasValueLayout();}
            """).ExitCode);
}
