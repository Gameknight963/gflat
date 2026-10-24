using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericCallbackViewTests
{
    private const string Search = """
        struct Bag<T> { }
        bool Search<T>(Bag<T> source, immutable(T)* key,
            bool(immutable(T)*, immutable(T)*)* equal) => equal(key,key);
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ForwardedSingletonFunctionAddressesKeepTheCallersGenericView(int optimization)
    {
        var result = CompilerTestHelper.Run(Search + """
            static class Comparer<T> {
                public static bool Same(immutable(T)* left, immutable(T)* right) => left==right;
            }
            bool Search<T>(Bag<T> source, immutable(T)* key)
                => Search<T>(move source,key,Comparer<T>::Same);
            int main() {
                Bag<int> source=new(); int key=42;
                if(!Search(source,&key))return 1;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QualifiedCallbackInferenceMatchesTheAlreadyBoundPointerType(int optimization)
    {
        var result = CompilerTestHelper.Run(Search + """
            bool Same(immutable(int*)* left, immutable(int*)* right) => **left==**right;
            int main() {
                Bag<int*> source=new(); int value=42; int* key=&value;
                if(!Search(source,&key,&Same))return 1;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void QualifiedCallbackInferenceStillRejectsDifferentPointerElementTypes()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Search + """
            bool Wrong(immutable(long*)* left, immutable(long*)* right) => true;
            int main() {
                Bag<int*> source=new(); int value=42; int* key=&value;
                Search(source,&key,&Wrong);
                return 0;
            }
            """));
        Assert.Contains("No matching overload", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OrdinaryOwnerOverloadPreviewsDoNotRebindSingletonFunctionAddresses(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            class Owner<T> {
                public static bool Use(bool(immutable(T)*)* callback, immutable(T)* key) => callback(key);
                public static bool Use(int tag, immutable(T)* key) => false;
            }
            static class Comparer<T> { public static bool Check(immutable(T)* key) => true; }
            bool Forward<P>(immutable(P)* key) => Owner<P>::Use(Comparer<P>::Check,key);
            int main() { int key=42; if(!Forward<int>(&key))return 1; return 0; }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void DeepQualificationDoesNotMergeDistinctGenericParameterOrigins()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Search + """
            static class Comparer<T> {
                public static bool Same(immutable(T)* left, immutable(T)* right) => true;
            }
            bool Forward<P,Q>(Bag<P> source, immutable(P)* key,
                bool(immutable(Q)*, immutable(Q)*)* equal) => Search(source,key,equal);
            int main() {
                Bag<int*> source=new(); int value=42; int* key=&value;
                Forward<int*,int*>(source,&key,Comparer<int*>::Same);
                return 0;
            }
            """));
        Assert.Contains("No matching overload", error.Message);
    }
}
