using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StructMemberDefaultTests
{
    [Fact]
    public void StructMembersArePublicAndNestedClassesKeepPrivateDefaults()
        => Compiler.Check("struct Data { int Value; int Read() => Value; class Inner { public int Value; } } int main() { Data value = new(); value.Value = 42; return value.Read(); }",
            options: new() { NoStdLib = true, NoBuiltins = true });

    [Theory]
    [InlineData("class Data { int Value; }")]
    [InlineData("struct Data { private int Value; }")]
    public void ClassDefaultsAndExplicitPrivacyRemainPrivate(string declaration)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(declaration + " int main() { Data value = new(); return value.Value; }",
            options: new() { NoStdLib = true, NoBuiltins = true }));
}
