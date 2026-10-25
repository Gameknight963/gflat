using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

// Exercise the civil value against independent calendar and exact-integer oracles.
public sealed class StandardDateTimeTests
{
    private const long Day=864000000000L,Maximum=3155378975999999999L;
    private const string Support="""
        using std;extern void*? malloc(nuint size);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
        long ReadLong(readonly(char)* text,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(text,ref at)<<shift);return (long)value;}
        nuint ReadText(readonly(char)* text,ref nuint at,char* output)nothrow{nuint count=(nuint)ReadByte(text,ref at);for(nuint i=0;i<count;i++)output[i]=(char)ReadByte(text,ref at);output[count]='\0';return count;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnspecifiedDateTimeOracleCoversComponentsCalendarClampingOrderAndTickArithmetic(int optimization)
    {
        var values=Values();using var data=new MemoryStream();
        using(var writer=new BinaryWriter(data,Encoding.UTF8,true))foreach(var value in values){
            writer.Write(value.Ticks);writer.Write((long)(value.Ticks/Day));writer.Write(value.TimeOfDay.Ticks);
            // Calendar operations are checked against .NET only when in range.
            int months=value.Year>1&&value.Year<9999?-1:0,years=value.Year<9999?1:0;
            writer.Write((long)months);writer.Write(value.AddMonths(months).Ticks);
            writer.Write((long)years);writer.Write(value.AddYears(years).Ticks);
        }
        string hex=Convert.ToHexString(data.ToArray());
        Run(Support+$$"""
            int main(){readonly(char)* data="{{hex}}";nuint at=0;DateTime previous=new();int before=attempts;deny=true;
                DateTime minimum=new();if(minimum.Ticks!=0l||minimum.Date.DayNumber!=0||minimum.TimeOfDay.Ticks!=0l)return 1;
                for(int row=0;row<{{values.Count}};row++){
                    long ticks=ReadLong(data,ref at);int day=(int)ReadLong(data,ref at);long clock=ReadLong(data,ref at);
                    int months=(int)ReadLong(data,ref at);long afterMonths=ReadLong(data,ref at);int years=(int)ReadLong(data,ref at);long afterYears=ReadLong(data,ref at);
                    DateTime value=DateTime::FromTicks(ticks);immutable(DateTime)* view=&value;
                    Date date=view.Date;TimeOfDay time=view.TimeOfDay;
                    if(date.DayNumber!=day||time.Ticks!=clock||view.Ticks!=ticks)return 2;
                    DateTime constructed=new(Date::FromDayNumber(day),TimeOfDay::FromTicks(clock));
                    if(!view.Equals(&constructed)||view.CompareTo(&constructed)!=0||view.GetHashCode()!=constructed.GetHashCode())return 3;
                    int expected=0;if(ticks>previous.Ticks)expected=1;else if(ticks<previous.Ticks)expected=-1;
                    if(view.CompareTo(&previous)!=expected||previous.CompareTo(view)!=-expected)return 4;
                    TimeSpan difference=view.Difference(previous);if(difference.Ticks!=ticks-previous.Ticks)return 5;
                    DateTime recovered=previous.Add(difference);if(recovered.Ticks!=ticks)return 6;
                    DateTime shiftedMonths=view.AddMonths(months);DateTime shiftedYears=view.AddYears(years);
                    if(shiftedMonths.Ticks!=afterMonths||shiftedYears.Ticks!=afterYears||shiftedMonths.TimeOfDay.Ticks!=clock||shiftedYears.TimeOfDay.Ticks!=clock)return 7;
                    if(value.Ticks!=ticks)return 8;previous=value;
                }
                if(at!={{hex.Length}}||attempts!=before)return 9;deny=false;if(allocated!=released)return 10;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactParsingAndCanonicalFormattingMatchHostTicksAtEveryCapacity(int optimization)
    {
        var rows=Values().Select(value=>(value,Input:Canonical(value))).ToList();
        foreach(string input in new[]{"2000-02-29T01:02:03.4","0001-01-01T00:00:00.0000000","2024-01-31T23:59:59.123","9999-12-31T23:59:59.9999999"}){
            int fraction=input.Length-20;string format="yyyy-MM-dd'T'HH:mm:ss."+new string('f',fraction);
            rows.Add((System.DateTime.ParseExact(input,format,CultureInfo.InvariantCulture,DateTimeStyles.None),input));
        }
        using var data=new MemoryStream();using(var writer=new BinaryWriter(data,Encoding.UTF8,true))foreach(var row in rows){writer.Write(row.value.Ticks);WriteText(writer,row.Input);WriteText(writer,Canonical(row.value));}
        string hex=Convert.ToHexString(data.ToArray());
        Run(Support+$$"""
            int main(){readonly(char)* data="{{hex}}";nuint at=0;char[64] input;char[32] canonical;int before=attempts;deny=true;
                for(int row=0;row<{{rows.Count}};row++){
                    long ticks=ReadLong(data,ref at);nuint length=ReadText(data,ref at,input);nuint expectedLength=ReadText(data,ref at,canonical);
                    DateTime counted=new();DateTime cstring=new();
                    if(!DateTime::TryParse(input,length,&counted)||!DateTime::TryParse(input,&cstring)||counted.Ticks!=ticks||cstring.Ticks!=ticks)return 1;
                    DateTime parsed=DateTime::Parse(input,length);DateTime parsedCString=DateTime::Parse(input);
                    if(parsed.Ticks!=ticks||parsedCString.Ticks!=ticks)return 2;
                    immutable(DateTime)* view=&counted;
                    for(nuint capacity=0;capacity<=28;capacity++){
                        char[32] output;for(nuint i=0;i<32;i++)output[i]='!';nuint written=99;
                        bool success=view.TryFormat(&output[1],capacity,ref written);
                        if(success!=(capacity>=expectedLength))return 3;
                        if(!success){if(written!=0)return 4;for(nuint i=0;i<32;i++)if(output[i]!='!')return 5;}
                        else{if(written!=expectedLength||output[0]!='!'||output[written+1]!='!')return 6;
                            for(nuint i=0;i<written;i++)if(output[i+1]!=canonical[i])return 7;
                            DateTime roundtrip=DateTime::Parse(&output[1],written);if(roundtrip.Ticks!=ticks)return 8;}
                    }
                    if(counted.Ticks!=ticks)return 9;
                }
                if(at!={{hex.Length}}||attempts!=before)return 10;deny=false;if(allocated!=released)return 11;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidInputRangeOverflowBackedAliasesAndWriterFailurePreserveTheirContracts(int optimization)
    {
        string[] invalid=["","0000-01-01T00:00:00","10000-01-01T00:00:00","2023-02-29T00:00:00","2000-13-01T00:00:00","2000-01-00T00:00:00",
            "2000-01-01t00:00:00","2000-01-01 00:00:00","2000-01-01T24:00:00","2000-01-01T00:60:00","2000-01-01T00:00:60",
            "2000-01-01T00:00:00.","2000-01-01T00:00:00.12345678","2000-01-01T00:00:00Z","2000-01-01T00:00:00+00:00",
            " 2000-01-01T00:00:00","2000-01-01T00:00:00 ","2000-01-01T00:00:00\0tail","2000-01-01T00:00:0\u0080"];
        var arithmetic=(from ticks in new[]{0L,1L,Maximum/2,Maximum-1,Maximum}
                        from delta in new[]{long.MinValue,-Maximum,-1L,0L,1L,Maximum,long.MaxValue}
                        let exact=(BigInteger)ticks+delta select(ticks,delta,Valid:exact>=0&&exact<=Maximum,Result:exact>=0&&exact<=Maximum?(long)exact:0L)).ToArray();
        using var data=new MemoryStream();using(var writer=new BinaryWriter(data,Encoding.UTF8,true)){
            foreach(string input in invalid){WriteText(writer,input);writer.Write((byte)(input=="2000-01-01T00:00:00\0tail"?1:0));}
            foreach(var row in arithmetic){writer.Write(row.ticks);writer.Write(row.delta);writer.Write((byte)(row.Valid?1:0));writer.Write(row.Result);}
        }
        string hex=Convert.ToHexString(data.ToArray());
        Run(Support+$$"""
            struct Writer:ITextWriter{
                public int Calls;public bool Fail;public bool Bad;
                public Writer(){Calls=0;Fail=false;Bad=false;}
                public void Write(readonly(char)* text,nuint count){
                    Calls++;if(Fail)throw new Exception("writer",73);
                    if(count!=27)Bad=true;
                    else for(nuint i=0;i<count;i++)if(text[i]!="2000-02-29T12:34:56.1234567"[i])Bad=true;
                }
            }
            int main(){readonly(char)* data="{{hex}}";nuint at=0;char[80] input;
                for(int row=0;row<{{invalid.Length}};row++){
                    nuint length=ReadText(data,ref at,input);bool cstringValid=ReadByte(data,ref at)!=0;DateTime value=DateTime::FromTicks(123l);int before=attempts;deny=true;
                    if(DateTime::TryParse(input,length,&value)||value.Ticks!=0l||attempts!=before)return 1;
                    value=DateTime::FromTicks(123l);if(DateTime::TryParse(input,&value)!=cstringValid||attempts!=before)return 17;
                    if(cstringValid){DateTime expected=DateTime::Parse("2000-01-01T00:00:00");if(value.Ticks!=expected.Ticks)return 18;}else if(value.Ticks!=0l)return 19;deny=false;
                    value=DateTime::FromTicks(123l);bool caught=false;
                    try{value=DateTime::Parse(input,length);}catch(ref Exception error){caught=true;}
                    if(!caught||value.Ticks!=123l)return 2;
                }
                readonly(char)*? absent=null;nuint[3] rejected=[0,28,(nuint)0-1];
                for(nuint row=0;row<3;row++){DateTime value=DateTime::FromTicks(123l);if(DateTime::TryParse(absent!,rejected[row],&value)||value.Ticks!=0l)return 3;}
                for(int row=0;row<{{arithmetic.Length}};row++){
                    long ticks=ReadLong(data,ref at);long delta=ReadLong(data,ref at);bool valid=ReadByte(data,ref at)!=0;long expected=ReadLong(data,ref at);
                    DateTime source=DateTime::FromTicks(ticks);DateTime result=DateTime::FromTicks(123l);bool caught=false;int before=attempts;deny=valid;
                    try{result=source.Add(TimeSpan::FromTicks(delta));}catch(ref Exception error){caught=true;}deny=false;
                    if(caught==valid||source.Ticks!=ticks)return 4;
                    if(valid){if(result.Ticks!=expected||attempts!=before)return 5;}else if(result.Ticks!=123l)return 6;
                }
                long[4] badTicks=[-1l,3155378976000000000l,(-9223372036854775807l-1l),9223372036854775807l];
                for(nuint row=0;row<4;row++){DateTime result=DateTime::FromTicks(123l);bool caught=false;try{result=DateTime::FromTicks(badTicks[row]);}catch(ref Exception error){caught=true;}if(!caught||result.Ticks!=123l)return 7;}
                // Fully backed and aligned receiver/output overlap; metadata is separate.
                if(sizeof(DateTime)>16)return 20;
                DateTime specimen=DateTime::Parse("2000-02-29T12:34:56.1234567");long ticks=specimen.Ticks;
                for(nuint offset=0;offset<=32;offset+=8)for(nuint capacity=0;capacity<=28;capacity++){
                    DateTime[16] storage;char* bytes=(char*)&storage[0];for(nuint i=0;i<(nuint)sizeof(DateTime)*16;i++)bytes[i]='!';storage[2]=specimen;
                    char[256] saved;for(nuint i=0;i<(nuint)sizeof(DateTime)*16;i++)saved[i]=bytes[i];nuint written=99;int before=attempts;deny=true;
                    bool success=storage[2].TryFormat(bytes+offset,capacity,ref written);deny=false;
                    if(success!=(capacity>=27)||attempts!=before)return 8;
                    if(!success){if(written!=0||storage[2].Ticks!=ticks)return 9;for(nuint i=0;i<(nuint)sizeof(DateTime)*16;i++)if(bytes[i]!=saved[i])return 10;}
                    else{if(written!=27)return 11;for(nuint i=0;i<(nuint)sizeof(DateTime)*16;i++){
                        if(i>=offset&&i<offset+27){if(bytes[i]!="2000-02-29T12:34:56.1234567"[i-offset])return 12;}else if(bytes[i]!=saved[i])return 13;
                    }
                    }
                }
                DateTime[16] parsedStorage;char* borrowed=(char*)&parsedStorage[0];
                for(nuint i=0;i<27;i++)borrowed[i]="2000-02-29T12:34:56.1234567"[i];
                if(!DateTime::TryParse(borrowed,27,&parsedStorage[0])||parsedStorage[0].Ticks!=ticks)return 14;
                Writer writer=new();int before=attempts;deny=true;specimen.Format(&writer);deny=false;
                if(writer.Calls!=1||writer.Bad||attempts!=before)return 21;
                writer.Fail=true;bool caught=false;try{specimen.Format(&writer);}catch(ref Exception error){if(error.GetCode()!=73)return 15;caught=true;}
                {String text=s$"{specimen}";if(!text.Equals("2000-02-29T12:34:56.1234567"))return 22;}
                if(!caught||writer.Calls!=2||specimen.Ticks!=ticks||at!={{hex.Length}}||allocated!=released)return 16;return 0;
            }
            """,optimization);
    }

    [Fact]
    public void SelectedPureSourcesNeedNoStringAndTryObserversHaveNoExceptionBoundary()
    {
        string[] names=["DateTime.gf","Date.gf","DayOfWeek.gf","TimeOfDay.gf","TimeSpan.gf","Text.gf",
            "IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("date-time-contract.gf","""
                namespace Memory{
                    public void*? Allocate(nuint count){return null;}
                    public void Free(void* storage)nothrow{}
                }
                bool Observe(immutable(std::DateTime)* value,readonly(char)* text,nuint count,char* output,nuint capacity,ref nuint written)nothrow{
                    std::DateTime parsed=new();
                    return std::DateTime::TryParse(text,count,&parsed)&&value.TryFormat(output,capacity,ref written)&&value.Equals(&parsed);
                }
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        string body=System.Text.RegularExpressions.Regex.Match(ir,
            @"(?ms)^define [^\r\n]*@gflat\$Observe\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke ",body);Assert.DoesNotContain("__gflat_eh_",body);
        Assert.DoesNotContain("$String",ir);
        // These known CRT imports cannot be supplied by an accidental full-std load.
        // Whole-module EH declarations remain allowed: checked factories also emit.
        Assert.DoesNotMatch(@"(?m)^declare\s+[^\r\n]*@(?:malloc|free|memcpy|memmove|memset|strlen|printf|puts|fwrite|fflush)\(",ir);
    }

    private static List<System.DateTime> Values()
    {
        long[] fixedTicks=[0,1,9999999,10000000,Day-1,Day,Maximum-1,Maximum,
            new System.DateTime(2000,2,29,23,59,59,DateTimeKind.Unspecified).Ticks+1234567,
            new System.DateTime(1900,3,31,12,0,0,DateTimeKind.Unspecified).Ticks,
            new System.DateTime(2024,1,31,12,0,0,DateTimeKind.Unspecified).Ticks];
        var values=fixedTicks.Select(ticks=>new System.DateTime(ticks,DateTimeKind.Unspecified)).ToList();
        var random=new Random(791683);for(int i=0;i<40;i++)values.Add(new System.DateTime(random.NextInt64(Maximum),DateTimeKind.Unspecified));return values;
    }
    private static string Canonical(System.DateTime value)=>value.ToString(value.Ticks%10000000==0?"yyyy-MM-dd'T'HH:mm:ss":"yyyy-MM-dd'T'HH:mm:ss.fffffff",CultureInfo.InvariantCulture);
    private static void WriteText(BinaryWriter writer,string text){byte[] bytes=Encoding.Latin1.GetBytes(text);writer.Write((byte)bytes.Length);writer.Write(bytes);}
    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("date-time.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"DateTime exit {result.ExitCode}: {result.StandardError}");
    }
}
