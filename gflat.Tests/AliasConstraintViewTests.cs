using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AliasConstraintViewTests
{
    private const string Providers = """
        interface IProvider { static int Read(); }
        interface IExtra { static int Extra(); }
        static class Answer : IProvider, IExtra
        {
            public static int Read() => 42;
            public static int Extra() => 7;
        }
        int Extra<P : IExtra>() => P::Extra();
        """;

    [Fact]
    public void ColonValidatesWithoutHidingMembersOrInterfaces()
    {
        Compiler.Check(Providers + """
            alias Selected : IProvider = Answer;
            int main() => Selected::Extra() + Extra<Selected>();
            """, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("Selected::Extra()")]
    [InlineData("Extra<Selected>()")]
    public void ExplicitConstrainedAliasHidesAdditionalCapabilities(string expression)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + $$"""
            constrained alias Selected : IProvider = Answer;
            int main() => {{expression}};
            """, options: new() { NoStdLib = true }));
        Assert.True(error.Message.Contains("not exposed") || error.Message.Contains("does not satisfy"), error.Message);
    }

    [Theory]
    [InlineData("alias Selected : IProvider = Wrong;")]
    [InlineData("constrained alias Selected : IProvider = Wrong;")]
    [InlineData("int F() { alias Selected : IProvider = Wrong; return 0; }")]
    [InlineData("int F() { constrained alias Selected : IProvider = Wrong; return 0; }")]
    public void EveryColonAliasValidatesEvenWhenUnused(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + "static class Wrong {}" + declaration,
            options: new() { NoStdLib = true }));
        Assert.Contains("does not satisfy", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("constrained ")]
    public void ConstrainedWithoutConstraintsRemainsTransparent(string modifier)
    {
        Compiler.Check(Providers + $$"""
            {{modifier}}alias Selected = Answer;
            int main()
            {
                {{modifier}}alias Local = Answer;
                return Selected::Extra() + Local::Extra() + Extra<Selected>();
            }
            """, options: new() { NoStdLib = true });
    }

    [Fact]
    public void LocalValidationUsesLexicalConstraintAliases()
    {
        Compiler.Check(Providers + """
            int main()
            {
                alias Contract = IProvider;
                constrained alias Selected : Contract = Answer;
                return Selected::Read();
            }
            """, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("")]
    [InlineData("constrained ")]
    public void LocalAliasesChooseTheirViewExplicitly(string modifier)
    {
        string source = Providers + $$"""
            int main()
            {
                {{modifier}}alias Selected : IProvider = Answer;
                return Selected::Extra();
            }
            """;
        if (modifier.Length == 0) Compiler.Check(source, options: new() { NoStdLib = true });
        else
        {
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
            Assert.Contains("not exposed", error.Message);
        }
    }

    [Theory]
    [InlineData("weak", " = Answer")]
    [InlineData("expect", "")]
    public void ReplacementsInheritTransparentValidationContract(string kind, string initializer)
    {
        Compiler.Check(Providers + $$"""
            {{kind}} alias Selected : IProvider{{initializer}};
            replace alias Selected = Answer;
            int main() => Selected::Extra();
            """, options: new() { NoStdLib = true });
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + $$"""
            static class Wrong {}
            {{kind}} alias Selected : IProvider{{initializer}};
            replace alias Selected = Wrong;
            """, options: new() { NoStdLib = true }));
        Assert.Contains("does not satisfy", error.Message);
    }

    [Theory]
    [InlineData("weak", " = Answer")]
    [InlineData("expect", "")]
    public void ReplacementsInheritConstrainedView(string kind, string initializer)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + $$"""
            {{kind}} constrained alias Selected : IProvider{{initializer}};
            replace alias Selected = Answer;
            int main() => Selected::Extra();
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData("replace alias Selected : IProvider = Answer;", "cannot redeclare constraints")]
    [InlineData("replace constrained alias Selected = Answer;", "cannot declare constrained")]
    public void ReplacementCannotChangeItsInheritedContract(string replacement, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + "weak alias Selected : IProvider = Answer;" + replacement,
            options: new() { NoStdLib = true }));
        Assert.Contains(diagnostic, error.Message);
    }

    [Fact]
    public void GenericLocalAliasKeepsItsConstrainedFlagDuringCloning()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + """
            int Forward<P>()
            {
                constrained alias Selected : IProvider = P;
                return Selected::Extra();
            }
            int main() => Forward<Answer>();
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void ModifierIsContextualAndMayPrecedeOtherAliasModifiers()
    {
        var ast = Parser.Parse(Lexer.Tokenize("constrained public weak alias Selected = int; int constrained() => 42;"));
        var alias = Assert.IsType<AliasDeclaration>(ast.Members[0]);
        Assert.True(alias.IsConstrained);
        Assert.Equal(FunctionImplementationKind.Weak, alias.ImplementationKind);
        Assert.IsType<MethodDeclaration>(ast.Members[1]);
    }
}
