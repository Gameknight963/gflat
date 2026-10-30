namespace gflat.Tests;

public sealed class WideIntegerNativeTests
{
    [Theory]
    [InlineData("extralong Compute(extralong a, extralong b, int shift) => a*b+(a<<shift)-(b>>shift);", false)]
    [InlineData("bool Compute(extralong a, extralong b) => a<b || a==b;", false)]
    [InlineData("extralong Compute(extralong a, extralong b) => a/b;", true)]
    [InlineData("extralong Compute(extralong a, extralong b) => a%b;", true)]
    [InlineData("double Compute(extralong a) => (double)a;", true)]
    [InlineData("float Compute(extralong a) => (float)a;", true)]
    [InlineData("extralong Compute(double a) => (extralong)a;", true)]
    [InlineData("extralong Compute(float a) => (extralong)a;", true)]
    public void OnlyHelperLoweredWideOperationsSelectTheExtraRuntime(string program, bool required)
    {
        string ir=Compiler.Emit(program+" int main()=>0;",options:new(){NoStdLib=true});
        Assert.Equal(required,NativeToolchain.RequiresWideIntegerRuntime(ir));
    }

    [Fact]
    public void PrimitiveStdInterfacesDoNotSelectCompilerRuntimeForOrdinaryPrograms()
    {
        var sources=new[]{"IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Standard/core",name))))
            .Append(new SourceFile("main.gf","using Standard; int main(){int n=42; IHashable* view=&n; return (int)view.GetHashCode();}"));
        string ir=Compiler.Emit(sources);
        Assert.Contains("i128",ir);
        Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RuntimeWideFloatingConversionsLinkTheirMatchingHelpers(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            int main(int argc,char** argv) {
                extralong value=(extralong)argc<<100;
                double precise=(double)value; float compact=(float)value;
                extralong fromDouble=(extralong)precise; extralong fromFloat=(extralong)compact;
                if(fromDouble!=value || fromFloat!=value) return 1;
                return 42;
            }
            """,optimization);
        Assert.Equal(42,result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RuntimeWideDivisionAndRemainderLinkWithoutStdOrExceptionRuntime(int optimization)
    {
        string ir = Compiler.Emit("""
            int main(int argc, char** argv) {
                extralong value = (extralong)argc << 96;
                extralong divisor = (extralong)(argc + 2);
                if (value % divisor != (extralong)1) return 1;
                if (-value % divisor != (extralong)-1) return 2;
                if (value / divisor * divisor + value % divisor != value) return 3;
                return 42;
            }
            """, options: new() { NoStdLib = true });
        Assert.Contains("srem i128", ir);
        Assert.Contains("sdiv i128", ir);
        Assert.DoesNotContain("__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        string root = Path.Combine(Path.GetTempPath(), "gflat_wide_native_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "wide.ll"), executable = Path.Combine(root, "wide.exe");
            File.WriteAllText(source, ir);
            var compilation = NativeToolchain.Compile(source, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(root, true); }
    }
}
