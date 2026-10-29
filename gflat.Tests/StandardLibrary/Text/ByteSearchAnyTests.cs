using System.Text;

namespace gflat.Tests.Std.Text;

// Independent nested-loop oracle; no membership-mask logic is shared with the implementation.
public sealed class ByteSearchAnyTests
{
    private static (int First, int Last) Naive(byte[] data, int start, int length,
        byte[] pattern, int patternStart, int count)
    {
        int first=length,last=length;
        for(int i=0;i<length;i++)
            for(int j=0;j<count;j++)
                if(data[start+i]==pattern[patternStart+j])
                {
                    if(first==length)first=i;
                    last=i;
                    break;
                }
        return(first,last);
    }

    private static string Bytes(IEnumerable<byte> values)
        => string.Join(",",values.Select(value=>"(byte)"+value));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryByteAndDuplicateUnsortedSetsMatchNaiveMembership(int optimization)
    {
        byte[] input=Enumerable.Range(0,256).Concat(Enumerable.Range(0,256).Reverse()).Select(value=>(byte)value).ToArray();
        var rows=new List<int>();
        for(int value=0;value<256;value++)
        {
            byte[] single=[(byte)value],pair=[(byte)value,(byte)value,(byte)(value^255)];
            var one=Naive(input,0,input.Length,single,0,1);
            var many=Naive(input,0,input.Length,pair,0,pair.Length);
            rows.AddRange([one.First,one.Last,many.First,many.Last]);
        }
        await Run($$"""
            using std; using std::Text;
            int main(){
                byte[514] input;byte[3] pattern;
                input[0]=(byte)90;input[513]=(byte)165;
                for(nuint i=0;i<256;i++){input[i+1]=(byte)i;input[i+257]=(byte)(255-i);}
                int[1024] rows=[{{string.Join(',',rows)}}];
                for(int value=0;value<256;value++){
                    pattern[0]=(byte)value;pattern[1]=(byte)value;pattern[2]=(byte)(value^255);
                    nuint row=(nuint)value*4;
                    for(nuint count=1;count<=2;count++){
                        if(ByteSearch::IndexOfAny((byte*)input+1,512,(byte*)pattern,count)!=(nuint)rows[row])return 1;
                        if(ByteSearch::LastIndexOfAny((byte*)input+1,512,(byte*)pattern,count)!=(nuint)rows[row+1])return 2;
                    }
                    if(ByteSearch::IndexOfAny((byte*)input+1,512,(byte*)pattern,3)!=(nuint)rows[row+2])return 3;
                    if(ByteSearch::LastIndexOfAny((byte*)input+1,512,(byte*)pattern,3)!=(nuint)rows[row+3])return 4;
                }
                for(nuint i=0;i<256;i++)if(input[i+1]!=(byte)i||input[i+257]!=(byte)(255-i))return 5;
                if(input[0]!=(byte)90||input[513]!=(byte)165)return 6;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SeededSlicesAndOverlappingReadonlyViewsMatchNaiveMembership(int optimization)
    {
        var random=new Random(492113);
        byte[] input=new byte[256],pattern=new byte[320];
        random.NextBytes(input);random.NextBytes(pattern);
        var disjoint=new List<int>();var overlapping=new List<int>();
        for(int trial=0;trial<40;trial++)
        {
            int start=random.Next(input.Length+1),length=random.Next(input.Length-start+1);
            int patternStart=random.Next(pattern.Length+1),count=random.Next(pattern.Length-patternStart+1);
            var expected=Naive(input,start,length,pattern,patternStart,count);
            disjoint.AddRange([start,length,patternStart,count,expected.First,expected.Last]);
        }
        for(int trial=0;trial<24;trial++)
        {
            int start=random.Next(input.Length+1),length=random.Next(input.Length-start+1);
            int patternStart=trial%4==0?start:random.Next(input.Length+1);
            int count=trial%4==0?length:random.Next(input.Length-patternStart+1);
            var expected=Naive(input,start,length,input,patternStart,count);
            overlapping.AddRange([start,length,patternStart,count,expected.First,expected.Last]);
        }
        await Run($$"""
            using std; using std::Text;
            int main(){
                byte[256] input=[{{Bytes(input)}}];byte[320] pattern=[{{Bytes(pattern)}}];
                byte[256] inputBefore;byte[320] patternBefore;
                for(nuint i=0;i<256;i++)inputBefore[i]=input[i];
                for(nuint i=0;i<320;i++)patternBefore[i]=pattern[i];
                int[240] disjoint=[{{string.Join(',',disjoint)}}];int[144] overlapping=[{{string.Join(',',overlapping)}}];
                for(nuint trial=0;trial<40;trial++){
                    nuint row=trial*6;
                    readonly(byte)* data=(byte*)input+disjoint[row];readonly(byte)* set=(byte*)pattern+disjoint[row+2];
                    nuint length=(nuint)disjoint[row+1];nuint count=(nuint)disjoint[row+3];
                    if(ByteSearch::IndexOfAny(data,length,set,count)!=(nuint)disjoint[row+4])return 1;
                    if(ByteSearch::LastIndexOfAny(data,length,set,count)!=(nuint)disjoint[row+5])return 2;
                }
                for(nuint trial=0;trial<24;trial++){
                    nuint row=trial*6;
                    readonly(byte)* data=(byte*)input+overlapping[row];readonly(byte)* set=(byte*)input+overlapping[row+2];
                    nuint length=(nuint)overlapping[row+1];nuint count=(nuint)overlapping[row+3];
                    if(ByteSearch::IndexOfAny(data,length,set,count)!=(nuint)overlapping[row+4])return 3;
                    if(ByteSearch::LastIndexOfAny(data,length,set,count)!=(nuint)overlapping[row+5])return 4;
                }
                for(nuint i=0;i<256;i++)if(input[i]!=inputBefore[i])return 5;
                for(nuint i=0;i<320;i++)if(pattern[i]!=patternBefore[i])return 6;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EmptySidesReadNeitherPointerAndRegionBoundsAreCounted(int optimization)
        =>await Run("""
            using std; using std::Text;
            int main(){
                readonly(byte)* invalid=(readonly(byte)*)1;nuint maximum=(nuint)0-(nuint)1;
                if(ByteSearch::IndexOfAny(invalid,0,invalid,maximum)!=0)return 1;
                if(ByteSearch::LastIndexOfAny(invalid,0,invalid,maximum)!=0)return 2;
                if(ByteSearch::IndexOfAny(invalid,maximum,invalid,0)!=maximum)return 3;
                if(ByteSearch::LastIndexOfAny(invalid,maximum,invalid,0)!=maximum)return 4;
                if(ByteSearch::IndexOfAny(invalid,0,invalid,0)!=0||ByteSearch::LastIndexOfAny(invalid,0,invalid,0)!=0)return 5;
                byte[4] input=[(byte)17,(byte)17,(byte)165,(byte)17];
                byte[3] pattern=[(byte)165,(byte)17,(byte)165];
                if(ByteSearch::IndexOfAny((byte*)input+1,2,(byte*)pattern+1,1)!=0)return 6;
                if(ByteSearch::LastIndexOfAny((byte*)input+1,2,(byte*)pattern+1,1)!=0)return 7;
                // A matching pattern guard is outside the one-byte set.
                if(ByteSearch::IndexOfAny((byte*)input+2,1,(byte*)pattern+1,1)!=1)return 8;
                if(ByteSearch::LastIndexOfAny((byte*)input+2,1,(byte*)pattern+1,1)!=1)return 9;
                byte[1] high=[(byte)255];byte[260] all;
                for(nuint i=0;i<260;i++)all[i]=(byte)i;
                if(ByteSearch::IndexOfAny((byte*)high,1,(byte*)all,260)!=0)return 10;
                if(ByteSearch::LastIndexOfAny((byte*)high,1,(byte*)all,260)!=0)return 11;
                byte[4] zeros=[(byte)128,(byte)0,(byte)255,(byte)0];byte[3] set=[(byte)0,(byte)255,(byte)0];
                if(ByteSearch::IndexOfAny((byte*)zeros,4,(byte*)set,3)!=1)return 12;
                if(ByteSearch::LastIndexOfAny((byte*)zeros,4,(byte*)set,3)!=3)return 13;
                return 0;
            }
            """,optimization);

    private static Task Run(string program,int optimization)
    {
        SourceFile[] sources=[
            new("ByteSearch.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Text/ByteSearch.gf"))),
            new("byte-search-any.gf",program)
        ];
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        foreach(string forbidden in new[]{"invoke ","personality","__gflat_eh_","gflat$Memory$","@malloc","@free","__gflat_gc"})
            Assert.DoesNotContain(forbidden,ir);
        string[] definitions=ir.Split('\n').Where(line=>line.StartsWith("define ")&&
            (line.Contains("$IndexOfAny(")||line.Contains("$LastIndexOfAny("))).ToArray();
        Assert.Equal(2,definitions.Length);
        Assert.All(definitions,line=>Assert.Contains("nounwind",line));
        var result=CompilerTestHelper.RunIr(ir,optimization,expectedExitCode:0);
        Assert.True(result.ExitCode==0,$"Byte any exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        return Task.CompletedTask;
    }
}
