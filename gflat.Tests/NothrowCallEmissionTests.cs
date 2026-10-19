using System.Text.RegularExpressions;

namespace gflat.Tests;

public class NothrowCallEmissionTests
{
    [Fact]
    public void DirectKnownNothrowCallsNeedNoExceptionRuntime()
    {
        string ir=Compiler.Emit("int Pure() nothrow=>42; int main()=>Pure();",options:new(){NoStdLib=true});
        Assert.Contains("@gflat$Pure() nounwind",ir);
        Assert.Contains("call i32 @gflat$Pure()",ir);
        Assert.DoesNotContain("invoke",ir);
        Assert.DoesNotContain("personality",ir);
        Assert.DoesNotContain("@__gflat_eh_",ir);
    }

    [Fact]
    public void ExplicitNothrowConstructionAndCopyingNeedNoExceptionRuntime()
    {
        string ir=Compiler.Emit("""
            struct Counter {
                public int Value;
                public Counter(int value) nothrow { Value=value; }
                public copy Counter(readonly(Counter)* source) nothrow { Value=source.Value; }
            }
            int main(){Counter first=new(21); Counter second=first; return first.Value+second.Value;}
            """,options:new(){NoStdLib=true});
        Assert.DoesNotContain("invoke",ir);
        Assert.DoesNotContain("personality",ir);
        Assert.DoesNotContain("@__gflat_eh_",ir);
    }

    [Fact]
    public void VirtualAndInterfaceContractsUseCallsWhileUntypedPointersRemainConservative()
    {
        string ir=Compiler.Emit("""
            interface I { int Read() nothrow; }
            class Base { public virtual int Read() nothrow=>20; }
            class Child : Base, I { public override int Read() nothrow=>21; }
            int main() {
                Child value=new(); I* viaInterface=&value; Base* viaBase=&value;
                return viaInterface.Read()+viaBase.Read();
            }
            """);
        string body=Regex.Match(ir,@"(?ms)^define .*?@main\([^\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);
        Assert.Equal(2,Regex.Matches(body,@"call i32 %").Count);
        Assert.DoesNotContain("invoke i32",body);

        string unknown=Compiler.Emit("int Pure() nothrow=>42; int main(){int()* pointer=Pure; return pointer();}");
        Assert.Contains("invoke i32 %",unknown);
    }
}
