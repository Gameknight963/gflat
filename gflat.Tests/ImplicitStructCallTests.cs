namespace gflat.Tests;

public class ImplicitStructCallTests
{
    [Theory]
    [InlineData("", "R", "int")]
    [InlineData("namespace Sample {", "R", "int")]
    [InlineData("namespace Sample {", "R<T>", "T")]
    public void UnqualifiedOverloadsPassTheExistingReceiver(string scope, string declaration, string parameter)
    {
        string type=scope.Length==0 ? "R" : "Sample::R";
        if(declaration.Contains('<')) type+="<int>";
        var result=CompilerTestHelper.Run($$"""
            {{scope}}
            struct {{declaration}} {
                private int calls;
                public R() { calls=0; }
                public int Calls { readonly get=>calls; }
                public int Forward({{parameter}} value)=>Add(40,value);
                public int Add(int extra,{{parameter}} value) { calls++; return extra+(int)value; }
                public int Add({{parameter}} value) { calls++; return (int)value; }
                public readonly int Read({{parameter}} value)=>Identity(value);
                public readonly int Identity({{parameter}} value)=>(int)value;
            }
            {{(scope.Length==0 ? "" : "}")}}
            int main() {
                {{type}} instance=new();
                int result=instance.Forward(2);
                if(instance.Calls!=1 || instance.Read(7)!=7) return 1;
                return result;
            }
            """);
        Assert.Equal(42,result.ExitCode);
    }
}
