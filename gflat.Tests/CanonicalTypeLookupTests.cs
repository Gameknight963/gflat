using System.Reflection;
using gflat.comptime;

namespace gflat.Tests;

public sealed class CanonicalTypeLookupTests
{
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void SemanticIdentitiesRemainStableInsideAContainingTypeWithTheSameShortName(string kind)
    {
        var (_, checker) = Compiler.Check($$"""
            {{kind}} Word { public int globalField; }
            class Owner { public {{kind}} Word { public long nestedField; } }
            """, options: new() { NoStdLib = true });
        var owner = checker.GetClassByIdentity("Owner")!;
        var context = typeof(TypeChecker).GetField("_currentClass", BindingFlags.NonPublic | BindingFlags.Instance)!;
        context.SetValue(checker, owner);
        try
        {
            if (kind == "struct")
            {
                Assert.Equal("Owner.Word", checker.GetStruct("Word")!.Name);
                Assert.Equal("Word", checker.GetStructByIdentity("Word")!.Name);
                Assert.Equal("Owner.Word", checker.GetStructByIdentity("Owner.Word")!.Name);
                Assert.Same(checker.GetStructByIdentity("Word"), ((IConstEvaluationContext)checker).GetStruct("Word"));
            }
            else
            {
                Assert.Equal("Owner.Word", checker.GetClass("Word")!.Name);
                Assert.Equal("Word", checker.GetClassByIdentity("Word")!.Name);
                Assert.Equal("Owner.Word", checker.GetClassByIdentity("Owner.Word")!.Name);
                Assert.True(checker.IsClassByIdentity("Word"));
                Assert.Same(checker.GetClassByIdentity("Word"), ((IConstEvaluationContext)checker).GetClass("Word"));
            }
        }
        finally { context.SetValue(checker, null); }
    }

    [Fact]
    public void IdentityQueriesDoNotInterpretSourceNamespaceSpelling()
    {
        var (_, checker) = Compiler.Check("namespace N { struct Word {} class Object {} }", options: new() { NoStdLib = true });
        Assert.Same(checker.GetStruct("N::Word"), checker.GetStructByIdentity("N$Word"));
        Assert.Same(checker.GetClass("N::Object"), checker.GetClassByIdentity("N$Object"));
        Assert.Null(checker.GetStructByIdentity("N::Word"));
        Assert.Null(checker.GetClassByIdentity("N::Object"));
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void SourceStaticLookupStillChoosesTheVisibleNestedType(string kind)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            {{kind}} Word { public static int Code()=>1; }
            class Owner {
                public {{kind}} Word { public static int Code()=>2; }
                public static int Read()=>Word::Code();
            }
            int main()=>Owner::Read()-2;
            """).ExitCode);
}
