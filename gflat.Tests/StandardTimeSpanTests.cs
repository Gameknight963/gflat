using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class StandardTimeSpanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullRangeComponentsAndInvariantFormattingMatchDotNet(int optimization)
    {
        var ticks = new SortedSet<long> { long.MinValue, long.MinValue + 1, long.MaxValue - 1, long.MaxValue, 0 };
        foreach (long scale in new long[] { 1, 10000, 10000000, 600000000, 36000000000, 864000000000 })
            foreach (long factor in new long[] { -25, -2, -1, 0, 1, 2, 25 })
                foreach (long delta in new long[] { -1, 0, 1 }) ticks.Add(scale * factor + delta);
        var random = new Random(100007);
        for (int i = 0; i < 180; i++) ticks.Add(random.NextInt64(long.MinValue, long.MaxValue));
        string path = Path.Combine(Path.GetTempPath(), "gflat-timespan-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var output = new BinaryWriter(File.Create(path)))
            {
                long previous = 0;
                foreach (long tick in ticks)
                {
                    var value = new TimeSpan(tick);
                    output.Write(tick);
                    foreach (int part in new[] { value.Days, value.Hours, value.Minutes, value.Seconds, value.Milliseconds }) output.Write(part);
                    output.Write(value.TotalSeconds);
                    byte[] text = Encoding.ASCII.GetBytes(value.ToString("c", CultureInfo.InvariantCulture));
                    output.Write((byte)text.Length); output.Write(text); output.Write(new byte[26 - text.Length]);
                    output.Write((byte)(tick.CompareTo(previous) + 1)); previous = tick;
                }
            }
            Run($$"""
                using std; using std::Time; using std::Collections;
                extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* output,nuint size,nuint count,void* file);
                extern int fclose(void* file);
                int main(){
                    void* file=fopen({{Literal(path)}},"rb");defer{fclose(file);}
                    long ticks=0l;int[5] parts;double seconds=0d;char length='\0';char comparison='\0';char[26] expected;
                    char[28] output;TimeSpan previous=new();int samples=0;
                    while(fread(&ticks,8,1,file)==1){
                        if(fread((void*)&parts[0],4,5,file)!=5||fread(&seconds,8,1,file)!=1||fread(&length,1,1,file)!=1
                           ||fread((void*)&expected[0],1,26,file)!=26||fread(&comparison,1,1,file)!=1)return 1;
                        immutable(TimeSpan) value=TimeSpan::FromTicks(ticks);
                        if(value.Ticks!=ticks||value.Days!=parts[0]||value.Hours!=parts[1]||value.Minutes!=parts[2]
                           ||value.Seconds!=parts[3]||value.Milliseconds!=parts[4])return 2;
                        double error=value.TotalSeconds-seconds;if(error<0d)error=-error;
                        double bound=seconds;if(bound<0d)bound=-bound;
                        if(error>bound*0.000000000000001d+0.000000000000001d)return 3;
                        if(value.CompareTo(&previous)!=(int)(byte)comparison-1)return 4;
                        TimeSpan same=TimeSpan::FromTicks(ticks);
                        if(!value.Equals(&same)||value.CompareTo(&same)!=0||value.GetHashCode()!=same.GetHashCode())return 5;
                        for(nuint capacity=0;capacity<=27;capacity++){
                            for(nuint i=0;i<28;i++)output[i]='!';nuint written=99;
                            bool success=value.TryFormat(&output[1],capacity,ref written);
                            if(success!=(capacity>=(nuint)(byte)length))return 6;
                            if(!success){if(written!=0)return 7;for(nuint i=0;i<28;i++)if(output[i]!='!')return 8;}
                            else{if(written!=(nuint)(byte)length||output[0]!='!')return 9;
                                for(nuint i=0;i<written;i++)if(output[i+1]!=expected[i])return 10;
                                for(nuint i=written+1;i<28;i++)if(output[i]!='!')return 11;}
                        }
                        TimeSpan[4] aligned;aligned[0]=same;nuint count=0;
                        if(!aligned[0].TryFormat((char*)&aligned[0],32,ref count)||count!=(nuint)(byte)length)return 12;
                        for(nuint i=0;i<count;i++)if(((char*)&aligned[0])[i]!=expected[i])return 13;
                        nuint written=99;if(value.TryFormat((char*)1,0,ref written)||written!=0||value.Ticks!=ticks)return 14;
                        previous=same;samples++;
                    }
                    TimeSpan zero=new();if(zero.Ticks!=0l||samples!={{ticks.Count}})return 15;return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntegralFactoriesAndArithmeticCheckEverySignedBoundary(int optimization)
    {
        var code = new StringBuilder("using std; using std::Time; using std::Collections;int main(){int caught=0;\n");
        int throws = 0;
        var random = new Random(3345);
        foreach (var (name, scale) in new (string, long)[] { ("Milliseconds", 10000), ("Seconds", 10000000), ("Minutes", 600000000), ("Hours", 36000000000), ("Days", 864000000000) })
        {
            var inputs = new List<long> { long.MinValue, long.MaxValue, long.MinValue / scale, long.MinValue / scale - 1, long.MaxValue / scale, long.MaxValue / scale + 1, -1, 0, 1 };
            for (int i = 0; i < 12; i++)
            {
                inputs.Add(random.NextInt64(long.MinValue, long.MaxValue));
                inputs.Add(random.NextInt64(long.MinValue / scale, long.MaxValue / scale + 1));
            }
            foreach (long input in inputs)
            {
                string call = $"TimeSpan::From{name}({Long(input)})";
                try { long expected = checked(input * scale); code.AppendLine($"if({call}.Ticks!={Long(expected)})return 1;"); }
                catch (OverflowException) { code.AppendLine($"try{{{call};return 2;}}catch(ref Exception error){{caught++;}}"); throws++; }
            }
        }
        var pairs = new List<(long, long)>();
        long[] edges = [long.MinValue, long.MinValue + 1, -1, 0, 1, long.MaxValue - 1, long.MaxValue];
        foreach (long a in edges) foreach (long b in edges) pairs.Add((a, b));
        for (int i = 0; i < 40; i++) pairs.Add((random.NextInt64(long.MinValue, long.MaxValue), random.NextInt64(long.MinValue, long.MaxValue)));
        foreach (var (a, b) in pairs)
        {
            code.AppendLine($"{{TimeSpan a=TimeSpan::FromTicks({Long(a)});TimeSpan b=TimeSpan::FromTicks({Long(b)});");
            foreach (bool add in new[] { true, false })
            {
                string call = add ? "a.Add(b)" : "a.Subtract(b)";
                try { long expected = (add ? new TimeSpan(a).Add(new TimeSpan(b)) : new TimeSpan(a).Subtract(new TimeSpan(b))).Ticks; code.AppendLine($"if({call}.Ticks!={Long(expected)})return 3;"); }
                catch (OverflowException) { code.AppendLine($"try{{{call};return 4;}}catch(ref Exception error){{caught++;}}"); throws++; }
            }
            code.AppendLine($"if(a.Ticks!={Long(a)}||b.Ticks!={Long(b)})return 5;}}");
        }
        foreach (long tick in edges)
        {
            string call = $"TimeSpan::FromTicks({Long(tick)}).Negate()";
            try { long expected = new TimeSpan(tick).Negate().Ticks; code.AppendLine($"if({call}.Ticks!={Long(expected)})return 6;"); }
            catch (OverflowException) { code.AppendLine($"try{{{call};return 7;}}catch(ref Exception error){{caught++;}}"); throws++; }
        }
        code.AppendLine($"if(caught!={throws})return 8;return 0;}}"); Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulValueOperationsAndBorrowedFormattingAllocateNothing(int optimization)
        => Run("""
            using std; using std::Time; using std::Collections;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}public replace void Free(void* data)nothrow{allocations++;}}
            int main(){
                TimeSpan a=TimeSpan::FromDays(-2);TimeSpan b=TimeSpan::FromHours(3);
                TimeSpan c=a.Add(b).Subtract(TimeSpan::FromMinutes(4)).Negate();
                c=c.Add(TimeSpan::FromSeconds(5)).Add(TimeSpan::FromMilliseconds(6));
                TimeSpan copy=TimeSpan::FromTicks(c.Ticks);
                if(!c.Equals(&copy)||c.CompareTo(&copy)!=0||c.GetHashCode()!=copy.GetHashCode())return 1;
                if(c.Days!=1||c.Hours!=21||c.Minutes!=4||c.Seconds!=5||c.Milliseconds!=6||c.TotalSeconds<1d)return 2;
                char[27] output;BufferWriter writer=new(output,27);c.Format(&writer);
                if(!CString::Equals(writer.Data,"1.21:04:05.0060000")||allocations!=0)return 3;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HashKeysInterpolationAndWriterFailuresUseExistingContracts(int optimization)
        => Run("""
            using std; using std::Time; using std::Collections;
            struct Sink:ITextWriter{
                public int Calls;public bool Fail;public char* Data;
                public Sink(char* data,bool fail){Calls=0;Fail=fail;Data=data;}
                public void Write(readonly(char)* input,nuint count){Calls++;if(count!=19)throw new Exception("count",71);
                    Data[0]=input[0];if(Fail)throw new Exception("write",73);
                    for(nuint i=1;i<count;i++)Data[i]=input[i];}
            }
            int main(){
                immutable(TimeSpan) duration=TimeSpan::FromTicks(-900610000001l);
                TimeSpan same=TimeSpan::FromTicks(-900610000001l);HashMap<TimeSpan,int> map=new();map.Add(same,42);
                if(map[&same]!=42||!map.ContainsKey(&same))return 1;
                String text=s$"time={duration}";if(!CString::Equals(text.Data,"time=-1.01:01:01.0000001"))return 2;
                char[26] output;Sink sink=new(output,false);IFormattable* formatted=&same;formatted.Format(&sink);
                if(sink.Calls!=1)return 3;readonly(char)* expected="-1.01:01:01.0000001";
                for(nuint i=0;i<19;i++)if(output[i]!=expected[i])return 4;
                output[0]='!';Sink failing=new(output,true);
                try{formatted.Format(&failing);return 5;}catch(ref Exception error){if(error.GetCode()!=73)return 6;}
                if(failing.Calls!=1||output[0]!='-'||duration.Ticks!=-900610000001l)return 7;return 0;
            }
            """, optimization);

    [Fact]
    public void SelectiveSourceCompilationNeedsNoStringOrLibcImplementation()
    {
        string[] names = ["core/Time/TimeSpan.gf", "core/Text.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/Exception.gf"];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("nothrow.gf", """
                using std; using std::Time; using std::Collections;
                namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* data)nothrow{}}
                bool Convert(long ticks,char* output,nuint capacity,ref nuint written)nothrow{
                    immutable(TimeSpan) value=TimeSpan::FromTicks(ticks);TimeSpan other=new();
                    long exact=value.Ticks;double seconds=value.TotalSeconds;
                    int parts=value.Days+value.Hours+value.Minutes+value.Seconds+value.Milliseconds;
                    bool equal=value.Equals(&other);int comparison=value.CompareTo(&other);nuint hash=value.GetHashCode();
                    return value.TryFormat(output,capacity,ref written);
                }
                """));
        Compiler.Emit(sources, options: new() { NoStdLib = true });
    }

    private static string Long(long value) => value == long.MinValue ? "(-9223372036854775807l-1l)" : value.ToString(CultureInfo.InvariantCulture) + "l";
    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("timespan.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}

