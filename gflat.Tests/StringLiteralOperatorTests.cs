using gflat.CompileExceptions;

namespace gflat.Tests;

public class StringLiteralOperatorTests
{
    private const string View = """
        namespace Text {
            class View {
                public readonly(char*) data;
                public ulong length;
                public View(readonly(char)* p, ulong n) { data = p; length = n; }
                public static View operator s""(readonly(char)* p, ulong n) {
                    return new View(p, n);
                }
            }
        }
        """;

    [Theory]
    [InlineData("s\"hello\"", 5, 104)]
    [InlineData("s\"\"", 0, 0)]
    [InlineData("s\"\\n\\t\\r\"", 3, 10)]
    [InlineData("s\"a\\0b\"", 3, 97)]
    [InlineData("s\"\\\"\\\\\"", 2, 34)]
    [InlineData("s\"é\"", 2, 195)]
    public void LiteralBytesAndLength(string literal, int length, int first)
    {
        string source = "using Text; " + View + $" int main() {{ View v = {literal}; if (v.length != {length}ul) return 1; if ((byte)v.data[0] != {first}) return 2; if (v.data[v.length] != '\\0') return 3; return 0; }}";
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void EmbeddedNullDoesNotTruncateData()
        => Assert.Equal(98, CompilerTestHelper.Run("using Text; " + View + " int main() { View v = s\"a\\0b\"; return (int)v.data[2]; }").ExitCode);

    [Fact]
    public void NamespaceQualificationDoesNotNeedUsing()
        => Assert.Equal(4, CompilerTestHelper.Run(View + " int main() { Text::View v = Text::s\"text\"; return (int)v.length; }").ExitCode);

    [Fact]
    public void PrefixIsVisibleInsideItsNamespace()
        => Assert.Equal(4, CompilerTestHelper.Run(View + " namespace Text { int test() { View v = s\"text\"; return (int)v.length; } } int main() { return Text::test(); }").ExitCode);

    [Fact]
    public void ForwardDeclarationsAndIdentifierPrefixesWork()
        => Assert.Equal(3, CompilerTestHelper.Run("int main() { S v = my_2string\"abc\"; return v.n; } struct S { public int n; public S(int value) { n = value; } public static S operator my_2string\"\"(readonly(char)* p, ulong n) { return new S((int)n); } }").ExitCode);

    [Fact]
    public void UnknownUnimportedPrefixIsRejected()
        => Assert.Contains("Unknown string prefix", Assert.Throws<TypeCheckException>(() => Compiler.Check(View + " int main() { Text::View v = s\"text\"; return 0; }")).Message);

    [Fact]
    public void ConflictingImportsRequireQualification()
    {
        string other = "namespace Other { struct OtherView { public static OtherView operator s\"\"(readonly(char)* p, ulong n) { return new OtherView(); } } }";
        Assert.Contains("Ambiguous", Assert.Throws<TypeCheckException>(() => Compiler.Check("using Text; using Other; " + View + other + " int main() { View v = s\"text\"; return 0; }")).Message);
        Compiler.Check("using Text; using Other; " + View + other + " int main() { View v = Text::s\"text\"; return 0; }");
    }

    [Theory]
    [InlineData("public S", "s", "readonly(char)* p, ulong n", "static")]
    [InlineData("private static S", "s", "readonly(char)* p, ulong n", "public static")]
    [InlineData("public static S", "c", "readonly(char)* p, ulong n", "reserved")]
    [InlineData("public static S", "s", "char* p, ulong n", "readonly(char)*")]
    [InlineData("public static S", "s", "readonly(char)* p, int n", "ulong")]
    [InlineData("public static S", "s", "readonly(char)* p", "ulong")]
    [InlineData("public static S", "s", "readonly(char)*? p, ulong n", "readonly(char)*")]
    [InlineData("public const static S", "s", "readonly(char)* p, ulong n", "non-const")]
    public void InvalidDeclarationsAreRejected(string modifiers, string prefix, string parameters, string error)
        => Assert.Contains(error, Assert.Throws<TypeCheckException>(() => Compiler.Check($"struct S {{ {modifiers} operator {prefix}\"\"({parameters}) {{ return new S(); }} }}")).Message);

    [Fact]
    public void ResultMustBeContainingType()
        => Assert.Contains("containing", Assert.Throws<TypeCheckException>(() => Compiler.Check("class S { public static int operator s\"\"(readonly(char)* p, ulong n) { return 1; } }")).Message);

    [Fact]
    public void DuplicatePrefixIsRejected()
        => Assert.Contains("Duplicate string prefix", Assert.Throws<TypeCheckException>(() => Compiler.Check("struct S { public static S operator s\"\"(readonly(char)* p, ulong n) { return new S(); } public static S operator s\"\"(readonly(char)* p, ulong n) { return new S(); } }")).Message);

    [Fact]
    public void ReturnedObjectsAreDestroyedOnce()
    {
        string source = """
            extern int putchar(int c);
            class S {
                public int n;
                public S(int value) { n = value; }
                ~S() { putchar(48 + n); }
                public static S operator s""(readonly(char)* p, ulong n) { return new S((int)n); }
            }
            S make() { return s"ab"; }
            int main() { s"a"; { S value = make(); } return 0; }
            """;
        Assert.Equal("12", CompilerTestHelper.Run(source).StandardOutput);
    }

    [Fact]
    public void PrefixInsideGenericFunctionIsClonedCorrectly()
        => Assert.Equal(3, CompilerTestHelper.Run("using Text; " + View + " int size<T>(T unused) { View v = s\"abc\"; return (int)v.length; } int main() { return size<int>(0); }").ExitCode);

    [Fact]
    public void NestedNamespaceImportsAndQualificationWork()
    {
        string source = """
            using Outer::Inner;
            namespace Outer { namespace Inner {
                struct S {
                    public int length;
                    public S(int n) { length = n; }
                    public static S operator s""(readonly(char)* p, ulong n) { return new S((int)n); }
                }
            } }
            int main() { S a = s"ab"; S b = Outer::Inner::s"c"; return a.length + b.length; }
            """;
        Assert.Equal(3, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void FactoryCanInitializePrivateFieldsWithoutAnImplicitReceiver()
    {
        Assert.Equal(3, CompilerTestHelper.Run("""
            class S {
                private int length;
                public int Length() { return length; }
                public static S operator s""(readonly(char)* p, ulong n) {
                    S value = new S(); value.length = (int)n; return value;
                }
            }
            int main() { S value = s"abc"; return value.Length(); }
            """).ExitCode);
    }

    [Theory]
    [InlineData("return this;", "this")]
    [InlineData("int n = length; return new S();", "length")]
    [InlineData("int n = Length(); return new S();", "explicit receiver")]
    public void FactoryCannotUseAnImplicitInstance(string body, string error)
        => Assert.Contains(error, Assert.Throws<TypeCheckException>(() => Compiler.Check("class S { int length; public int Length() { return length; } public static S operator s\"\"(readonly(char)* p, ulong n) { " + body + " } }")).Message);

    [Fact]
    public void ThrowingFactoryUsesNormalExceptionCleanup()
    {
        Assert.Equal("NC", CompilerTestHelper.Run("""
            extern int putchar(int c);
            class S {
                ~S() { putchar(68); }
                public static S operator s""(readonly(char)* p, ulong n) throws {
                    putchar(78); throw new* Exception();
                }
            }
            int main() { try { S value = s"abc"; } catch (Exception* ex) { putchar(67); } return 0; }
            """).StandardOutput);
    }

    [Fact]
    public void ThrowingFactoryRequiresThrowsOrCatch()
        => Assert.Contains("throws", Assert.Throws<TypeCheckException>(() => Compiler.Check("class S { public static S operator s\"\"(readonly(char)* p, ulong n) throws { throw new* Exception(); } } S make() { return s\"abc\"; }")).Message);

    [Fact]
    public void ShortCircuitOnlyDestroysConstructedTemporaries()
    {
        Assert.Equal("ND", CompilerTestHelper.Run("""
            extern int putchar(int c);
            class S {
                public bool Test() { return true; }
                ~S() { putchar(68); }
                public static S operator s""(readonly(char)* p, ulong n) { putchar(78); return new S(); }
            }
            int main() { false && s"skip".Test(); true && s"make".Test(); return 0; }
            """).StandardOutput);
    }

    [Theory]
    [InlineData("s \"\"", "immediately followed")]
    [InlineData("s\"text\"", "empty quotes")]
    public void OperatorMarkerMustBeAdjacentAndEmpty(string marker, string error)
        => Assert.Contains(error, Assert.Throws<TypeCheckException>(() => Compiler.Check("struct S { public static S operator " + marker + "(readonly(char)* p, ulong n) { return new S(); } }")).Message);

    [Fact]
    public void GenericOwnerIsRejectedExplicitly()
        => Assert.Contains("non-generic", Assert.Throws<TypeCheckException>(() => Compiler.Check("class S<T> { public static S<T> operator s\"\"(readonly(char)* p, ulong n) { return new S<T>(); } }")).Message);

    [Fact]
    public void CustomInterpolationRemainsUnsupported()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("using Text; " + View + " int main() { View v = s$\"text{1}\"; return 0; }"));
}
