using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExtensionBlockTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedReceiversMutateOriginalStorageAndCleanTemporariesWithoutCopying(int optimization)
    {
        var result = Run("""
            int destroyed=0; int evaluations=0;
            struct Owner {
                public int value;
                public Owner(int number) { value=number; }
                ~Owner(){destroyed++;}
            }
            extension Owner {
                public void Increment(){this.value++;}
                public immutable int Read()=>this.value;
            }
            extension int {
                public immutable int Next()=>*this+1;
            }
            Owner* Once(Owner* item){evaluations++;return item;}
            int main(){
                {
                    Owner item=new(40);
                    item.Increment(); Once(&item).Increment();
                    if(item.Read()!=42||evaluations!=1||destroyed!=0)return 1;
                    int temporary = new Owner(7).Read();
                    if(temporary!=7||destroyed!=1)return 2;
                    if(41.Next()!=42)return 3;
                }
                return destroyed-2;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericBlocksBorrowWithoutCopyingAndPreserveMethodTypeParameters(int optimization)
    {
        var result = Run("""
            struct Box<T>{public T value;public Box(T item){value=move item;}}
            extension<T> Box<T> {
                public immutable T Read()=>this.value;
                public U Apply<U>(U(T)* transform)=>transform(this.value);
            }
            long Twice(int value)=>(long)value*2;
            int main(){Box<int> item=new(21);if(item.Read()!=21)return 1;return (int)item.Apply<long>(Twice)-42;}
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("void Old(this int value){}", "extension block")]
    [InlineData("extension int* { public void Bad(){} }", "underlying object")]
    [InlineData("extension int** { public void Bad(){} }", "underlying object")]
    [InlineData("alias P=int*;extension P {public void Bad(){}}", "underlying object")]
    public void RemovedParameterSyntaxAndPointerTargetsAreRejected(string code,string message)
        => Assert.Contains(message, Assert.Throws<TypeCheckException>(() => Compiler.Check(code,
            options: new(){NoStdLib=true,NoBuiltins=true})).Message);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImplementationOptInAndOrdinaryFunctionsHaveIndependentDeclarations(int optimization)
    {
        var result = Run("""
            interface IEqual<T> {
                immutable bool Equal(T other);
                extension implementation int : IEqual<int> {
                    public immutable bool Equal(int other)=>*this==other;
                }
            }
            int Next(int value)=>value+2;
            extension int {public immutable int Next()=>*this+1;}
            int main(){int n=40;if(n.Next()!=41||Next(n)!=42||!n.Equal(40))return 1;return 0;}
            """, optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Fact]
    public void PlainImplementationsRemainHiddenFromOrdinaryMemberLookup()
        => Assert.Throws<TypeCheckException>(()=>Compiler.Check("""
            interface I {int Read();implementation int {public int Read()=>*this;}}
            int main(){int n=42;return n.Read();}
            """,options:new(){NoStdLib=true,NoBuiltins=true}));

    [Fact]
    public void BorrowedOverloadsCannotDifferOnlyByReceiverProtection()
        => Assert.Contains("cannot differ only", Assert.Throws<TypeCheckException>(()=>Compiler.Check("""
            extension int {public int Read()=>*this;public readonly int Read()=>*this;}
            """,options:new(){NoStdLib=true,NoBuiltins=true})).Message);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedOverloadFamiliesSelectArgumentsWithoutJoiningOrdinaryFunctions(int optimization)
    {
        var result=Run("""
            int Read(int value)=>value+100;
            extension int {
                public readonly int Read(int extra)=>*this+extra;
                public readonly int Read(long extra)=>*this+(int)extra+1;
            }
            int main(){int value=20;if(value.Read(2)!=22||value.Read(2l)!=23||Read(value)!=120)return 1;return 0;}
            """,optimization);
        Assert.Equal(0,result.ExitCode);
    }

    private static ExecutionResult Run(string source, int optimization)
        => CompilerTestHelper.RunIr(Compiler.Emit(source, options: new(){InternalizeFunctions=true}), optimization, expectedExitCode:0);
}
