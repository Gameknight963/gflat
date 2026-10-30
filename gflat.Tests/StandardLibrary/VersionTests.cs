using System.Text;

namespace gflat.Tests.Std;

public sealed class VersionTests
{
    private const string Support="""
        using std; using std::Text;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
        int ReadInt(readonly(char)* text,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(text,ref at)<<shift);return (int)value;}
        nuint ReadText(readonly(char)* text,ref nuint at,char* output)nothrow{nuint count=(nuint)ReadByte(text,ref at);for(nuint i=0;i<count;i++)output[i]=(char)ReadByte(text,ref at);output[count]='\0';return count;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TupleComponentsAndAllPairOrderingMatchDotNetIncludingMissingPartsWithoutAllocation(int optimization)
    {
        var values=Values();using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true)){
            foreach(var value in values){writer.Write(value.Major);writer.Write(value.Minor);writer.Write(value.Build);writer.Write(value.Revision);}
            foreach(var left in values)foreach(var right in values)writer.Write((byte)(Math.Sign(left.CompareTo(right))+1));
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;int before=attempts;deny=true;
                Version zero=new();if(zero.Major!=0||zero.Minor!=0||zero.Build!=-1||zero.Revision!=-1)return 1;
                Version[{{{values.Count}}}] values;
                for(nuint i=0;i<{{{values.Count}}};i++){
                    int major=ReadInt(data,ref at);int minor=ReadInt(data,ref at);int build=ReadInt(data,ref at);int revision=ReadInt(data,ref at);
                    values[i]=new Version(major,minor,build,revision);immutable(Version)* view=&values[i];
                    if(view.Major!=major||view.Minor!=minor||view.Build!=build||view.Revision!=revision)return 2;
                    Version copied=values[i];if(!view.Equals(&copied)||view.GetHashCode()!=copied.GetHashCode())return 3;
                }
                for(nuint i=0;i<{{{values.Count}}};i++)for(nuint j=0;j<{{{values.Count}}};j++){
                    int expected=ReadByte(data,ref at)-1;immutable(Version)* left=&values[i];immutable(Version)* right=&values[j];
                    int actual=left.CompareTo(right);int sign=0;if(actual<0)sign=-1;else if(actual>0)sign=1;
                    if(sign!=expected||left.Equals(right)!=(expected==0))return 4;
                    if(expected==0&&left.GetHashCode()!=right.GetHashCode())return 5;
                }
                if(at!={{{hex.Length}}}||attempts!=before)return 6;deny=false;if(allocated!=released)return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrictValidTextsCanonicalizeLikeDotNetAndEveryOutputCapacityPreservesGuards(int optimization)
    {
        var texts=Values().Select(value=>value.ToString()).ToList();
        texts.AddRange(["01.002","000.000.000","01.002.0003.00004",new string('0',80)+"1.0002","0."+new string('0',80),"2147483647.2147483647.2147483647.2147483647"]);
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(string input in texts){
            var value=System.Version.Parse(input);WriteText(writer,input);WriteText(writer,value.ToString());
            writer.Write(value.Major);writer.Write(value.Minor);writer.Write(value.Build);writer.Write(value.Revision);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;char[128] input;char[44] canonical;int before=attempts;deny=true;
                for(int row=0;row<{{{texts.Count}}};row++){
                    nuint length=ReadText(data,ref at,input);nuint expectedLength=ReadText(data,ref at,canonical);
                    int major=ReadInt(data,ref at);int minor=ReadInt(data,ref at);int build=ReadInt(data,ref at);int revision=ReadInt(data,ref at);
                    Version expected=new(major,minor,build,revision);Version counted=new();Version cstring=new();
                    if(!Version::TryParse(input,length,&counted)||!Version::TryParse(input,&cstring)||!counted.Equals(&expected)||!cstring.Equals(&expected))return 1;
                    Version parsed=Version::Parse(input,length);Version parsedCString=Version::Parse(input);
                    if(!parsed.Equals(&expected)||!parsedCString.Equals(&expected))return 2;
                    // The counted overload must not require a terminator after its input.
                    input[length]='X';Version nonterminated=new();if(!Version::TryParse(input,length,&nonterminated)||!nonterminated.Equals(&expected))return 3;
                    immutable(Version)* view=&counted;
                    for(nuint capacity=0;capacity<=44;capacity++){
                        char[48] output;for(nuint i=0;i<48;i++)output[i]='!';nuint written=99;
                        bool success=view.TryFormat(&output[1],capacity,ref written);
                        if(success!=(capacity>=expectedLength))return 4;
                        if(!success){if(written!=0)return 5;for(nuint i=0;i<48;i++)if(output[i]!='!')return 6;}
                        else{if(written!=expectedLength||output[0]!='!'||output[written+1]!='!')return 7;
                            for(nuint i=0;i<written;i++)if(output[i+1]!=canonical[i])return 8;
                            Version roundtrip=Version::Parse(&output[1],written);if(!roundtrip.Equals(&expected))return 9;}
                    }
                    if(!view.Equals(&expected))return 10;
                }
                if(at!={{{hex.Length}}}||attempts!=before)return 11;deny=false;if(allocated!=released)return 12;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidSyntaxAndComponentsResetTryResultsWhileAliasesAndWriterFailuresAreTransactional(int optimization)
    {
        string[] invalid=["","1","1.",".1","1..2","1.2.","1.2.3.","1.2.3.4.5","-1.2","+1.2","1.-2","1.+2","1.2.-3","1.2.3.-4",
            " 1.2","1.2 ","1. 2","1e1.2","2147483648.0","0.2147483648","0.0.2147483648","0.0.0.2147483648","1.2\0suffix","1.\u0080"];
        (int Major,int Minor,int Build,int Revision)[] bad=[(-1,0,-1,-1),(0,-1,-1,-1),(0,0,-2,-1),(0,0,-1,0),(0,0,0,-2),(int.MinValue,0,-1,-1),(0,int.MinValue,-1,-1)];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true)){
            foreach(string input in invalid){WriteText(writer,input);writer.Write((byte)(input=="1.2\0suffix"?1:0));}
            foreach(var value in bad){writer.Write(value.Major);writer.Write(value.Minor);writer.Write(value.Build);writer.Write(value.Revision);}
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            struct Writer:ITextWriter{
                public int Calls;public bool Fail;public bool Bad;public Writer(){Calls=0;Fail=false;Bad=false;}
                public void Write(readonly(char)* data,nuint count){Calls++;if(Fail)throw new Exception("writer",73);if(count!=7)Bad=true;else for(nuint i=0;i<count;i++)if(data[i]!="1.2.0.0"[i])Bad=true;}
            }
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;char[128] input;Version zero=new();
                for(int row=0;row<{{{invalid.Length}}};row++){
                    nuint length=ReadText(data,ref at,input);bool cstringValid=ReadByte(data,ref at)!=0;Version value=new(9,8,7,6);int before=attempts;deny=true;
                    if(Version::TryParse(input,length,&value)||!value.Equals(&zero)||attempts!=before)return 1;
                    value=new(9,8,7,6);if(Version::TryParse(input,&value)!=cstringValid||attempts!=before)return 2;
                    if(cstringValid){Version expected=new(1,2);if(!value.Equals(&expected))return 3;}else if(!value.Equals(&zero))return 4;deny=false;
                    value=new(9,8,7,6);bool caught=false;try{value=Version::Parse(input,length);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Invalid version"))return 5;caught=true;}
                    if(!caught||value.Major!=9||value.Minor!=8||value.Build!=7||value.Revision!=6)return 6;
                }
                for(int row=0;row<{{{bad.Length}}};row++){
                    int major=ReadInt(data,ref at);int minor=ReadInt(data,ref at);int build=ReadInt(data,ref at);int revision=ReadInt(data,ref at);
                    Version value=new(9,8,7,6);bool caught=false;try{value=new Version(major,minor,build,revision);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Invalid version components"))return 7;caught=true;}
                    if(!caught||value.Major!=9||value.Minor!=8||value.Build!=7||value.Revision!=6)return 8;
                }
                readonly(char)*? absent=null;int beforeShort=attempts;deny=true;
                for(nuint count=0;count<3;count++){Version value=new(9,8,7,6);if(Version::TryParse(absent!,count,&value)||!value.Equals(&zero))return 22;}
                deny=false;if(attempts!=beforeShort)return 23;
                // All overlap storage is aligned, fully backed, and disjoint from written.
                if((nuint)sizeof(Version)>32)return 9;
                Version specimen=new(1,2,0,0);
                for(nuint offset=0;offset<=32;offset+=8)for(nuint capacity=0;capacity<=8;capacity++){
                    Version[8] storage;char* buffer=(&storage[0]).reinterpret<char*>();for(nuint i=0;i<(nuint)sizeof(Version)*8;i++)buffer[i]='!';storage[1]=specimen;
                    char[256] saved;for(nuint i=0;i<(nuint)sizeof(Version)*8;i++)saved[i]=buffer[i];nuint written=99;int before=attempts;deny=true;
                    bool success=storage[1].TryFormat(buffer+offset,capacity,ref written);deny=false;
                    if(success!=(capacity>=7)||attempts!=before)return 10;
                    if(!success){if(written!=0||!storage[1].Equals(&specimen))return 11;for(nuint i=0;i<(nuint)sizeof(Version)*8;i++)if(buffer[i]!=saved[i])return 12;}
                    else{if(written!=7)return 13;for(nuint i=0;i<(nuint)sizeof(Version)*8;i++){
                        if(i>=offset&&i<offset+7){if(buffer[i]!="1.2.0.0"[i-offset])return 14;}else if(buffer[i]!=saved[i])return 15;
                    }}
                }
                for(int badInput=0;badInput<2;badInput++){
                    Version[8] storage;char* buffer=(&storage[0]).reinterpret<char*>();for(nuint i=0;i<7;i++)buffer[i]="1.2.0.0"[i];if(badInput==1)buffer[6]='x';
                    bool success=Version::TryParse(buffer,7,&storage[0]);if(success!=(badInput==0))return 16;
                    if(success){if(!storage[0].Equals(&specimen))return 17;}else if(!storage[0].Equals(&zero))return 18;
                }
                Writer writer=new();int before=attempts;deny=true;specimen.Format(&writer);deny=false;
                if(writer.Calls!=1||writer.Bad||attempts!=before)return 19;
                writer.Fail=true;bool caught=false;try{specimen.Format(&writer);}catch(ref Exception error){if(error.GetCode()!=73)return 20;caught=true;}
                if(!caught||writer.Calls!=2||specimen.Major!=1||specimen.Minor!=2||specimen.Build!=0||specimen.Revision!=0||at!={{{hex.Length}}}||allocated!=released)return 21;return 0;
            }
            """,optimization);
    }

    [Fact]
    public void SelectedCoreValueSourcesNeedNoStringAndTryObserversHaveNoExceptionBoundary()
    {
        string[] names=["Version.gf","ITextWriter.gf", "IFormattable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("version-contract.gf","""
                namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* storage)nothrow{}}
                bool Observe(immutable(std::Version)* value,readonly(char)* text,nuint count,char* output,nuint capacity,ref nuint written)nothrow{
                    std::Version parsed=new();return std::Version::TryParse(text,count,&parsed)&&value.TryFormat(output,capacity,ref written)&&value.Equals(&parsed);
                }
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});Assert.DoesNotContain("$String",ir);
        Assert.DoesNotMatch(@"(?m)^declare\s+[^\r\n]*@(?:malloc|free|memcpy|memmove|memset|strlen)\(",ir);
        string body=System.Text.RegularExpressions.Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Observe\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke ",body);Assert.DoesNotContain("__gflat_eh_",body);
    }

    private static List<System.Version> Values()
    {
        var values=new List<System.Version>{new(),new(1,2),new(1,2,0),new(1,2,0,0),new(1,2,0,1),new(0,0),new(0,0,0),new(0,0,0,0),
            new(int.MaxValue,int.MaxValue),new(int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue)};
        var random=new Random(541813);for(int i=0;i<40;i++){
            int major=random.Next(8),minor=random.Next(8),build=random.Next(8),revision=random.Next(8);
            values.Add(i%3==0?new(major,minor):i%3==1?new(major,minor,build):new(major,minor,build,revision));
        }
        return values;
    }
    private static void WriteText(BinaryWriter writer,string text){byte[] bytes=Encoding.Latin1.GetBytes(text);writer.Write((byte)bytes.Length);writer.Write(bytes);}
    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("version.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Version exit {result.ExitCode}: {result.StandardError}");
    }
}
