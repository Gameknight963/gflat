namespace gflat.Tests;

public sealed class GenericBaseBindingReentryTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void SignatureTimeGenericBodiesSeeCompleteOrdinaryOverloadFamilies(bool constrained, int optimization)
    {
        string constraint = constrained ? ": IEqual<T>" : "";
        var result = CompilerTestHelper.Run($$"""
            interface IEqual<T> { immutable bool Equals(immutable(T)* other); }
            struct Probe : IEqual<Probe> {
                public immutable bool Equals(immutable(Probe)* other) => true;
                public immutable bool Equals(readonly(char)* text) => false;
            }
            struct GenericConsumer<T {{constraint}}> {
                public static bool Use(immutable(T)* value) => value.Equals(value);
            }
            void Trigger(GenericConsumer<Probe>* value) { }
            int main() {
                Probe value = new();
                if (!GenericConsumer<Probe>::Use(&value)) return 1;
                return 0;
            }
            """, optimization);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void BaseSpecializationsCanReenterOverloadedParameterBinding(string kind)
    {
        // Seed resolves a nested iterator before IO's parameter creates another
        // specialization. Preparing that iterator's overloads revisits IO.
        Compiler.Check($$"""
            interface I<T, Iterator> { Iterator GetEnumerator(); }
            struct View<T> : I<T, View<T>::Iterator> {
                public struct Iterator { }
                public Iterator GetEnumerator() => new();
            }
            {{kind}} Buffer<T> : I<T, View<T>::Iterator> {
                public View<T>::Iterator GetEnumerator() => new();
            }
            class Seed : I<int, View<int>::Iterator> {
                public View<int>::Iterator GetEnumerator() => new();
            }
            static class IO {
                public static void Write(int value) { }
                public static void Write(ref immutable(Buffer<byte>) value) { }
            }
            int main() => 0;
            """);
    }
}
