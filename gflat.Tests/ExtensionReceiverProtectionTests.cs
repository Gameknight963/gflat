using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExtensionReceiverProtectionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LiteralAndFoldedReceiversUseIndependentMutableTemporaryStorage(int optimization)
    {
        const string text="""
            extension int {
                public int Increment(){(*this)++;return *this;}
                public readonly int Read()=>*this;
                public const readonly int Next()=>*this+1;
            }
            const int Folded()=>41;
            const int savedGlobal=41;
            int main(){
                const int local=41;
                if((41).Increment()!=42)return 1;
                if(Folded().Increment()!=42)return 2;
                if((40).Next()!=41)return 3;
                if(local.Read()!=41||savedGlobal.Read()!=41)return 4;
                return 0;
            }
            """;
        string ir=Compiler.Emit(text,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        CompilerTestHelper.RunIr(ir,optimization,expectedExitCode:0);
    }

    [Theory]
    [InlineData("const int saved=41;int main()=>saved.Increment();")]
    [InlineData("int main(){const int saved=41;return saved.Increment();}")]
    [InlineData("int Probe(readonly(int) saved)=>saved.Increment();")]
    public void ConstStorageDoesNotBecomeAMutableTemporary(string program)
    {
        string text="extension int{public int Increment(){(*this)++;return *this;}}"+program;
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(text,options:new(){NoStdLib=true,NoBuiltins=true}));
    }
}
