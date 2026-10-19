namespace gflat.Tests;

public sealed class NoBuiltinsTests
{
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void OptimizerPolicyIsExplicitAndIndependentOfStandardLibrarySelection(bool noStdLib,bool noBuiltins)
    {
        Assert.False(new CompilationOptions().NoBuiltins);
        string ir=Compiler.Emit("int main()=>0;",options:new(){NoStdLib=noStdLib,NoBuiltins=noBuiltins});
        string[] definitions=Definitions(ir);
        Assert.NotEmpty(definitions);
        Assert.All(definitions,line=>Assert.Equal(noBuiltins,line.Contains("\"no-builtins\"",StringComparison.Ordinal)));
        Assert.All(ir.Split('\n').Where(line=>line.StartsWith("declare ")),line=>Assert.DoesNotContain("\"no-builtins\"",line));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GeneratedMethodsLambdasThunksAndLlvmBodyHelpersCarryPolicy(int optimization)
    {
        string ir=Compiler.Emit("""
            interface IRead { int Read(); }
            class Base : IRead {
                public Base() {}
                public virtual int Read()=>20;
                ~Base() {}
            }
            class Derived : Base {
                public Derived() {}
                public override int Read()=>41;
            }
            int InitialValue()=>1;
            int seed=InitialValue();
            int main() {
                Derived item=new();Base* pointer=&item;IRead* erased=pointer;
                int()* callback=()=>__llvm<int>("ret i32 1");
                return erased.Read()+callback()+seed-1;
            }
            """,options:new(){NoBuiltins=true,DebugSymbols=true,InternalizeFunctions=true});
        string[] definitions=Definitions(ir);
        Assert.Contains(definitions,line=>line.Contains("interface_thunk",StringComparison.Ordinal));
        Assert.Contains(definitions,line=>line.Contains("interface_destroy",StringComparison.Ordinal));
        Assert.Contains(definitions,line=>line.Contains("gflat$llvm$",StringComparison.Ordinal));
        Assert.Contains(definitions,line=>line.Contains("static_init",StringComparison.Ordinal));
        Assert.All(definitions,line=>Assert.Contains("\"no-builtins\"",line));
        string directory=Path.Combine(Path.GetTempPath(),"gflat-no-builtins-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"input.ll"),output=Path.Combine(directory,"test.exe");
            File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            Assert.Equal(42,NativeToolchain.Run(output,[]).ExitCode);
        }
        finally {Directory.Delete(directory,true);}
    }

    [Fact]
    public void ArbitraryLlvmModuleDefinitionsRemainUnderTheirAuthorsControl()
    {
        string ir=Compiler.Emit("""
            int main()=>__llvm_module<int>("define i32 @entry() { ret i32 42 }","entry");
            """,options:new(){NoStdLib=true,NoBuiltins=true});
        string module=Assert.Single(Definitions(ir),line=>line.Contains("$entry(",StringComparison.Ordinal));
        Assert.DoesNotContain("\"no-builtins\"",module);
        Assert.Contains(Definitions(ir),line=>line.StartsWith("define i32 @main(",StringComparison.Ordinal)&&line.Contains("\"no-builtins\"",StringComparison.Ordinal));
    }

    private static string[] Definitions(string ir)=>ir.Split('\n').Where(line=>line.StartsWith("define ",StringComparison.Ordinal)).ToArray();
}
