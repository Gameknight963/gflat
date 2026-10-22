using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ParameterOwnerDelimiterParserTests
{
    [Theory]
    [InlineData("public void Pending(int")]
    [InlineData("public void Pending(")]
    [InlineData("public C(int")]
    [InlineData("public static C operator +(C")]
    [InlineData("private extern int Pending(int")]
    public void ParameterRecoveryLeavesClassClosingBraceForItsOwner(string pending)
    {
        string text=$"class C{{ public int Read(){{return 42;}} {pending}\n}}\nint Healthy(){{return 7;}}";
        var diagnostics=new DiagnosticBag();
        var unit=Parser.Parse(Lexer.Tokenize(text),diagnostics,recoverErrors:true);
        Assert.True(diagnostics.HasErrors);
        Assert.Single(unit.Members.OfType<ClassDeclaration>());
        Assert.Equal("Healthy",Assert.Single(unit.Members.OfType<MethodDeclaration>()).Name);
    }

    [Theory]
    [InlineData("extern int Pending(int;")]
    [InlineData("int Pending(int;")]
    public void ParameterRecoveryLeavesSemicolonForItsDeclaration(string pending)
    {
        var diagnostics=new DiagnosticBag();
        var unit=Parser.Parse(Lexer.Tokenize(pending+"\nint Healthy(){return 7;}"),diagnostics,recoverErrors:true);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(unit.Members.OfType<MethodDeclaration>(),method=>method.Name=="Healthy");
    }
}
