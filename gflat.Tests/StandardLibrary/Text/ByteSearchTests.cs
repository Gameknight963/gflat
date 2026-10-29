using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class ByteSearchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExhaustiveSmallBinaryInputsMatchIndependentNaiveSearch(int optimization)
        => Run("""
            using std; using std::Text; using std::Collections;
            int main() {
                byte[10] input; byte[8] pattern;
                for(nuint length=0;length<=8;length++) {
                    for(uint bits=0;bits<((uint)1<<(uint)length);bits++) {
                        input[0]=(byte)255;input[length+1]=(byte)255;
                        for(nuint i=0;i<length;i++) input[i+1]=(byte)((bits>>(uint)i)&1u);
                        for(nuint count=0;count<=6;count++) {
                            for(uint key=0;key<((uint)1<<(uint)count);key++) {
                                for(nuint i=0;i<count;i++) pattern[i]=(byte)((key>>(uint)i)&1u);
                                nuint first=length; nuint last=length;
                                if(count==0) first=0;
                                else if(count<=length) {
                                    for(nuint i=0;i<=length-count;i++) {
                                        bool same=true;
                                        for(nuint j=0;j<count;j++) if(input[i+j+1]!=pattern[j]) same=false;
                                        if(same) {if(first==length)first=i;last=i;}
                                    }
                                }
                                if(ByteSearch::IndexOf(&input[1],length,(byte*)pattern,count)!=first)return 1;
                                if(ByteSearch::LastIndexOf(&input[1],length,(byte*)pattern,count)!=last)return 2;
                                if(input[0]!=(byte)255||input[length+1]!=(byte)255)return 3;
                            }
                        }
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RandomBinarySlicesAndEveryByteMatchDotNetSpanOracle(int optimization)
    {
        var random=new Random(71931);
        byte[] input=new byte[512];random.NextBytes(input);
        for(int i=0;i<256;i++)input[i]=(byte)i;
        var code=new StringBuilder("using std; using std::Text; using std::Collections;int main(){byte[512] input;byte[96] pattern;\n");
        for(int i=0;i<input.Length;i++)code.AppendLine($"input[{i}]=(byte){input[i]};");
        for(int trial=0;trial<80;trial++)
        {
            int start=random.Next(input.Length+1),length=random.Next(input.Length-start+1);
            int count=random.Next(97);byte[] pattern=new byte[count];random.NextBytes(pattern);
            if(trial%2==0 && count<=length)input.AsSpan(start+random.Next(length-count+1),count).CopyTo(pattern);
            for(int i=0;i<count;i++)code.AppendLine($"pattern[{i}]=(byte){pattern[i]};");
            int first=input.AsSpan(start,length).IndexOf(pattern),last=input.AsSpan(start,length).LastIndexOf(pattern);
            if(first<0)first=length;if(last<0)last=length;
            code.AppendLine($"if(ByteSearch::IndexOf((byte*)input+{start},{length},(byte*)pattern,{count})!={first})return 1;");
            code.AppendLine($"if(ByteSearch::LastIndexOf((byte*)input+{start},{length},(byte*)pattern,{count})!={last})return 2;");
        }
        code.AppendLine("return 0;}");
        Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LongPeriodicPatternsAndRegionLimitsHaveDefinedResults(int optimization)
        => Run("""
            using std; using std::Text; using std::Collections;
            int main() {
                byte[262144] input;byte[8192] pattern;
                for(nuint i=0;i<262144;i++)input[i]=(byte)97;
                for(nuint i=0;i<8192;i++)pattern[i]=(byte)97;
                pattern[4096]=(byte)98;
                if(ByteSearch::IndexOf((byte*)input,262144,(byte*)pattern,8192)!=262144)return 1;
                if(ByteSearch::LastIndexOf((byte*)input,262144,(byte*)pattern,8192)!=262144)return 2;
                input[131072]=(byte)98;
                if(ByteSearch::IndexOf((byte*)input,262144,(byte*)pattern,8192)!=126976)return 3;
                if(ByteSearch::LastIndexOf((byte*)input,262144,(byte*)pattern,8192)!=126976)return 4;
                if(ByteSearch::IndexOf((byte*)input,262144,&input[131072],1)!=131072)return 9;
                if(ByteSearch::LastIndexOf((byte*)input,262144,&input[131072],1)!=131072)return 10;
                for(nuint i=0;i<8192;i++)pattern[i]=(byte)97;
                if(ByteSearch::IndexOf((byte*)input,262144,(byte*)pattern,8192)!=0)return 5;
                if(ByteSearch::LastIndexOf((byte*)input,262144,(byte*)pattern,8192)!=253952)return 6;
                // A match in storage just outside the counted region is not a match.
                pattern[0]=(byte)98;
                if(ByteSearch::IndexOf((byte*)input,131072,(byte*)pattern,1)!=131072)return 7;
                if(ByteSearch::LastIndexOf((byte*)input,131072,(byte*)pattern,1)!=131072)return 8;
                return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        SourceFile[] sources=[new("ByteSearch.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Text/ByteSearch.gf"))),new("main.gf",code)];
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        foreach(string forbidden in new[]{"invoke ","personality","@__gflat_eh_","gflat$Memory$","@malloc","@free"})Assert.DoesNotContain(forbidden,ir);
        Assert.Equal(0,CompilerTestHelper.RunIr(ir,optimization,expectedExitCode:0).ExitCode);
    }
}

