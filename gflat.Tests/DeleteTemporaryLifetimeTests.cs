namespace gflat.Tests;

public sealed class DeleteTemporaryLifetimeTests
{
    private const string Prelude="""
        extern int putchar(int value);
        private extern void*? malloc(nuint bytes);private extern void free(void* storage);
        void*? watched=null;int childDead=0;int holderDead=0;int freed=0;
        namespace Memory{
            public replace void*? Allocate(nuint bytes)=>malloc(bytes);
            public replace void Free(void* storage){if(storage==watched){freed++;putchar(70);watched=null;}free(storage);}
        }
        struct Child{public int Value;public Child(){Value=0;}~Child(){childDead++;putchar(67);}}
        struct Holder{
            public Child* Target;
            public Holder(Child* target){Target=target;}
            public Child* Get()=>Target;
            public Child*? Nullable()=>Target;
            public Child* Pointer=>Target;
            public Child* Fail(){throw new Exception();}
            public Holder Forward()=>new Holder(Target);
            ~Holder(){holderDead++;putchar(72);}
        }
        Holder Make(Child* target)=>new Holder(target);
        """;

    [Theory]
    [InlineData(0,"Make(target).Get()",1)]
    [InlineData(2,"Make(target).Get()",1)]
    [InlineData(0,"Make(target).Target",1)]
    [InlineData(2,"Make(target).Target",1)]
    [InlineData(0,"Make(target).Pointer",1)]
    [InlineData(2,"Make(target).Pointer",1)]
    [InlineData(0,"Make(target).Nullable()",1)]
    [InlineData(2,"Make(target).Nullable()",1)]
    [InlineData(0,"Make(target).Forward().Get()",2)]
    [InlineData(2,"Make(target).Forward().Get()",2)]
    public void DeleteDestroysAndFreesBeforeItsTemporaryReceivers(int optimization,string expression,int holders)
        => Run(Prelude+"""
            int main(){Child* target=new* Child();watched=(void*)target;
                delete EXPRESSION;
                if(childDead!=1 || holderDead!=HOLDERS || freed!=1)return 1;
                putchar(69);return 0;}
            """.Replace("EXPRESSION",expression).Replace("HOLDERS",holders.ToString()),optimization,"CF"+new string('H',holders)+"E");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingTargetEvaluationCleansReceiverOnceAndLeavesTargetUnfreed(int optimization)
        => Run(Prelude+"""
            int main(){Child* target=new* Child();watched=(void*)target;bool caught=false;
                try{delete Make(target).Fail();}catch{caught=true;putchar(75);}
                if(!caught || holderDead!=1 || childDead!=0 || freed!=0)return 1;
                delete target;if(childDead!=1 || freed!=1 || holderDead!=1)return 2;
                putchar(69);return 0;}
            """,optimization,"HKCFE");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedReferencesInTemporaryReceiverRemainRootedThroughTargetDestruction(int optimization)
        => Run("""
            using std;
            bool bad=false;int holders=0;
            class Node{public int Value;public Node^? Next;public Node(int value){Value=value;}}
            struct Child{public int Value;public Child(){Value=0;}~Child(){GC::Collect();if(TracingCollector::ManagedCount!=1)bad=true;}}
            struct Holder{
                private Node^ root;private Child* target;
                public Holder(Child* value){target=value;root=new^ Node(42);root.Next=root;}
                public Child* Get()=>target;
                ~Holder(){holders++;if(root.Value!=42)bad=true;}
            }
            Holder Make(Child* target)=>new Holder(target);
            int main(){Child* target=new* Child();delete Make(target).Get();
                if(bad || holders!=1)return 1;
                GC::Collect();return (int)TracingCollector::ManagedCount;}
            """,optimization,"",managed:true);

    private static void Run(string program,int optimization,string expected,bool managed=false)
    {
        var sources=new List<SourceFile>();
        if(managed)foreach(string name in new[]{"libc/GC.gf","libc/TracingCollector.gf"})
            sources.Add(new SourceFile("std/"+name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",name))));
        sources.Add(new SourceFile("delete-temporary.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Delete temporary exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        Assert.Equal(expected,result.StandardOutput);
    }
}
