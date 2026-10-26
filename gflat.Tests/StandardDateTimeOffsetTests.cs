using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class StandardDateTimeOffsetTests
{
    private const long Minute=600000000L,Maximum=3155378975999999999L;
    private const string Support="""
        using std; using std::Time;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
        long ReadLong(readonly(char)* data,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(data,ref at)<<shift);return (long)value;}
        nuint ReadText(readonly(char)* data,ref nuint at,char* output)nothrow{nuint count=(nuint)ReadByte(data,ref at);for(nuint i=0;i<count;i++)output[i]=(char)ReadByte(data,ref at);output[count]='\0';return count;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitOffsetAndInstantOraclesCoverRangeEdgesEqualityAndToOffsetWithoutNormalAllocation(int optimization)
    {
        var rows=Values();using var bytes=new MemoryStream();long previous=0;
        using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))for(int i=0;i<rows.Count;i++){
            var value=rows[i];var offset=TimeSpan.FromMinutes(new[]{-840,-1,0,1,840}[i%5]);
            bool valid=true;long wall=0;try{wall=value.ToOffset(offset).Ticks;}catch(ArgumentException){valid=false;}
            writer.Write(value.Ticks);writer.Write(value.UtcTicks);writer.Write(value.Offset.Ticks);writer.Write(offset.Ticks);writer.Write((byte)(valid?1:0));writer.Write(wall);
            writer.Write((byte)(Math.Sign(value.UtcTicks-previous)+1));previous=value.UtcTicks;
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;DateTimeOffset previous=new();
                DateTimeOffset minimum=new();if(minimum.DateTime.Ticks!=0l||minimum.UtcDateTime.Ticks!=0l||minimum.Offset.Ticks!=0l)return 1;
                for(int row=0;row<{{{rows.Count}}};row++){
                    long wall=ReadLong(data,ref at);long utc=ReadLong(data,ref at);long offset=ReadLong(data,ref at);long selected=ReadLong(data,ref at);bool valid=ReadByte(data,ref at)!=0;long shifted=ReadLong(data,ref at);int expectedComparison=ReadByte(data,ref at)-1;
                    int before=attempts;deny=true;DateTimeOffset value=new(DateTime::FromTicks(wall),TimeSpan::FromTicks(offset));immutable(DateTimeOffset)* view=&value;
                    if(view.DateTime.Ticks!=wall||view.UtcDateTime.Ticks!=utc||view.Offset.Ticks!=offset)return 2;
                    DateTimeOffset copy=value;DateTimeOffset asUtc=view.ToOffset(TimeSpan::FromTicks(0l));
                    if(!view.Equals(&copy)||!view.EqualsExact(&copy)||!view.Equals(&asUtc)||view.GetHashCode()!=asUtc.GetHashCode()||asUtc.DateTime.Ticks!=utc)return 3;
                    if(view.EqualsExact(&asUtc)!=(offset==0l)||view.CompareTo(&previous)!=expectedComparison||previous.CompareTo(view)!=-expectedComparison)return 4;
                    DateTimeOffset result=value;
                    if(valid){result=view.ToOffset(TimeSpan::FromTicks(selected));if(result.DateTime.Ticks!=shifted||result.Offset.Ticks!=selected||!result.Equals(view)||result.UtcDateTime.Ticks!=utc||attempts!=before)return 5;deny=false;}
                    else{if(attempts!=before)return 6;deny=false;bool caught=false;try{result=view.ToOffset(TimeSpan::FromTicks(selected));}catch(ref Exception error){caught=true;}if(!caught||!result.EqualsExact(view))return 7;}
                    if(!value.EqualsExact(&copy))return 8;previous=value;
                }
                if(at!={{{hex.Length}}}||allocated!=released)return 9;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitSuffixGrammarAndCanonicalBuffersMatchDateTimeOffsetOraclesAtEveryCapacity(int optimization)
    {
        var rows=Values().Select(value=>(Value:value,Input:Canonical(value))).ToList();
        var clock=new System.DateTime(2000,2,29,12,34,56,DateTimeKind.Unspecified);
        rows.Add((new System.DateTimeOffset(clock.AddTicks(1000000),TimeSpan.Zero),"2000-02-29T12:34:56.1Z"));
        rows.Add((new System.DateTimeOffset(clock.AddTicks(1230000),TimeSpan.FromMinutes(150)),"2000-02-29T12:34:56.123+02:30"));
        rows.Add((new System.DateTimeOffset(clock,TimeSpan.Zero),"2000-02-29T12:34:56-00:00"));
        rows.Add((new System.DateTimeOffset(clock,TimeSpan.Zero),"2000-02-29T12:34:56+00:00"));
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var row in rows){
            WriteText(writer,row.Input);WriteText(writer,Canonical(row.Value));writer.Write(row.Value.Ticks);writer.Write(row.Value.UtcTicks);writer.Write(row.Value.Offset.Ticks);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;char[80] input;char[34] expected;int before=attempts;deny=true;
                for(int row=0;row<{{{rows.Count}}};row++){
                    nuint length=ReadText(packet,ref at,input);nuint expectedLength=ReadText(packet,ref at,expected);long wall=ReadLong(packet,ref at);long utc=ReadLong(packet,ref at);long offset=ReadLong(packet,ref at);
                    DateTimeOffset counted=new();DateTimeOffset cstring=new();
                    if(!DateTimeOffset::TryParse(input,length,&counted)||!DateTimeOffset::TryParse(input,&cstring)||!counted.EqualsExact(&cstring))return 1;
                    if(counted.DateTime.Ticks!=wall||counted.UtcDateTime.Ticks!=utc||counted.Offset.Ticks!=offset)return 2;
                    DateTimeOffset parsed=DateTimeOffset::Parse(input,length);DateTimeOffset parsedCString=DateTimeOffset::Parse(input);
                    if(!parsed.EqualsExact(&counted)||!parsedCString.EqualsExact(&counted))return 3;
                    input[length]='X';if(!DateTimeOffset::TryParse(input,length,&parsed)||!parsed.EqualsExact(&counted))return 4;
                    immutable(DateTimeOffset)* view=&counted;
                    for(nuint capacity=0;capacity<=34;capacity++){
                        char[40] output;for(nuint i=0;i<40;i++)output[i]='!';nuint written=99;
                        bool success=view.TryFormat(&output[1],capacity,ref written);if(success!=(capacity>=expectedLength))return 5;
                        if(!success){if(written!=0)return 6;for(nuint i=0;i<40;i++)if(output[i]!='!')return 7;}
                        else{if(written!=expectedLength||output[0]!='!'||output[written+1]!='!')return 8;for(nuint i=0;i<written;i++)if(output[i+1]!=expected[i])return 9;}
                    }
                    if(counted.DateTime.Ticks!=wall||counted.Offset.Ticks!=offset)return 10;
                }
                if(at!={{{hex.Length}}}||attempts!=before)return 11;deny=false;if(allocated!=released)return 12;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidInputsOffsetsUtcBoundsBackedAliasesAndWriterFailuresPreserveTheirDocumentedState(int optimization)
    {
        string[] invalid=["","2000-01-01T00:00:00","2000-01-01T00:00:00z","2000-01-01t00:00:00Z","2000-01-01 00:00:00Z",
            " 2000-01-01T00:00:00Z","2000-01-01T00:00:00Z ","2000-01-01T00:00:00+14:01","2000-01-01T00:00:00-14:01",
            "2000-01-01T00:00:00+15:00","2000-01-01T00:00:00+00:60","2000-01-01T00:00:00+1:00","2000-01-01T00:00:00+0100",
            "2000-01-01T00:00:00+01:00:00","2000-01-01T00:00:00+01:00Z","2000-01-01T00:00:60Z","2001-02-29T00:00:00Z",
            "2000-01-01T00:00:00.12345678Z","0001-01-01T00:00:00+00:01","9999-12-31T23:59:59.9999999-00:01",
            "2000-01-01T00:00:00\u0080","2000-01-01T00:00:00Z\0suffix"];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(string text in invalid){WriteText(writer,text);writer.Write((byte)(text.Contains('\0')?1:0));}
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            struct Writer:ITextWriter{public int Calls;public char First;public Writer(){Calls=0;First='!';}public void Write(readonly(char)* data,nuint count){Calls++;First=data[0];throw new Exception("writer",73);}}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;char[96] input;DateTimeOffset zero=new();
                DateTimeOffset saved=DateTimeOffset::Parse("2024-01-02T03:04:05.1234567+05:45");
                for(int row=0;row<{{{invalid.Length}}};row++){
                    nuint length=ReadText(packet,ref at,input);bool cstringValid=ReadByte(packet,ref at)!=0;DateTimeOffset value=saved;int before=attempts;deny=true;
                    if(DateTimeOffset::TryParse(input,length,&value)||!value.EqualsExact(&zero))return 1;
                    value=saved;if(DateTimeOffset::TryParse(input,&value)!=cstringValid||attempts!=before)return 2;
                    if(cstringValid){DateTimeOffset expected=DateTimeOffset::Parse("2000-01-01T00:00:00Z");if(!value.EqualsExact(&expected))return 3;}else if(!value.EqualsExact(&zero))return 4;deny=false;
                    value=saved;bool caught=false;try{value=DateTimeOffset::Parse(input,length);}catch(ref Exception error){caught=true;}if(!caught||!value.EqualsExact(&saved))return 5;
                }
                readonly(char)*? absent=null;nuint[4] impossible=[0,19,34,(nuint)0-1];
                for(nuint i=0;i<4;i++){DateTimeOffset value=saved;if(DateTimeOffset::TryParse(absent!,impossible[i],&value)||!value.EqualsExact(&zero))return 6;}
                long[8] offsets=[1l,-1l,600000001l,-600000001l,504600000000l,-504600000000l,(-9223372036854775807l-1l),9223372036854775807l];
                for(nuint i=0;i<8;i++){
                    DateTimeOffset value=saved;bool caught=false;try{value=new DateTimeOffset(saved.DateTime,TimeSpan::FromTicks(offsets[i]));}catch(ref Exception error){caught=true;}if(!caught||!value.EqualsExact(&saved))return 7;
                    caught=false;try{value=saved.ToOffset(TimeSpan::FromTicks(offsets[i]));}catch(ref Exception error){caught=true;}if(!caught||!value.EqualsExact(&saved))return 8;
                }
                if(sizeof(DateTimeOffset)>32)return 9;
                for(nuint offset=0;offset<=32;offset+=8)for(nuint capacity=32;capacity<=33;capacity++){
                    DateTimeOffset[8] storage;char* data=(char*)&storage[0];for(nuint i=0;i<(nuint)sizeof(DateTimeOffset)*8;i++)data[i]='!';storage[1]=saved;
                    char[256] prior;for(nuint i=0;i<(nuint)sizeof(DateTimeOffset)*8;i++)prior[i]=data[i];nuint written=99;int before=attempts;deny=true;
                    bool success=storage[1].TryFormat(data+offset,capacity,ref written);deny=false;
                    if(success!=(capacity==33)||attempts!=before)return 10;
                    if(!success){if(written!=0||!storage[1].EqualsExact(&saved))return 11;for(nuint i=0;i<(nuint)sizeof(DateTimeOffset)*8;i++)if(data[i]!=prior[i])return 12;}
                    else{if(written!=33)return 13;for(nuint i=0;i<(nuint)sizeof(DateTimeOffset)*8;i++){
                        if(i>=offset&&i<offset+33){if(data[i]!="2024-01-02T03:04:05.1234567+05:45"[i-offset])return 14;}else if(data[i]!=prior[i])return 15;
                    }}
                }
                DateTimeOffset[8] parseStorage;char* borrowed=(char*)&parseStorage[0];for(nuint i=0;i<33;i++)borrowed[i]="2024-01-02T03:04:05.1234567+05:45"[i];
                if(!DateTimeOffset::TryParse(borrowed,33,&parseStorage[0])||!parseStorage[0].EqualsExact(&saved))return 16;
                Writer writer=new();bool caught=false;try{saved.Format(&writer);}catch(ref Exception error){if(error.GetCode()!=73)return 17;caught=true;}
                if(!caught||writer.Calls!=1||writer.First!='2'||saved.Offset.Ticks!=207000000000l||at!={{{hex.Length}}}||allocated!=released)return 18;return 0;
            }
            """,optimization);
    }

    private static List<System.DateTimeOffset> Values()
    {
        List<(long Wall,int Minutes)> tuples=[(0,0),(Maximum,0),(0,-840),(840*Minute,840),(Maximum,840),(Maximum-840*Minute,-840),
            (System.DateTime.UnixEpoch.Ticks,0),(System.DateTime.UnixEpoch.Ticks+1234567,345),(System.DateTime.UnixEpoch.Ticks-1,-1)];
        var random=new Random(119753);for(int i=0;i<32;i++){long utc=random.NextInt64(Maximum);int offset=random.Next(-840,841);long wall=utc+offset*Minute;if(wall>=0&&wall<=Maximum)tuples.Add((wall,offset));}
        return tuples.Select(value=>new System.DateTimeOffset(new System.DateTime(value.Wall,DateTimeKind.Unspecified),TimeSpan.FromMinutes(value.Minutes))).ToList();
    }
    private static string Canonical(System.DateTimeOffset value)
    {
        string clock=value.DateTime.ToString(value.Ticks%10000000==0?"yyyy-MM-dd'T'HH:mm:ss":"yyyy-MM-dd'T'HH:mm:ss.fffffff",CultureInfo.InvariantCulture);
        int minutes=(int)value.Offset.TotalMinutes;if(minutes==0)return clock+"Z";
        int magnitude=Math.Abs(minutes);return clock+(minutes<0?"-":"+")+(magnitude/60).ToString("D2",CultureInfo.InvariantCulture)+":"+(magnitude%60).ToString("D2",CultureInfo.InvariantCulture);
    }
    private static void WriteText(BinaryWriter writer,string text){byte[] bytes=Encoding.UTF8.GetBytes(text);writer.Write((byte)bytes.Length);writer.Write(bytes);}
    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("date-time-offset.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"DateTimeOffset exit {result.ExitCode}: {result.StandardError}");
    }
}
