using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Time;

// Check provider-independent state and exact conversion with deterministic clocks.
public sealed class StopwatchTests
{
    private const string Support="""
        using std; using std::Time;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        long now=0l;long frequency=10l;int timestamps=0;int frequencies=0;bool failTimestamp=false;bool failFrequency=false;
        static class FakeClock:IMonotonicClock{
            public static long GetTimestamp(){timestamps++;if(failTimestamp){failTimestamp=false;throw new Exception("timestamp",91);}return now;}
            public static long GetFrequency(){frequencies++;if(failFrequency){failFrequency=false;throw new Exception("frequency",92);}return frequency;}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StateTransitionsCopiesAndNoOpOperationsUseOnlyTheirRequiredClockSamplesWithoutAllocation(int optimization)
        =>Run(Support+"""
            int main(){
                int before=attempts;deny=true;frequency=3l;now=-100l;
                Stopwatch<FakeClock> watch=new();immutable(Stopwatch<FakeClock>)* view=&watch;
                if(frequencies!=1||timestamps!=0||view.IsRunning||view.ElapsedTicks!=0l||view.ElapsedSeconds!=0.0)return 1;
                TimeSpan initial=view.Elapsed;if(initial.Ticks!=0l||timestamps!=0)return 2;
                watch.Start();if(!view.IsRunning||timestamps!=1)return 3;
                failTimestamp=true;watch.Start();if(timestamps!=1||!failTimestamp)return 4;failTimestamp=false;
                now=-94l;int calls=timestamps;
                if(view.ElapsedTicks!=6l||timestamps!=calls+1)return 5;calls=timestamps;
                if(view.ElapsedSeconds!=2.0||timestamps!=calls+1)return 6;calls=timestamps;
                TimeSpan span=view.Elapsed;if(span.Ticks!=20000000l||timestamps!=calls+1)return 7;
                now=-91l;watch.Stop();if(view.IsRunning||view.ElapsedTicks!=9l)return 8;
                calls=timestamps;failTimestamp=true;watch.Stop();if(timestamps!=calls||!failTimestamp)return 9;failTimestamp=false;
                now=1000l;if(view.ElapsedTicks!=9l||timestamps!=calls)return 10;
                watch.Start();now=1006l;watch.Stop();if(view.ElapsedTicks!=15l||view.ElapsedSeconds!=5.0)return 11;
                // A copied value has its own accumulated/running state and cached frequency.
                watch.Start();Stopwatch<FakeClock> copied=watch;int frequencyCalls=frequencies;
                now=1009l;copied.Stop();now=1012l;
                if(copied.IsRunning||copied.ElapsedTicks!=18l||watch.ElapsedTicks!=21l||frequencies!=frequencyCalls)return 12;
                calls=timestamps;failTimestamp=true;copied.Reset();if(copied.IsRunning||copied.ElapsedTicks!=0l||timestamps!=calls||!failTimestamp)return 13;failTimestamp=false;
                now=2000l;watch.Restart();now=2004l;if(!watch.IsRunning||watch.ElapsedTicks!=4l)return 14;
                watch.Reset();if(watch.IsRunning||watch.ElapsedTicks!=0l)return 15;
                calls=timestamps;frequencyCalls=frequencies;now=3000l;
                Stopwatch<FakeClock> fresh=Stopwatch<FakeClock>::StartNew();
                if(!fresh.IsRunning||timestamps!=calls+1||frequencies!=frequencyCalls+1)return 16;
                now=3003l;fresh.Stop();if(fresh.ElapsedTicks!=3l||watch.ElapsedTicks!=0l)return 17;
                if(attempts!=before)return 18;deny=false;if(allocated!=released)return 19;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactTimeSpanScalingMatchesBigIntegerAcrossFrequencyAndSignedLongBoundaries(int optimization)
    {
        long[] frequencies=[1,2,3,7,9999999,10000000,10000001,1000000000,long.MaxValue];
        var rows=new List<(long Frequency,long Ticks)>();var random=new Random(550719);
        foreach(long frequency in frequencies){
            var ticks=new HashSet<long>{0,1,frequency-1,frequency,long.MaxValue};if(frequency<long.MaxValue)ticks.Add(frequency+1);
            for(int sample=0;sample<8;sample++)ticks.Add(random.NextInt64(long.MaxValue));
            foreach(long value in ticks)rows.Add((frequency,value));
        }
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var row in rows){
            BigInteger exact=(BigInteger)row.Ticks*10000000/row.Frequency;bool fits=exact<=long.MaxValue;
            writer.Write(row.Frequency);writer.Write(row.Ticks);writer.Write((double)row.Ticks/(double)row.Frequency);
            writer.Write((byte)(fits?1:0));writer.Write(fits?(long)exact:0L);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
            long ReadLong(readonly(char)* text,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(text,ref at)<<shift);return (long)value;}
            double ReadDouble(readonly(char)* text,ref nuint at)nothrow{long bits=ReadLong(text,ref at);return *(double*)&bits;}
            int main(){readonly(char)* data="{{hex}}";nuint at=0;
                for(int row=0;row<{{rows.Count}};row++){
                    frequency=ReadLong(data,ref at);long ticks=ReadLong(data,ref at);double seconds=ReadDouble(data,ref at);
                    bool fits=ReadByte(data,ref at)!=0;long expected=ReadLong(data,ref at);int before=attempts;deny=true;
                    now=0l;Stopwatch<FakeClock> watch=new();watch.Start();now=ticks;watch.Stop();
                    immutable(Stopwatch<FakeClock>)* view=&watch;int calls=timestamps;int frequencyCalls=frequencies;
                    if(view.IsRunning||view.ElapsedTicks!=ticks||view.ElapsedSeconds!=seconds||timestamps!=calls||frequencies!=frequencyCalls||attempts!=before)return 1;
                    TimeSpan result=TimeSpan::FromTicks(123l);
                    if(fits){result=view.Elapsed;if(result.Ticks!=expected||attempts!=before)return 2;deny=false;}
                    else{deny=false;bool caught=false;try{result=view.Elapsed;}catch(ref Exception error){caught=true;}if(!caught||result.Ticks!=123l)return 3;}
                    if(view.ElapsedTicks!=ticks||view.ElapsedSeconds!=seconds||timestamps!=calls||frequencies!=frequencyCalls)return 4;
                }
                if(at!={{hex.Length}}||allocated!=released)return 5;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidFrequencyProviderFailureAndRawOverflowAreTransactionalAndResetAlwaysRecovers(int optimization)
        =>Run(Support+"""
            int main(){
                frequency=10l;now=0l;Stopwatch<FakeClock> watch=new();watch.Start();now=7l;watch.Stop();
                long[3] invalid=[0l,-1l,(-9223372036854775807l-1l)];
                for(nuint i=0;i<3;i++){
                    frequency=invalid[i];bool caught=false;int calls=timestamps;
                    try{watch=new Stopwatch<FakeClock>();}catch(ref Exception error){caught=true;}frequency=10l;
                    if(!caught||watch.IsRunning||watch.ElapsedTicks!=7l||timestamps!=calls)return 1;
                }
                failFrequency=true;bool caught=false;try{watch=new Stopwatch<FakeClock>();}catch(ref Exception error){if(error.GetCode()!=92)return 2;caught=true;}
                if(!caught||watch.IsRunning||watch.ElapsedTicks!=7l)return 3;
                failTimestamp=true;caught=false;try{watch.Start();}catch(ref Exception error){if(error.GetCode()!=91)return 4;caught=true;}
                if(!caught||watch.IsRunning||watch.ElapsedTicks!=7l)return 5;
                now=100l;watch.Start();now=107l;failTimestamp=true;caught=false;
                try{watch.Stop();}catch(ref Exception error){if(error.GetCode()!=91)return 6;caught=true;}
                if(!caught||!watch.IsRunning||watch.ElapsedTicks!=14l)return 7;watch.Stop();
                failTimestamp=true;caught=false;try{watch.Restart();}catch(ref Exception error){if(error.GetCode()!=91)return 8;caught=true;}
                if(!caught||watch.IsRunning||watch.ElapsedTicks!=14l)return 9;
                now=200l;watch.Start();now=203l;failTimestamp=true;caught=false;
                try{watch.Restart();}catch(ref Exception error){caught=true;}
                if(!caught||!watch.IsRunning||watch.ElapsedTicks!=17l)return 10;
                for(int getter=0;getter<3;getter++){
                    failTimestamp=true;caught=false;int calls=timestamps;
                    try{if(getter==0)watch.ElapsedTicks;else if(getter==1)watch.ElapsedSeconds;else watch.Elapsed;}catch(ref Exception error){if(error.GetCode()!=91)return 11;caught=true;}
                    if(!caught||timestamps!=calls+1||!watch.IsRunning||watch.ElapsedTicks!=17l)return 12;
                }
                watch.Reset();now=10l;watch.Start();now=9l;caught=false;
                try{watch.Stop();}catch(ref Exception error){caught=true;}
                if(!caught||!watch.IsRunning)return 13;now=11l;if(watch.ElapsedTicks!=1l)return 14;
                // Negative signed origins are valid; only the elapsed distance is bounded.
                watch.Reset();now=(-9223372036854775807l-1l);watch.Start();now=0l;caught=false;
                try{watch.Stop();}catch(ref Exception error){caught=true;}
                if(!caught||!watch.IsRunning)return 15;now=(-9223372036854775807l);watch.Stop();if(watch.ElapsedTicks!=1l)return 16;
                watch.Reset();now=(-9223372036854775807l-1l);watch.Start();now=-1l;watch.Stop();
                if(watch.ElapsedTicks!=9223372036854775807l)return 17;
                now=0l;watch.Start();now=1l;caught=false;try{watch.Stop();}catch(ref Exception error){caught=true;}
                if(!caught||!watch.IsRunning)return 18;
                int calls=timestamps;failTimestamp=true;int before=attempts;deny=true;watch.Reset();
                if(watch.IsRunning||watch.ElapsedTicks!=0l||timestamps!=calls||!failTimestamp||attempts!=before)return 19;
                deny=false;failTimestamp=false;
                if(allocated!=released)return 20;return 0;
            }
            """,optimization);

    [Fact]
    public void SelectedCoreSourcesNeedNoStringLibcOrWideDivisionAndResetHasNoExceptionBoundary()
    {
        string[] names=["Time/IMonotonicClock.gf","Time/Stopwatch.gf","Time/TimeSpan.gf","ITextWriter.gf", "IFormattable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("stopwatch-contract.gf","""
                namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* storage)nothrow{}}
                static class Clock:std::Time::IMonotonicClock{public static long GetTimestamp()=>0l;public static long GetFrequency()=>3l;}
                bool ResetProbe(std::Time::Stopwatch<Clock>* value)nothrow{value.Reset();return value.IsRunning;}
                std::Time::TimeSpan Observe(immutable(std::Time::Stopwatch<Clock>)* value)=>value.Elapsed;
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        Assert.DoesNotContain("$String",ir);Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));
        Assert.DoesNotMatch(@"(?m)^declare\s+[^\r\n]*@(?:malloc|free|memcpy|memmove|memset|strlen|clock_gettime|QueryPerformanceCounter)\(",ir);
        string body=System.Text.RegularExpressions.Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$ResetProbe\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke ",body);Assert.DoesNotContain("__gflat_eh_",body);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("stopwatch.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Stopwatch exit {result.ExitCode}: {result.StandardError}");
    }
}
