using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardTimeSpanParsingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CanonicalAndGeneratedGrammarInputsMatchIndependentTickOracle(int optimization)
    {
        await WithDirectory(directory =>
        {
            var rows = ValidRows();
            string manifest = Path.Combine(directory, "valid.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Count);
                foreach (var row in rows)
                {
                    WriteText(writer, Encoding.ASCII.GetBytes(row.Text)); writer.Write(row.Ticks);
                    WriteText(writer, Encoding.ASCII.GetBytes(new TimeSpan(row.Ticks).ToString("c", CultureInfo.InvariantCulture)));
                }
            }
            Run(directory, """
                using std;
                nuint ReadText(File* input,char* text){nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);
                    input.ReadExactly(text,count);text[count]='\0';return count;}
                int main(int argc,char** argv){
                    File file=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&file);
                    for(int row=0;row<rows;row++){
                        char[64] input;char[64] saved;char[27] canonical;
                        nuint length=ReadText(&file,input);long expected=FileBinary::ReadInt64LittleEndian(&file);
                        nuint canonicalLength=ReadText(&file,canonical);
                        for(nuint i=0;i<=length;i++)saved[i]=input[i];
                        TimeSpan counted=TimeSpan::FromTicks(99l);TimeSpan cstring=TimeSpan::FromTicks(99l);
                        if(!TimeSpan::TryParse(input,length,&counted)||!TimeSpan::TryParse(input,&cstring))return 1;
                        TimeSpan parsed=TimeSpan::Parse(input,length);TimeSpan parsedCString=TimeSpan::Parse(input);
                        if(counted.Ticks!=expected||cstring.Ticks!=expected||parsed.Ticks!=expected||parsedCString.Ticks!=expected)return 2;
                        for(nuint i=0;i<=length;i++)if(input[i]!=saved[i])return 3;
                        char[27] output;for(nuint i=0;i<27;i++)output[i]='!';nuint written=91;
                        if(!counted.TryFormat(output,26,ref written)||written!=canonicalLength)return 4;
                        for(nuint i=0;i<written;i++)if(output[i]!=canonical[i])return 5;
                        if(output[written]!='!')return 6;
                        TimeSpan roundtrip=TimeSpan::Parse(output,written);if(roundtrip.Ticks!=expected)return 7;
                    }
                    file.Close();return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task InvalidGrammarAndOverflowResetTryResultsAndPreserveThrowingDestinations(int optimization)
    {
        await WithDirectory(directory =>
        {
            string[] invalid = ["", "-", "+00:00:00", "--00:00:00", "00:00", "00", "0:00:00", "000:00:00",
                "00:0:00", "00:000:00", "00:00:0", "00:00:000", "24:00:00", "00:60:00", "00:00:60",
                "1.24:00:00", "1.00:60:00", "1.00:00:60", ".00:00:00", "1..00:00:00", "1.0:00:00",
                "1:00:00:00", "00::00:00", "00:00::00", "00:00:00.", "00:00:00.12345678",
                "00:00:00..1", "00:00:00.1.2", "00:00:00.-1", "00:00:00.a", "00:00:00x",
                " 00:00:00", "00:00:00 ", "00:00:00\t", "00:00:00\r\n", "00 :00:00",
                "1 day", "1h", "NaN", "Infinity", "10675199.02:48:05.4775808", "-10675199.02:48:05.4775809",
                "10675200.00:00:00", "-10675200.00:00:00", "99999999.00:00:00", new string('9',26),
                "0000000000.00:00:00.0000001", "-000000000.00:00:00.0000001",
                "00:00:00\0", "\000:00:00", "00:\000:00", "00:00:00.\0"];
            var rows = invalid.Select(Encoding.UTF8.GetBytes).ToList();
            rows.Add([48,48,58,48,48,58,48,128]); rows.Add([255,48,58,48,48,58,48,48]);
            string manifest = Path.Combine(directory, "invalid.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Count); foreach (byte[] row in rows) WriteText(writer,row);
            }
            Run(directory, """
                using std;
                int main(int argc,char** argv){
                    File file=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&file);
                    for(int row=0;row<rows;row++){
                        nuint count=(nuint)FileBinary::ReadInt32LittleEndian(&file);char[64] text;
                        file.ReadExactly(text,count);text[count]='\0';TimeSpan value=TimeSpan::FromTicks(73l);
                        if(TimeSpan::TryParse(text,count,&value)||value.Ticks!=0l)return 1;
                        value=TimeSpan::FromTicks(73l);bool caught=false;
                        try{value=TimeSpan::Parse(text,count);return 2;}
                        catch(Exception* error){caught=CString::Equals(error.GetMessage(),"Invalid time interval");}
                        if(!caught||value.Ticks!=73l)return 3;
                        bool nul=false;for(nuint i=0;i<count;i++)if(text[i]=='\0')nul=true;
                        if(!nul){value=TimeSpan::FromTicks(73l);
                            if(TimeSpan::TryParse(text,&value)||value.Ticks!=0l)return 4;
                            caught=false;try{value=TimeSpan::Parse(text);return 5;}
                            catch(Exception* error){caught=CString::Equals(error.GetMessage(),"Invalid time interval");}if(!caught)return 6;}
                    }
                    for(nuint count=27;count<=40;count++){
                        TimeSpan value=TimeSpan::FromTicks(73l);
                        if(TimeSpan::TryParse((readonly(char)*)1,count,&value)||value.Ticks!=0l)return 7;
                    }
                    TimeSpan value=TimeSpan::FromTicks(73l);
                    if(TimeSpan::TryParse((readonly(char)*)1,0,&value)||value.Ticks!=0l)return 8;
                    value=TimeSpan::FromTicks(73l);
                    if(TimeSpan::TryParse((readonly(char)*)1,(nuint)0-(nuint)1,&value)||value.Ticks!=0l)return 9;
                    char[27] unterminated;for(nuint i=0;i<27;i++)unterminated[i]='0';
                    value=TimeSpan::FromTicks(73l);if(TimeSpan::TryParse(unterminated,&value)||value.Ticks!=0l)return 10;
                    // Counted embedded NUL is invalid; C-string semantics stop at that byte.
                    char[10] prefix=['0','0',':','0','0',':','0','0','\0','x'];
                    if(!TimeSpan::TryParse(prefix,&value)||value.Ticks!=0l)return 11;
                    file.Close();return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SuccessfulParsingAndTryFailuresAllocateNothingAndStageBackedInputAliases(int optimization)
        => await WithDirectory(directory => Run(directory, """
            using std;
            int allocations=0;int frees=0;
            namespace Memory{public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* value)nothrow{frees++;}}
            int main(){
                TimeSpan[4] aligned;char* input=(char*)&aligned[0];
                readonly(char)* endpoint="-10675199.02:48:05.4775808";long minimum=-9223372036854775807l-1l;
                readonly(char)* shortFraction="000000000.00:00:00.0000001";
                for(int offset=0;offset<=3;offset++){
                    for(nuint i=0;i<26;i++)input[(nuint)offset+i]=endpoint[i];input[offset+26]='\0';
                    if(!TimeSpan::TryParse(input+offset,26,&aligned[0])||aligned[0].Ticks!=minimum)return 1;
                    for(nuint i=0;i<26;i++)input[(nuint)offset+i]=endpoint[i];input[offset+26]='\0';
                    if(!TimeSpan::TryParse(input+offset,&aligned[0])||aligned[0].Ticks!=minimum)return 2;
                    for(nuint i=0;i<26;i++)input[(nuint)offset+i]=shortFraction[i];input[offset+26]='\0';
                    if(!TimeSpan::TryParse(input+offset,26,&aligned[0])||aligned[0].Ticks!=1l)return 3;
                    readonly(char)* invalid="00:00:00.12345678";
                    for(nuint i=0;i<17;i++)input[(nuint)offset+i]=invalid[i];
                    if(TimeSpan::TryParse(input+offset,17,&aligned[0])||aligned[0].Ticks!=0l)return 4;
                }
                TimeSpan value=TimeSpan::Parse(endpoint);TimeSpan counted=TimeSpan::Parse(endpoint,26);
                if(value.Ticks!=minimum||counted.Ticks!=minimum)return 5;
                value=TimeSpan::FromTicks(73l);
                if(TimeSpan::TryParse((readonly(char)*)1,0,&value)||value.Ticks!=0l)return 6;
                value=TimeSpan::FromTicks(73l);
                if(TimeSpan::TryParse((readonly(char)*)1,27,&value)||value.Ticks!=0l)return 7;
                char[26] output;nuint written=91;if(!counted.TryFormat(output,26,ref written)||written!=26)return 8;
                for(nuint i=0;i<26;i++)if(output[i]!=endpoint[i])return 9;
                if(allocations!=0||frees!=0)return 10;return 0;
            }
            """, [], optimization));

    [Fact]
    public void SelectiveSourceTryParseOverloadsUseDirectNothrowCallsWithoutStringOrLibcSources()
    {
        string[] names = ["core/Time/TimeSpan.gf", "core/Text.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/IHashable.gf", "core/Hashing/primitive_hashing.gf", "core/Exception.gf"];
        var sources = names.Select(name => new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",name))))
            .Append(new SourceFile("parse-contract.gf", """
                using std;
                namespace Memory{public void*? Allocate(nuint size){return null;}public void Free(void* value)nothrow{}}
                bool Convert(readonly(char)* input,nuint count,TimeSpan* result)nothrow{
                    bool first=TimeSpan::TryParse(input,count,result);bool second=TimeSpan::TryParse(input,result);return first&&second;
                }
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true});
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Convert\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke",body);
        string[] calls=Regex.Matches(body,@"\bcall (?:(?:zeroext|signext) )?i\d+ @(?<symbol>[^\s(]+\$TryParse[^\s(]*)\(")
            .Select(match=>match.Groups["symbol"].Value).Distinct().ToArray();
        Assert.Equal(2,calls.Length);
        foreach(string symbol in calls){string definition=Assert.Single(ir.Split('\n'),line=>line.StartsWith("define ",StringComparison.Ordinal)&&line.Contains("@"+symbol+"(",StringComparison.Ordinal));
            Assert.Contains(" nounwind",definition);}
    }

    private static List<(string Text,long Ticks)> ValidRows()
    {
        var rows=new List<(string Text,long Ticks)>();
        var ticks=new SortedSet<long>{long.MinValue,long.MinValue+1,long.MaxValue-1,long.MaxValue,-1,0,1};
        foreach(long scale in new long[]{10000,10000000,600000000,36000000000,864000000000})
            foreach(long multiplier in new long[]{-25,-1,1,25})foreach(long delta in new long[]{-1,0,1})ticks.Add(scale*multiplier+delta);
        var random=new Random(267100);
        for(int sample=0;sample<160;sample++)ticks.Add(random.NextInt64(long.MinValue,long.MaxValue));
        foreach(long tick in ticks)rows.Add((new TimeSpan(tick).ToString("c",CultureInfo.InvariantCulture),tick));
        // This generator defines the strict grammar and computes ticks independently with BigInteger.
        for(int digits=0;digits<=7;digits++)for(int sample=0;sample<24;sample++){
            int days=sample%3==0?0:random.Next(0,10675200);int hours=random.Next(24),minutes=random.Next(60),seconds=random.Next(60);
            int fraction=digits==0?0:random.Next((int)BigInteger.Pow(10,digits));bool negative=sample%2==0;
            string dayText=days.ToString(CultureInfo.InvariantCulture).PadLeft(sample%4+1,'0');
            bool includeDays=days!=0||sample%3==0;
            string text=(negative?"-":"")+(includeDays?dayText+".":"")+$"{hours:00}:{minutes:00}:{seconds:00}"+
                (digits==0?"":"."+fraction.ToString(CultureInfo.InvariantCulture).PadLeft(digits,'0'));
            BigInteger magnitude=((BigInteger)days*86400+hours*3600+minutes*60+seconds)*10000000+
                (digits==0?BigInteger.Zero:(BigInteger)fraction*BigInteger.Pow(10,7-digits));
            BigInteger expected=negative?-magnitude:magnitude;
            if(text.Length<=26&&expected>=long.MinValue&&expected<=long.MaxValue)rows.Add((text,(long)expected));
        }
        rows.Add(("-00:00:00",0));rows.Add(("-0.00:00:00.0000000",0));
        rows.Add(("000000000.00:00:00.0000001",1));rows.Add(("00:00:01.5",15000000));
        return rows;
    }
    private static void WriteText(BinaryWriter writer,byte[] text){writer.Write(text.Length);writer.Write(text);}
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-timespan-parse-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string program,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("timespan-parse-test.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(executable,arguments,30000);Assert.True(result.ExitCode==0,$"TimeSpan parse exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
