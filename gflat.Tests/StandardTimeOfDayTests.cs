using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardTimeOfDayTests
{
    private const long Day=864000000000L;
    private const string Counters="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        int ReadByte(readonly(char)* text,ref nuint position)nothrow{int value=(Digit(text[position])<<4)|Digit(text[position+1]);position+=2;return value;}
        int ReadWord(readonly(char)* text,ref nuint position)nothrow{int low=ReadByte(text,ref position);return low|(ReadByte(text,ref position)<<8);}
        long ReadLong(readonly(char)* text,ref nuint position)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(text,ref position)<<shift);return (long)value;}
        nuint ReadText(readonly(char)* text,ref nuint position,char* output)nothrow{nuint count=(nuint)ReadByte(text,ref position);for(nuint i=0;i<count;i++)output[i]=(char)ReadByte(text,ref position);output[count]='\0';return count;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TimeOnlyOracleCoversExactTicksComponentsOrderStrictFractionParsingAndAllCapacities(int optimization)
    {
        var ticks=new List<long>{0,1,9999,10000,9999999,10000000,599999999,600000000,35999999999,36000000000,Day/2,Day-10000,Day-1};
        var random=new Random(520173);for(int i=0;i<32;i++)ticks.Add(random.NextInt64(Day));
        var rows=ticks.Select(value=>(Value:new TimeOnly(value),Text:Canonical(new TimeOnly(value)))).ToList();
        foreach(string text in new[]{"01:02:03.4","12:34:56.12","12:34:56.123","00:00:00.0000000","00:00:00.0","23:59:59.9999999"})
            rows.Add((TimeOnly.ParseExact(text,"HH:mm:ss."+new string('f',text.Length-9),CultureInfo.InvariantCulture),text));
        using var data=new MemoryStream();long previous=0;
        using(var writer=new BinaryWriter(data,Encoding.UTF8,leaveOpen:true))foreach(var row in rows){
            writer.Write(row.Value.Ticks);writer.Write((byte)row.Value.Hour);writer.Write((byte)row.Value.Minute);writer.Write((byte)row.Value.Second);writer.Write((ushort)row.Value.Millisecond);
            writer.Write(row.Value.Ticks-row.Value.Ticks%10000);writer.Write((byte)(Math.Sign(row.Value.Ticks-previous)+1));previous=row.Value.Ticks;
            WriteText(writer,row.Text);WriteText(writer,Canonical(row.Value));
        }
        string hex=Convert.ToHexString(data.ToArray());
        Run(Counters+$$"""
            int main(){
                readonly(char)* data="{{hex}}";nuint position=0;char[64] input;char[64] canonical;TimeOfDay previous=new();
                int before=attempts;deny=true;
                for(int row=0;row<{{rows.Count}};row++){
                    long ticks=ReadLong(data,ref position);int hour=ReadByte(data,ref position);int minute=ReadByte(data,ref position);int second=ReadByte(data,ref position);int millisecond=ReadWord(data,ref position);
                    long constructorTicks=ReadLong(data,ref position);int comparison=ReadByte(data,ref position)-1;
                    nuint length=ReadText(data,ref position,input);nuint canonicalLength=ReadText(data,ref position,canonical);
                    TimeOfDay value=TimeOfDay::FromTicks(ticks);immutable(TimeOfDay)* view=&value;
                    if(view.Ticks!=ticks||view.Hour!=hour||view.Minute!=minute||view.Second!=second||view.Millisecond!=millisecond)return 1;
                    TimeOfDay constructed=new(hour,minute,second,millisecond);if(constructed.Ticks!=constructorTicks)return 2;
                    TimeOfDay counted=new();TimeOfDay cstring=new();
                    if(!TimeOfDay::TryParse(input,length,&counted)||!TimeOfDay::TryParse(input,&cstring)||counted.Ticks!=ticks||cstring.Ticks!=ticks)return 3;
                    TimeOfDay parsed=TimeOfDay::Parse(input,length);TimeOfDay parsedCString=TimeOfDay::Parse(input);
                    if(!view.Equals(&counted)||!view.Equals(&parsed)||!view.Equals(&parsedCString)||view.GetHashCode()!=counted.GetHashCode())return 4;
                    TimeSpan span=TimeSpan::FromTicks(ticks);if(view.GetHashCode()!=span.GetHashCode()||view.CompareTo(&previous)!=comparison||previous.CompareTo(view)!=-comparison||view.CompareTo(view)!=0)return 5;
                    previous=value;
                    for(nuint capacity=0;capacity<=17;capacity++){
                        char[20] output;for(nuint i=0;i<20;i++)output[i]='!';nuint written=99;
                        bool success=view.TryFormat(&output[1],capacity,ref written);
                        if(success!=(capacity>=canonicalLength))return 6;
                        if(!success){if(written!=0)return 7;for(nuint i=0;i<20;i++)if(output[i]!='!')return 8;}
                        else{if(written!=canonicalLength||output[0]!='!'||output[written+1]!='!')return 9;
                            for(nuint i=0;i<written;i++)if(output[i+1]!=canonical[i])return 10;
                            TimeOfDay roundtrip=TimeOfDay::Parse(&output[1],written);if(roundtrip.Ticks!=ticks)return 11;}
                    }
                    if(view.Ticks!=ticks)return 12;
                }
                if(position!={{hex.Length}}||attempts!=before)return 13;deny=false;
                TimeOfDay zero=new();if(zero.Ticks!=0l||zero.Hour!=0||zero.Minute!=0||zero.Second!=0||zero.Millisecond!=0)return 14;
                TimeOfDay shortConstructor=new(12,34);if(shortConstructor.Ticks!=452400000000l)return 15;
                if(allocated!=released)return 16;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidGrammarAndComponentExtremesRejectAndTryFailuresResetOnlyAfterReadingInput(int optimization)
    {
        string[] invalid=["","00:00","0:00:00","000:00:00","24:00:00","12:60:00","12:00:60","12:00:00.","12:00:00.12345678","12:00:00..1",
            "-00:00:00","+00:00:00","0.00:00:00","1.00:00:00"," 12:00:00","12:00:00 ","12:00:00Z","12:00:00+01:00","12:00:00\0","12:00:00\0tail","\0:00:00","00:00:0\u0080"];
        string[] formats=["HH:mm:ss",..Enumerable.Range(1,7).Select(digits=>"HH:mm:ss."+new string('f',digits))];
        using var data=new MemoryStream();
        using(var writer=new BinaryWriter(data,Encoding.UTF8,leaveOpen:true))foreach(string text in invalid){
            WriteText(writer,text);string prefix=text.Split('\0')[0];
            bool valid=TimeOnly.TryParseExact(prefix,formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out TimeOnly time);
            writer.Write((byte)(valid?1:0));writer.Write(valid?time.Ticks:0L);
        }
        var bad=new List<int>();foreach(int component in Enumerable.Range(0,4))foreach(int value in new[]{-1,component==0?24:component==3?1000:60,int.MinValue,int.MaxValue}){
            int[] parts=new int[4];parts[component]=value;bad.AddRange(parts);
        }
        string hex=Convert.ToHexString(data.ToArray());
        Run(Counters+$$"""
            int main(){
                readonly(char)* data="{{hex}}";nuint position=0;char[64] input;
                for(int row=0;row<{{invalid.Length}};row++){
                    nuint length=ReadText(data,ref position,input);bool cstringValid=ReadByte(data,ref position)!=0;long cstringTicks=ReadLong(data,ref position);
                    TimeOfDay value=TimeOfDay::FromTicks(123l);int before=attempts;deny=true;
                    if(TimeOfDay::TryParse(input,length,&value)||value.Ticks!=0l)return 1;
                    value=TimeOfDay::FromTicks(123l);
                    if(TimeOfDay::TryParse(input,&value)!=cstringValid||value.Ticks!=cstringTicks||attempts!=before)return 2;deny=false;
                    value=TimeOfDay::FromTicks(123l);bool caught=false;
                    try{value=TimeOfDay::Parse(input,length);}catch(ref Exception error){caught=true;}
                    if(!caught||value.Ticks!=123l)return 3;
                }
                nuint[4] impossible=[0,7,17,(nuint)0-1];
                for(nuint row=0;row<4;row++){TimeOfDay value=TimeOfDay::FromTicks(123l);if(TimeOfDay::TryParse((readonly(char)*)1,impossible[row],&value)||value.Ticks!=0l)return 4;}
                int[{{bad.Count}}] parts=[{{string.Join(',',bad.Select(value=>value==int.MinValue?"(-2147483647-1)":value.ToString(CultureInfo.InvariantCulture)))}}];
                for(nuint row=0;row<{{bad.Count}};row+=4){bool caught=false;
                    try{TimeOfDay value=new(parts[row],parts[row+1],parts[row+2],parts[row+3]);}catch(ref Exception error){caught=true;}
                    if(!caught)return 5;}
                long[4] badTicks=[-1l,864000000000l,(-9223372036854775807l-1l),9223372036854775807l];
                for(nuint row=0;row<4;row++){TimeOfDay kept=TimeOfDay::FromTicks(123l);bool caught=false;
                    try{kept=TimeOfDay::FromTicks(badTicks[row]);}catch(ref Exception error){caught=true;}
                    if(!caught||kept.Ticks!=123l)return 6;}
                if(allocated!=released)return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BackedParseAndFormatOverlapAreStagedAndWriterUsesOneCountedCall(int optimization)
        =>Run(Counters+"""
            struct Writer:ITextWriter{
                public int Calls;public bool Fail;public bool Bad;
                public Writer(){Calls=0;Fail=false;Bad=false;}
                public void Write(readonly(char)* data,nuint count){Calls++;if(Fail)throw new Exception("writer",73);
                    if(count!=16)Bad=true;else for(nuint i=0;i<16;i++)if(data[i]!="12:34:56.1234567"[i])Bad=true;}
            }
            int main(){
                if(sizeof(TimeOfDay)!=8)return 1;
                readonly(char)*[4] texts=["00:00:00","01:02:03.4","23:59:59.9999999","12:34:5X"];
                int[4] lengths=[8,10,16,8];long[4] ticks=[0l,37234000000l,863999999999l,0l];
                int requests=attempts;deny=true;
                for(nuint sample=0;sample<4;sample++)for(nuint offset=0;offset<=16;offset+=4)for(nuint resultIndex=0;resultIndex<4;resultIndex++){
                    TimeOfDay[8] aligned;char* bytes=(char*)&aligned[0];for(nuint i=0;i<64;i++)bytes[i]='!';
                    for(nuint i=0;i<(nuint)lengths[sample];i++)bytes[offset+i]=texts[sample][i];
                    char[64] saved;for(nuint i=0;i<64;i++)saved[i]=bytes[i];
                    bool success=TimeOfDay::TryParse(bytes+offset,(nuint)lengths[sample],&aligned[resultIndex]);
                    if(success!=(sample<3)||aligned[resultIndex].Ticks!=ticks[sample])return 2;
                    for(nuint i=0;i<64;i++)if((i<resultIndex*8||i>=resultIndex*8+8)&&bytes[i]!=saved[i])return 3;
                }
                for(nuint sample=0;sample<3;sample++)for(nuint offset=0;offset<=20;offset+=4)for(nuint capacity=0;capacity<=17;capacity++){
                    TimeOfDay[8] aligned;char* bytes=(char*)&aligned[0];for(nuint i=0;i<64;i++)bytes[i]='!';
                    aligned[2]=TimeOfDay::FromTicks(ticks[sample]);char[64] saved;for(nuint i=0;i<64;i++)saved[i]=bytes[i];
                    readonly(char)* expected=texts[sample];nuint length=(nuint)lengths[sample];if(sample==1){expected="01:02:03.4000000";length=16;}
                    nuint written=99;bool success=aligned[2].TryFormat(bytes+offset,capacity,ref written);
                    if(success!=(capacity>=length))return 4;
                    if(!success){if(written!=0||aligned[2].Ticks!=ticks[sample])return 5;for(nuint i=0;i<64;i++)if(bytes[i]!=saved[i])return 6;}
                    else{if(written!=length)return 7;for(nuint i=0;i<64;i++){
                        if(i>=offset&&i<offset+length){if(bytes[i]!=expected[i-offset])return 8;}else if(bytes[i]!=saved[i])return 9;}}
                }
                TimeOfDay value=TimeOfDay::Parse("12:34:56.1234567");Writer writer=new();value.Format(&writer);
                if(writer.Calls!=1||writer.Bad||attempts!=requests)return 10;deny=false;
                long original=value.Ticks;writer.Fail=true;bool caught=false;
                try{value.Format(&writer);}catch(ref Exception error){if(error.GetCode()!=73)return 11;caught=true;}
                if(!caught||writer.Calls!=2||value.Ticks!=original||allocated!=released)return 12;return 0;
            }
            """,optimization);

    [Fact]
    public void SelectedPureSourcesNeedNoStringAndTryObserversHaveNoExceptionBoundary()
    {
        string[] names=["Time/TimeOfDay.gf","Time/TimeSpan.gf","Text.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("time-contract.gf","""
                namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* storage)nothrow{}}
                bool Observe(immutable(std::TimeOfDay)* value,readonly(char)* text,nuint count,char* output,nuint capacity,ref nuint written)nothrow{
                    std::TimeOfDay parsed=new();return std::TimeOfDay::TryParse(text,count,&parsed)&&value.TryFormat(output,capacity,ref written)&&value.Equals(&parsed);
                }
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Observe\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke ",body);Assert.DoesNotContain("__gflat_eh_",body);Assert.DoesNotContain("$String",ir);
    }

    private static string Canonical(TimeOnly value)=>value.ToString(value.Ticks%10000000==0?"HH:mm:ss":"HH:mm:ss.fffffff",CultureInfo.InvariantCulture);
    private static void WriteText(BinaryWriter writer,string text){byte[] bytes=Encoding.Latin1.GetBytes(text);writer.Write((byte)bytes.Length);writer.Write(bytes);}
    private static void Run(string program,int optimization){
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("time-of-day.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"TimeOfDay exit {result.ExitCode}: {result.StandardError}");
    }
}
