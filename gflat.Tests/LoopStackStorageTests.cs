using System.Text.RegularExpressions;

namespace gflat.Tests;

public class LoopStackStorageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RepeatedShortCircuitAndLocalStorageDoNotGrowTheStack(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            bool More(int n)=>n<1500000;
            int main() {
                int sum=0;
                for(int i=0;i<1500000;i++) {
                    int[4] values=[i,1,2,3];
                    if((More(values[0]) && values[1]==1) || values[2]==9) sum++;
                }
                return sum-1500000;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FixedAllocationsDominateAllLoopAndCleanupBlocks()
    {
        string ir=Compiler.Emit("""
            struct Resource { public int value; public Resource(int n) {value=n;} ~Resource() {} }
            bool Use(Resource value)=>value.value==2;
            int main() {
                for(int i=0;i<2;i++) {
                    int local=i;
                    if(local==0 || Use(new Resource(2))) local++;
                }
                return 0;
            }
            """);
        foreach(Match function in Regex.Matches(ir,@"(?ms)^define .*?\{\r?\n(?<body>.*?)^\}"))
        {
            bool entry=true;
            foreach(string line in function.Groups["body"].Value.Split('\n'))
            {
                if(Regex.IsMatch(line,@"^\S+:")) entry=line.Trim()=="entry:";
                if(line.Contains(" = alloca ")) Assert.True(entry, $"Allocation outside entry block: {line}");
            }
        }
    }
}
