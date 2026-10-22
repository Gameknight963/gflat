using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StaticInterfaceDeclarationTests
{
    [Fact]
    public void StaticInterfaceMakesMethodsAndPropertyAccessorsStatic()
    {
        var result = CompilerTestHelper.Run("""
            static interface ITransform {
                int Apply(int value);
                int Answer { get; }
            }
            static class Implementation : ITransform {
                public static int Apply(int value) => value + 1;
                public static int Answer => 40;
            }
            int Read<P : ITransform>() => P::Apply(P::Apply(P::Answer));
            int main() => Read<Implementation>();
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void StaticPropertiesMayRequireBothAccessors()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            static interface ISettings { int Value { get; set; } }
            static class Settings : ISettings {
                public static int Value { get => 42; set { if (value != 42) throw new Exception(); } }
            }
            int Read<P : ISettings>() { P::Value = 42; return P::Value; }
            int main() => Read<Settings>();
            """).ExitCode);

    [Fact]
    public void EmptyStaticInterfaceRemainsAStaticContract()
    {
        var (ast, checker) = Compiler.Check("static interface IMarker {} static class Provider : IMarker {}");
        Assert.True(Assert.Single(ast.Members.OfType<InterfaceDeclaration>(), i => i.Name == "IMarker").IsStatic);
        Assert.True(checker.GetInterface("IMarker")!.IsStaticContract);
    }

    [Theory]
    [InlineData("static interface I { immutable void Run(); }", "Readonly methods require an instance receiver")]
    [InlineData("static interface I { void Run() {} }", "cannot have a body")]
    [InlineData("static interface I {} void Use(I* value) {}", "not an instance reference")]
    [InlineData("static interface I { void Run(); } class C : I { public void Run() {} }", "static modifier")]
    public void InvalidStaticContractsHaveSourceDiagnostics(string source, string diagnostic)
        => Assert.Contains(diagnostic, Assert.Throws<TypeCheckException>(() => Compiler.Check(source)).Message);

    [Fact]
    public void StaticAndExplicitStaticMembersUseTheSameContract()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            static interface IValue { static int Read(); }
            struct Provider : IValue { public static int Read() => 42; }
            int Read<P : IValue>() => P::Read();
            int main() => Read<Provider>();
            """).ExitCode);
}
