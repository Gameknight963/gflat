using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class CallEvaluationFlowTests
{
    [Theory]
    [InlineData("int Pass(int value)=>value; int main(){ int(int)*? callback=null; return callback((callback=Pass)!(42)); }")]
    [InlineData("struct Box { public int Read(int value)=>value; } int main(){ Box value=new Box(); Box*? box=null; return box.Read((box=&value)!.Read(42)); }")]
    [InlineData("int Pass(int value)=>value; int(int)* Create(ref int*? pointer){pointer=null;return Pass;} int main(){int value=42;int*? pointer=&value;return Create(ref pointer)(*pointer);}")]
    public void ArgumentEffectsCannotRetroactivelyMakeTheCalleeOrArgumentsSafe(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("nullable", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FunctionPointerIsCapturedBeforeALaterArgumentClearsItsSlot()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Pass(int value)=>value;
            int Clear(ref int(int)*? callback){callback=null;return 42;}
            int main(){int(int)*? callback=Pass;return callback(Clear(ref callback));}
            """).ExitCode);

    [Fact]
    public void MethodReceiverIsCapturedBeforeALaterArgumentClearsItsSlot()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Box { public int Read(int value)=>value; }
            int Clear(ref Box*? pointer){pointer=null;return 42;}
            int main(){Box value=new Box();Box*? pointer=&value;return pointer.Read(Clear(ref pointer));}
            """).ExitCode);

    [Fact]
    public void PropertyFunctionPointerIsEvaluatedBeforeItsArguments()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Pass(int value)=>value;
            struct Box { public int(int)* Callback => Pass; }
            int Clear(ref Box*? pointer){pointer=null;return 42;}
            int main(){Box value=new Box();Box*? pointer=&value;return pointer.Callback(Clear(ref pointer));}
            """).ExitCode);

    [Fact]
    public void FunctionPointerFieldIsCapturedBeforeItsReceiverSlotIsCleared()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Pass(int value)=>value;
            struct Box { public int(int)* Callback; public Box(){Callback=Pass;} }
            int Clear(ref Box*? pointer){pointer=null;return 42;}
            int main(){Box value=new Box();Box*? pointer=&value;return pointer.Callback(Clear(ref pointer));}
            """).ExitCode);

    [Fact]
    public void ReadonlyMethodReceiverRetainsItsCapturedView()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Box { public readonly int Read(int value)=>value; }
            int Clear(ref readonly(Box)*? pointer){pointer=null;return 42;}
            int main(){Box value=new Box();readonly(Box)*? pointer=&value;return pointer.Read(Clear(ref pointer));}
            """).ExitCode);

    [Fact]
    public void MethodReturningAFunctionPointerIsStillADirectMethodCall()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Pass(int value)=>value;
            struct Box { public int(int)* Create()=>Pass; }
            int main(){Box value=new Box();return value.Create()(42);}
            """).ExitCode);

    [Fact]
    public void ExtensionReceiverAndArgumentsExecuteOnceInSourceOrder()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Box {}
            Box Create(){putchar(67);return new Box();}
            int First(){putchar(65);return 20;}
            int Second(){putchar(66);return 22;}
            int Read(this Box value,int first,int second)=>first+second;
            int main()=>Create().Read(First(),Second());
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("CAB", result.StandardOutput);
    }

    [Fact]
    public void RemovedArrowSyntaxStillProducesASourceDiagnostic()
        => Assert.Contains("no longer supported", Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Box { public int Read()=>42; }
            int main(){Box value=new Box();Box* pointer=&value;return pointer->Read();}
            """)).Description);

    [Fact]
    public void LaterArgumentsSeeEarlierArgumentAssignments()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Sum(int first,int second)=>first+second;
            int main(){int value=21;int*? pointer=null;return Sum(*(pointer=&value)!,*pointer);}
            """).ExitCode);

    [Fact]
    public void LaterArgumentsSeeEarlierArgumentInvalidation()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            int Sum(int first,int second)=>first+second;
            int Clear(ref int*? pointer){pointer=null;return 21;}
            int main(){int value=21;int*? pointer=&value;return Sum(Clear(ref pointer),*pointer);}
            """));
}
