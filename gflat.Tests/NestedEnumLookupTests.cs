namespace gflat.Tests;

public sealed class NestedEnumLookupTests
{
    [Theory]
    [InlineData("class", 0)]
    [InlineData("class", 2)]
    [InlineData("static class", 0)]
    [InlineData("static class", 2)]
    [InlineData("struct", 0)]
    [InlineData("struct", 2)]
    public void NestedEnumsAreRegisteredBeforeMethodSignaturesAndResolveExternally(string owner, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            using Library;
            namespace Library {
                public {{owner}} Codec {
                    public enum Status : byte { Done = 7, Invalid = 9 }
                    public static Status Read(Status value = Status::Done) => value;
                    public static Codec::Status Qualified() => Codec::Status::Invalid;
                }
            }
            int main() {
                Codec::Status local = Codec::Read();
                Library::Codec::Status qualified = Library::Codec::Qualified();
                if (local != Codec::Status::Done || qualified != Library::Codec::Status::Invalid) return 1;
                if (sizeof(Codec::Status) != 1 || (int)local != 7 || (int)qualified != 9) return 2;
                return 0;
            }
            """, optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedEnumSpecializationsRemainDistinctAndDoNotLeakIntoNamespaces(int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Library {
                enum Status : long { Outer = 3 }
                class Codec<T> {
                    public enum Status : byte { Done = 7 }
                    public static Status Read() => Status::Done;
                }
                int Outer() => sizeof(Status);
            }
            int main() {
                Library::Codec<int>::Status first = Library::Codec<int>::Read();
                Library::Codec<byte>::Status second = Library::Codec<byte>::Read();
                if (first != Library::Codec<int>::Status::Done || second != Library::Codec<byte>::Status::Done) return 1;
                if (Library::Outer() != 8 || sizeof(Library::Codec<int>::Status) != 1) return 2;
                return 0;
            }
            """, optimization).ExitCode);
}
