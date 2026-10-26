using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class PlatformUtcClockTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostConversionHelpersMatchExactCivilTicksAndRejectInvalidNativeRanges(int optimization)
    {
        bool windows=OperatingSystem.IsWindows();Assert.True(windows||OperatingSystem.IsLinux());
        var rows=new List<(long First,long Second,bool Valid,long Ticks,int Error)>();
        var random=new Random(420137);
        if(windows){
            // FILETIME's unsigned epoch cannot represent years before1601.
            ulong epoch=(ulong)new System.DateTime(1601,1,1).Ticks;
            ulong cutoff=(ulong)System.DateTime.MaxValue.Ticks-epoch;
            var values=new HashSet<ulong>{0,1,9999999,10000000,uint.MaxValue,(ulong)uint.MaxValue+1,
                (ulong)System.DateTime.UnixEpoch.Ticks-epoch,cutoff-1,cutoff,cutoff+1,(ulong)long.MaxValue,ulong.MaxValue};
            for(int i=0;i<32;i++)values.Add((ulong)random.NextInt64((long)cutoff));
            foreach(ulong value in values){BigInteger exact=(BigInteger)value+epoch;bool valid=exact<=System.DateTime.MaxValue.Ticks;
                rows.Add(((long)(uint)value,(long)(uint)(value>>32),valid,valid?(long)exact:0,2));}
        }else{
            long epoch=System.DateTime.UnixEpoch.Ticks;
            long minimum=-62135596800L,maximum=253402300799L;
            foreach(long seconds in new[]{minimum,minimum+1,-1L,0L,1L,maximum-1,maximum})
            foreach(long nanos in new[]{0L,1L,99L,100L,123456789L,999999999L}){
                BigInteger exact=(BigInteger)epoch+(BigInteger)seconds*10000000+nanos/100;
                rows.Add((seconds,nanos,true,(long)exact,0));
            }
            foreach(long seconds in new[]{minimum-1,maximum+1,long.MinValue,long.MaxValue})rows.Add((seconds,0,false,0,2));
            foreach(long nanos in new[]{-1L,1000000000L,long.MinValue,long.MaxValue})rows.Add((0,nanos,false,0,1));
            for(int i=0;i<32;i++){long seconds=random.NextInt64(minimum,maximum+1),nanos=random.NextInt64(1000000000);
                rows.Add((seconds,nanos,true,System.DateTime.UnixEpoch.AddTicks(seconds*10000000+nanos/100).Ticks,0));}
        }
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var row in rows){writer.Write(row.First);writer.Write(row.Second);writer.Write((byte)(row.Valid?1:0));writer.Write(row.Ticks);writer.Write((byte)row.Error);}
        string hex=Convert.ToHexString(bytes.ToArray());
        string conversion=windows?"Windows::system_clock_detail::FromFileTime((uint)first,(uint)second)":"Linux::system_clock_detail::FromUnixTime(first,second)";
        var result=CompilerTestHelper.Run(Sources(HostFolder,$$$"""
            using std; using std::Text; using std::Time;
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
            long ReadLong(readonly(char)* text,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(text,ref at)<<shift);return (long)value;}
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{rows.Count}}};row++){
                    long first=ReadLong(data,ref at);long second=ReadLong(data,ref at);bool valid=ReadByte(data,ref at)!=0;
                    long expected=ReadLong(data,ref at);int errorKind=ReadByte(data,ref at);DateTime value=DateTime::FromTicks(123l);bool caught=false;
                    try{value={{{conversion}}};}catch(ref Exception error){
                        caught=true;
                        if(errorKind==1){if(!CString::Equals(error.GetMessage(),"clock_gettime returned an invalid timestamp"))return 1;}
                        else if(!CString::Equals(error.GetMessage(),"System time is outside years 1 through 9999"))return 2;
                    }
                    if(caught==valid)return 3;
                    if(valid){if(value.Ticks!=expected)return 4;}else if(value.Ticks!=123l)return 5;
                }
                if(at!={{{hex.Length}}})return 6;return 0;
            }
            """),optimization);
        Assert.True(result.ExitCode==0,$"UTC conversions exit {result.ExitCode}: {result.StandardError}");
        Assert.Equal("",result.StandardOutput);Assert.Equal("",result.StandardError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task HostUtcSamplesAreBracketedByHostUtcObservationsWithoutAssumingSampleOrder(int optimization)
    {
        Assert.True(OperatingSystem.IsWindows()||OperatingSystem.IsLinux());
        string provider=OperatingSystem.IsWindows()?"Windows::SystemClock":"Linux::SystemClock";
        string program=$$$"""
            using std; using std::Text; using std::Time;
            int main(){
                for(int i=0;i<3;i++){
                    DateTime value={{{provider}}}::GetUtcNow();char[32] output;nuint written=0;
                    if(!IntegerFormatting::TryFormat(value.Ticks,output,32,ref written))return 1;
                    Console::WriteLine(output,written);
                }
                Console::Flush();return 0;
            }
            """;
        string directory=Path.Combine(Path.GetTempPath(),"gflat-utc-clock-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Compiler.Emit(Sources(HostFolder,program),options:new(){InternalizeFunctions=true});
            string ll=Path.Combine(directory,"clock.ll"),exe=Path.Combine(directory,"clock.exe");File.WriteAllText(ll,ir);
            var compilation=NativeToolchain.Compile(ll,exe,optimizationLevel:optimization);
            Assert.True(compilation.ExitCode==0,compilation.StandardError);
            long before=System.DateTime.UtcNow.Ticks;
            var execution=NativeToolchain.Run(exe,[]);
            long after=System.DateTime.UtcNow.Ticks;
            Assert.Equal(0,execution.ExitCode);Assert.Equal("",execution.StandardError);
            string[] lines=execution.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);Assert.Equal(3,lines.Length);
            // Wall clocks may move backward: no adjacent native-sample ordering assertion.
            long lower=Math.Min(before,after),upper=Math.Max(before,after);
            foreach(string line in lines){long ticks=long.Parse(line,NumberStyles.None,CultureInfo.InvariantCulture);Assert.InRange(ticks,lower,upper);}
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    [Fact]
    public void ForeignProviderAndItsConversionHelperEmitWithoutLinkingForeignNativeSymbols()
    {
        bool windows=OperatingSystem.IsWindows();string folder=windows?"linux":"windows";
        string provider=windows?"Linux::SystemClock":"Windows::SystemClock";
        string helper=windows?"Linux::system_clock_detail::FromUnixTime(0l,0l)":"Windows::system_clock_detail::FromFileTime(0u,0u)";
        string program=$$$"""
            using std; using std::Text; using std::Time;int main(){DateTime sampled={{{provider}}}::GetUtcNow();DateTime converted={{{helper}}};if(sampled.Ticks==converted.Ticks)return 1;return 0;}
            """;
        // DateTime is concrete; emit checks actual provider bodies and foreign ABI declarations.
        string ir=Compiler.Emit(Sources(folder,program));
        Assert.Contains(windows?"@clock_gettime(":"@GetSystemTimePreciseAsFileTime(",ir);
        Assert.Contains("$SystemClock$GetUtcNow",ir);
    }

    private static string HostFolder=>OperatingSystem.IsWindows()?"windows":"linux";
    private static IEnumerable<SourceFile> Sources(string folder,string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Platform",folder),"*.gf",SearchOption.AllDirectories))
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("utc-clock.gf",program));
}
