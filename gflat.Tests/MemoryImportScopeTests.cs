using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class MemoryImportScopeTests
{
    [Theory]
    [InlineData(0,false)]
    [InlineData(2,false)]
    [InlineData(0,true)]
    [InlineData(2,true)]
    public void GlobalPrivateNativeImportsCoexistWithDefaultAndReplacementHooks(int optimization,bool replace)
    {
        string replacement=replace?"""
            namespace Memory {
                public replace void*? Allocate(nuint size){allocated++;return malloc(size);}
                public replace void Free(void* pointer) nothrow{released++;free(pointer);}
            }
            """:"";
        string program="""
            private extern void*? malloc(nuint bytes);
            private extern void free(void* address);
            int allocated=0;int released=0;
            """+replacement+$$"""
            int main(){
                // These direct and addressed names belong to THIS source file.
                void*? raw=malloc(8);if(raw==null)return 1;
                ((byte*)raw!)[0]=(byte)201;if(((byte*)raw!)[0]!=(byte)201)return 2;free(raw!);
                void*?(nuint)* allocate=&malloc;void(void*)* release=&free;
                raw=allocate(8);if(raw==null)return 3;release(raw!);
                void*? storage=Memory::Allocate(8);if(storage==null)return 4;
                ((byte*)storage!)[7]=(byte)203;if(((byte*)storage!)[7]!=(byte)203)return 5;
                Memory::Free(storage!);
                if(allocated!={{(replace?1:0)}}||released!={{(replace?1:0)}})return 6;
                return 0;
            }
            """;
        SourceFile[] sources=[StandardLibrary.Load("libc/Memory/allocation.gf"),new("private-memory-consumer.gf",program)];
        string ir=Compiler.Emit(sources,options:new(){InternalizeFunctions=true});
        Assert.Equal(1,ir.Split("declare i8* @malloc(").Length-1);
        Assert.Equal(1,ir.Split("declare void @free(").Length-1);
        Assert.DoesNotContain("@Memory$NativeImports$malloc",ir);
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Memory import scope exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void AutomaticMemoryImportsStayPrivateAndCannotLeakThroughQualifiedCallsOrAddresses()
    {
        foreach(string body in new[]{"Memory::NativeImports::malloc(1);","void*?(nuint)* callback=&Memory::NativeImports::malloc;"})
        {
            var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check([new SourceFile("private-memory-access.gf","int main(){"+body+"return 0;}")]));
            Assert.Contains("private to its source file",error.Message);
        }
        string ir=Compiler.Emit([new SourceFile("automatic-memory-coexist.gf","""
            private extern void*? malloc(nuint bytes);private extern void free(void* address);
            int main(){void*? raw=malloc(8);if(raw==null)return 1;free(raw!);
                void*? other=Memory::Allocate(8);if(other==null)return 2;Memory::Free(other!);return 0;}
            """)]);
        Assert.Equal(1,ir.Split("declare i8* @malloc(").Length-1);
        Assert.Equal(1,ir.Split("declare void @free(").Length-1);
    }

    [Fact]
    public void IndependentNativeImportsStillRejectConflictingAbiAndNothrowContracts()
    {
        foreach(var value in new[]{
            ("private extern int malloc(nuint bytes);","incompatible native signatures"),
            ("private extern void*? malloc(nuint bytes) nothrow;","conflicting nothrow contracts"),
            ("private extern void free(int value);","incompatible native signatures")})
        {
            var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit([new SourceFile("conflicting-memory-import.gf",value.Item1+"int main()=>0;")]));
            Assert.Contains(value.Item2,error.Message);
        }
    }
}
