using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class DeclarationQualifierTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("int*")]
    [InlineData("int**")]
    [InlineData("int[3]")]
    [InlineData("int(int)*")]
    [InlineData("readonly(int)*")]
    public void DeclarationPrefixesMatchExplicitWholeTypeQualification(string type)
    {
        foreach (string qualifier in new[] { "readonly", "immutable" })
        {
            var unit = Parser.Parse(Lexer.Tokenize($"{qualifier} {type} first; {qualifier}({type}) second;"));
            var fields = unit.Members.OfType<FieldDeclaration>().ToArray();
            Assert.Equal(2, fields.Length);
            Assert.Equal(TypeChecker.TypeName(fields[1].Type), TypeChecker.TypeName(fields[0].Type));
            Assert.True(fields[0].Type.IsReadOnlyValue);
            Assert.Equal(fields[1].Type.IsImmutableValue, fields[0].Type.IsImmutableValue);
        }
    }

    [Theory]
    [InlineData("readonly int number = 1; number = 2;")]
    [InlineData("int n=1; readonly int* p=&n; p=&n;")]
    [InlineData("int n=1; immutable int* p=&n; *p=2;")]
    [InlineData("readonly int[2] values=[1,2]; values[0]=3;")]
    [InlineData("struct S{public readonly int value;public S(){value=1;}public void Change(){value=2;}}")]
    [InlineData("struct S{public readonly int* data;public S(int* p){data=p;}public void Change(int* p){data=p;}}")]
    public void ShorthandPreservesAssignmentAndDeepProtection(string body)
    {
        string source = body.StartsWith("struct", StringComparison.Ordinal) ? body : "void Test(){" + body + "}";
        Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldsAliasesLocalsAndLoopBindingsProtectSlotsWithoutFreezingPointees(int optimization)
    {
        const string source = """
            alias IntPointer = int*;
            readonly int globalValue = 7;
            struct Buffer {
                public readonly IntPointer data;
                public immutable int size;
                public Buffer(int* p){data=p;size=2;}
                public readonly void Write(int value){data[0]=value;}
                public immutable int Size()=>size;
                public readonly(int)* View()=>data;
            }
            int main() {
                int first=1; int second=2;
                readonly int* fixedPointer=&first;
                *fixedPointer=10;
                readonly IntPointer aliasPointer=&first;
                *aliasPointer=11;
                int* mutableSlot=&first;
                readonly int** outer=&mutableSlot;
                *outer=&second;
                **outer=20;
                readonly(int)* protectedPointee=&first;
                protectedPointee=&second;
                Buffer buffer=new(&first);
                buffer.Write(30);
                immutable int copiedSize=buffer.Size();
                int sum=0;
                for(readonly int index=1;sum<1;){sum+=index;}
                int[2] numbers=[2,3];
                foreach(readonly int number in numbers){sum+=number;}
                if(first!=30 || second!=20 || *protectedPointee!=20 || copiedSize!=2 || globalValue!=7 || sum!=6)return 1;
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(source, optimization).ExitCode);
    }

    [Fact]
    public void ReceiverAndReturnQualificationRemainIndependent()
    {
        var unit = Parser.Parse(Lexer.Tokenize("""
            struct Buffer {
                public readonly int Count()=>1;
                public readonly(char)* Data()=>"";
                public readonly readonly(char)* Peek()=>"";
                public readonly int Value { get=>1; }
            }
            """));
        var type = Assert.IsType<StructDeclaration>(Assert.Single(unit.Members));
        var methods = type.Members.OfType<MethodDeclaration>().ToDictionary(m => m.Name);
        Assert.True(methods["Count"].IsReadOnly);
        Assert.False(methods["Count"].ReturnType.IsReadOnlyValue);
        Assert.False(methods["Data"].IsReadOnly);
        Assert.True(Assert.IsType<PointerTypeExpression>(methods["Data"].ReturnType).Inner.IsReadOnlyValue);
        Assert.True(methods["Peek"].IsReadOnly);
        Assert.True(type.Members.OfType<PropertyDeclaration>().Single().Getter!.IsReadOnly);
    }
}
