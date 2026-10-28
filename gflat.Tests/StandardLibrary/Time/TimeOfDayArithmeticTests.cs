using Integer=System.Numerics.BigInteger;

namespace gflat.Tests.Std.Time;

public sealed class TimeOfDayArithmeticTests
{
    private const long Day=864000000000;
    private const string Helpers="""
        using std; using std::Encoding; using std::Time;
        int attempts=0;bool deny=false;
        extern void*? malloc(nuint size);extern void free(void* value);
        namespace Memory {
            public replace void*? Allocate(nuint size){attempts++;if(deny)return null;return malloc(size);}
            public replace void Free(void* value)nothrow{free(value);}
        }
        int Hex(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        long ReadLong(readonly(char)* text,ref nuint position)nothrow{
            ulong value=0ul;
            for(int shift=0;shift<64;shift+=8){
                uint byteValue=(uint)(Hex(text[position])*16+Hex(text[position+1]));position+=2;
                value=value|((ulong)byteValue<<shift);
            }
            return (long)value;
        }
        TimeOfDay AddProbe(immutable(TimeOfDay)* value,TimeSpan delta,ref int days)nothrow
            =>value.Add(delta,ref days);
        bool BetweenProbe(immutable(TimeOfDay)* value,TimeOfDay start,TimeOfDay end)nothrow
            =>value.IsBetween(start,end);
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AdditionMatchesTimeOnlyAndExactUnboundedTickOracleIncludingOutputAliases(int optimization)
    {
        var rows=new List<(long Start,long Delta)>();
        foreach(long start in new[]{0L,1,Day/2,Day-1})
        foreach(long delta in new[]{long.MinValue,long.MaxValue,-Day-1,-Day,-Day+1,-1,0,1,Day-1,Day,Day+1})
            rows.Add((start,delta));
        var random=new Random(951397);
        for(int i=0;i<64;i++){
            byte[] bytes=new byte[8];random.NextBytes(bytes);
            rows.Add((random.NextInt64(Day),BitConverter.ToInt64(bytes)));
        }
        using var data=new MemoryStream();
        using(var writer=new BinaryWriter(data,System.Text.Encoding.UTF8,leaveOpen:true))
        foreach(var row in rows){
            Integer days=Integer.DivRem((Integer)row.Start+row.Delta,Day,out Integer remainder);
            if(remainder.Sign<0){days--;remainder+=Day;}
            TimeOnly expected=new TimeOnly(row.Start).Add(System.TimeSpan.FromTicks(row.Delta),out int wrapped);
            Assert.Equal((long)remainder,expected.Ticks);Assert.Equal((int)days,wrapped);
            writer.Write(row.Start);writer.Write(row.Delta);writer.Write(expected.Ticks);writer.Write((long)wrapped);
        }
        string hex=Convert.ToHexString(data.ToArray());
        Run(Helpers+$$"""
            int main(){
                readonly(char)* data="{{hex}}";nuint position=0;
                int before=attempts;deny=true;
                for(int row=0;row<{{rows.Count}};row++){
                    long start=ReadLong(data,ref position);long delta=ReadLong(data,ref position);
                    long expected=ReadLong(data,ref position);int wantedDays=(int)ReadLong(data,ref position);
                    TimeOfDay value=TimeOfDay::FromTicks(start);TimeSpan interval=TimeSpan::FromTicks(delta);
                    immutable(TimeOfDay)* view=&value;readonly(TimeOfDay)* shallow=&value;
                    int days=123;TimeOfDay result=AddProbe(view,interval,ref days);
                    TimeOfDay simple=shallow.Add(interval);
                    if(result.Ticks!=expected||simple.Ticks!=expected||days!=wantedDays||value.Ticks!=start)return 1;
                    // Each int output is aligned and fully backed by the 8-byte receiver.
                    // Only returned result and output are inspected after deliberate overwrite.
                    for(nuint offset=0;offset<8;offset+=4){
                        TimeOfDay aliased=TimeOfDay::FromTicks(start);
                        int* output=(int*)((char*)&aliased+offset);
                        TimeOfDay shifted=aliased.Add(interval,ref *output);
                        if(shifted.Ticks!=expected||*output!=wantedDays)return 2;
                    }
                }
                if(position!={{hex.Length}}||attempts!=before)return 3;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BetweenMatchesInclusiveExclusiveTimeOnlyRangesAcrossMidnightAndEqualEndpoints(int optimization)
    {
        var rows=new List<(long Value,long Start,long End)>();
        long[] edges=[0,1,Day/2,Day-1];
        foreach(long value in edges)foreach(long start in edges)foreach(long end in edges)rows.Add((value,start,end));
        var random=new Random(397951);
        for(int i=0;i<64;i++)rows.Add((random.NextInt64(Day),random.NextInt64(Day),random.NextInt64(Day)));
        using var data=new MemoryStream();
        using(var writer=new BinaryWriter(data,System.Text.Encoding.UTF8,leaveOpen:true))
        foreach(var row in rows){
            bool expected=new TimeOnly(row.Value).IsBetween(new TimeOnly(row.Start),new TimeOnly(row.End));
            bool independent=row.Start<row.End?row.Value>=row.Start&&row.Value<row.End:
                row.Start>row.End&&(row.Value>=row.Start||row.Value<row.End);
            Assert.Equal(independent,expected);
            writer.Write(row.Value);writer.Write(row.Start);writer.Write(row.End);writer.Write(expected?1L:0L);
        }
        string hex=Convert.ToHexString(data.ToArray());
        Run(Helpers+$$"""
            int main(){
                readonly(char)* data="{{hex}}";nuint position=0;int before=attempts;deny=true;
                for(int row=0;row<{{rows.Count}};row++){
                    long ticks=ReadLong(data,ref position);long first=ReadLong(data,ref position);
                    long last=ReadLong(data,ref position);bool expected=ReadLong(data,ref position)!=0l;
                    TimeOfDay value=TimeOfDay::FromTicks(ticks);TimeOfDay start=TimeOfDay::FromTicks(first);TimeOfDay end=TimeOfDay::FromTicks(last);
                    immutable(TimeOfDay)* view=&value;
                    if(BetweenProbe(view,start,end)!=expected||value.Ticks!=ticks||start.Ticks!=first||end.Ticks!=last)return 1;
                }
                if(position!={{hex.Length}}||attempts!=before)return 2;return 0;
            }
            """,optimization);
    }

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("time-of-day-arithmetic.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"TimeOfDay arithmetic exit {result.ExitCode}: {result.StandardError}");
    }
}
