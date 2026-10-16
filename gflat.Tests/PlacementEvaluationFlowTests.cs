using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class PlacementEvaluationFlowTests
{
    [Theory]
    [InlineData("int", "", "0")]
    [InlineData("Box", "struct Box { public int Value; public Box(int value){Value=value;} }", "new Box(0)")]
    [InlineData("Box", "class Box { public int Value; public Box(int value){Value=value;} }", "new Box(0)")]
    public void DestinationCannotMakeAnEarlierArgumentDereferenceSafe(string type, string declaration, string initial)
    {
        string source = declaration + $$"""
            {{type}}* Pick(int*? ignored,{{type}}* output)=>output;
            int main(){
                int value=42; int*? pointer=null; {{type}} storage={{initial}};
                new* {{type}}(*pointer) at Pick(pointer=&value,&storage);
                return 0;
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("nullable", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("int", "", "0", "storage")]
    [InlineData("Box", "struct Box { public int Value; public Box(int value){Value=value;} }", "new Box(0)", "storage.Value")]
    [InlineData("Box", "class Box { public int Value; public Box(int value){Value=value;} }", "new Box(0)", "storage.Value")]
    public void ArgumentsAreCapturedBeforeDestinationEffects(string type, string declaration, string initial, string result)
        => Assert.Equal(42, CompilerTestHelper.Run(declaration + $$"""
            {{type}}* Clear(ref int*? pointer,{{type}}* output){pointer=null;return output;}
            int main(){
                int value=42; int*? pointer=&value; {{type}} storage={{initial}};
                new* {{type}}(*pointer) at Clear(ref pointer,&storage);
                if(pointer!=null) return 1;
                return {{result}};
            }
            """).ExitCode);

    [Theory]
    [InlineData("int", "", "0", "storage")]
    [InlineData("Box", "struct Box { public int Value; public Box(int value){Value=value;} }", "new Box(0)", "storage.Value")]
    [InlineData("Box", "class Box { public int Value; public Box(int value){Value=value;} }", "new Box(0)", "storage.Value")]
    public void DestinationObservesEarlierArgumentAssignment(string type, string declaration, string initial, string result)
        => Assert.Equal(42, CompilerTestHelper.Run(declaration + $$"""
            int Seed({{type}}*? ignored)=>42;
            int main(){
                {{type}} storage={{initial}}; {{type}}*? destination=null;
                new* {{type}}(Seed(destination=&storage)) at destination;
                return {{result}};
            }
            """).ExitCode);
}
