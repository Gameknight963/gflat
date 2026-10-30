using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class SourceCallableDebugNameTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LoweredHelpersKeepSourceCallStackNamesAndNativeLinkage(int optimization)
    {
        var source=new SourceFile(Path.GetFullPath("source-callable-debug-names.gf"),"""
            using Tools;
            namespace Tools {
                struct Box<T>{public T Value;}
                extension<T> Box<T>{public T Read()=>this.Value;}
                struct Number {
                    public int Value;
                    public Number(int value){Value=value;}
                    public static explicit operator int(Number value)=>value.Value;
                    public static explicit operator Number(int value)=>new Number(value);
                }
                interface IMark {
                    immutable int Mark();
                    implementation int{public immutable int Mark()=>*this;}
                }
                interface IShift {
                    immutable int Shift(int amount);
                    extension implementation int{public immutable int Shift(int amount)=>*this+amount;}
                }
                int Marker<T:IMark>(T value)=>value.Mark();
            }
            int main(){Box<int> box;box.Value=10;Number number=(Number)12;int value=20;return box.Read()+(int)number+value.Shift(0)+Marker(0);}
            """);
        string ir=Compiler.Emit([source],options:new(){DebugSymbols=true,NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        var subprograms=Regex.Matches(ir,"!DISubprogram\\(name: \\\"(?<name>[^\\\"]*)\\\", linkageName: \\\"(?<linkage>[^\\\"]*)\\\"").ToArray();
        Assert.NotEmpty(subprograms);
        string[] names=subprograms.Select(match=>match.Groups["name"].Value).ToArray();
        Assert.Contains("Tools::Read",names);
        Assert.Contains("Tools::Shift",names);
        Assert.Contains("Tools::Mark",names);
        Assert.Contains("Tools::Marker",names);
        Assert.Contains("Tools::Number::Number",names);
        Assert.Contains("Tools::Number::explicit operator int",names);
        Assert.Contains("Tools::Number::explicit operator Tools::Number",names);
        Assert.DoesNotContain(names,name=>name.Contains("$extension$",StringComparison.Ordinal)||name.Contains("$conversion$",StringComparison.Ordinal)||name.Contains("$primitive$",StringComparison.Ordinal));
        // ABI symbols remain unchanged and each lowered function's visible name
        // is independent of its implementation-only linkage name.
        Assert.Contains(subprograms,match=>match.Groups["linkage"].Value.Contains("$extension$",StringComparison.Ordinal)&&match.Groups["name"].Value=="Tools::Read");
        Assert.Contains(subprograms,match=>match.Groups["linkage"].Value.Contains("$conversion$",StringComparison.Ordinal)&&match.Groups["name"].Value.StartsWith("Tools::Number::explicit operator ",StringComparison.Ordinal));
        CompilerTestHelper.RunIr(ir,optimization,expectedExitCode:42);
    }
}
