using gflat.CompileExceptions;

namespace gflat.Tests;

public class TypeofTests
{
    [Fact]
    public void TypeMetadataIsACompileTimeConstantAndWorksInGenericCode()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            struct Player { public int score; public char tag; }
            const nuint width = typeof(Player).Size;
            const nuint alignment = typeof(Player).Alignment;
            const nuint Size<T>() => typeof(T).Size;
            const nuint intWidth = Size<int>();
            int main() {
                std::TypeInfo info = typeof(Player);
                if (info.Size != (nuint)sizeof(Player)) return 1;
                if (width != (nuint)sizeof(Player) || alignment != (nuint)alignof(Player)) return 2;
                if (intWidth != (nuint)4) return 3;
                return 42;
            }
            """).ExitCode);
    }

    [Fact]
    public void NamesResolveAliasesAndGenericNamespacesWithoutMangling()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) text, ...);
            namespace Game { struct Box<T> { public T value; } }
            alias IntBox = Game::Box<int>;
            int main() {
                printf("%s\n", typeof(IntBox).Name);
                printf("%s\n", typeof(readonly(char*)).Name);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Game::Box<int>\nreadonly(char*)\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReferenceTraitsDescribeTheQueriedTypeAndNotItsPointee()
    {
        Assert.Equal(0, CompilerTestHelper.Run("""
            struct Resource { ~Resource() {} }
            int main() {
                if (!typeof(Resource).HasDestructor) return 1;
                if (typeof(Resource*).HasDestructor) return 2;
                if (!typeof(int*?).IsPointer || !typeof(int*?).IsNullable) return 3;
                if (!typeof(int^).IsManaged || typeof(int^).IsPointer) return 4;
                if (!typeof(readonly(int)*).IsReadOnly) return 5;
                return 0;
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("int main() { std::TypeInfo t = typeof(int); t.Size = 8; return 0; }")]
    [InlineData("int main() { std::TypeInfo t = typeof(int); t.Name = \"changed\"; return 0; }")]
    [InlineData("int main() { std::TypeInfo t = typeof(MissingType); return 0; }")]
    public void MetadataCannotBeMutatedAndUnknownTypesAreDiagnosed(string source)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source));
}
