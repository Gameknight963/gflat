using System.Text;

namespace gflat.Tests;

public sealed class StandardUuidTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CanonicalNetworkOrderParsingFormattingAndComparisonMatchGuid(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-uuid-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"oracle.bin");
            var cases=new List<byte[]>{new byte[16],Enumerable.Repeat((byte)255,16).ToArray(),
                Convert.FromHexString("00112233445566778899AABBCCDDEEFF"),
                Convert.FromHexString("6BA7B8109DAD11D180B400C04FD430C8"),
                Convert.FromHexString("919108F752D143209BACF847DB4148A8")};
            var random=new Random(9562);
            for(int i=0;i<128;i++){byte[] bytes=new byte[16];random.NextBytes(bytes);cases.Add(bytes);}
            using(var stream=File.Create(path))
            {
                string previous=Guid.Empty.ToString("D");
                foreach(byte[] bytes in cases)
                {
                    var guid=new Guid(bytes,bigEndian:true);
                    string spelling=guid.ToString("D");
                    Assert.Equal(bytes,Convert.FromHexString(spelling.Replace("-","")));
                    stream.Write(Encoding.ASCII.GetBytes(spelling));
                    stream.Write(bytes);
                    stream.WriteByte((byte)(Math.Sign(string.CompareOrdinal(spelling,previous))+1));
                    previous=spelling;
                }
            }
            Run($$"""
                using std; using std::Text; using std::Collections;
                extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* output,nuint size,nuint count,void* file);
                extern int fclose(void* file);
                extern void*? malloc(nuint count);extern void free(void* pointer);
                bool deny=false;int allocations=0;
                namespace Memory{
                    public replace void*? Allocate(nuint count){if(deny){allocations++;return null;}return malloc(count);}
                    public replace void Free(void* pointer) nothrow{free(pointer);}
                }
                int main(){
                    void* file=fopen({{Literal(path)}},"rb");
                    defer{fclose(file);}
                    char[53] record;char[37] uppercase;char[38] formatted;
                    Uuid previous=new();int count=0;deny=true;
                    while(fread((void*)&record[0],1,53,file)==53){
                        Uuid value=new Uuid(1ul,2ul);
                        if(!Uuid::TryParse(record,36,&value))return 1;
                        ulong high=0ul;ulong low=0ul;
                        for(nuint i=0;i<8;i++){high=(high<<8)|(ulong)(byte)record[36+i];low=(low<<8)|(ulong)(byte)record[44+i];}
                        if(value.High!=high||value.Low!=low||value.IsNil!=(high==0ul&&low==0ul))return 2;
                        if(value.CompareTo(&previous)!=(int)(byte)record[52]-1)return 3;
                        previous=value;
                        for(nuint i=0;i<36;i++){
                            char digit=record[i];if(digit>='a'&&digit<='f')digit=(char)(digit-'a'+'A');uppercase[i]=digit;
                        }
                        uppercase[36]='\0';Uuid other=new();
                        if(!Uuid::TryParse(uppercase,&other)||!other.Equals(&value)||other.GetHashCode()!=value.GetHashCode())return 4;
                        Uuid parsed=Uuid::Parse(uppercase);Uuid counted=Uuid::Parse(record,36);
                        if(!parsed.Equals(&value)||!counted.Equals(&value))return 5;
                        formatted[0]='!';formatted[37]='?';nuint written=91;
                        if(!value.TryFormat(&formatted[1],36,ref written)||written!=36||formatted[0]!='!'||formatted[37]!='?')return 6;
                        for(nuint i=0;i<36;i++)if(formatted[i+1]!=record[i])return 7;
                        count++;
                    }
                    deny=false;if(count!={{cases.Count}}||allocations!=0)return 8;return 0;
                }
                """,optimization);
        }
        finally{Directory.Delete(directory,true);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidFormsAndOverlapRespectNilFailureAndOutputPreflight(int optimization)
    {
        const string canonical="00112233-4455-6677-8899-aabbccddeeff";
        string[] invalid=[""," ","{"+canonical+"}",canonical.Replace("-",""),"urn:uuid:"+canonical,
            " "+canonical,canonical+" ","\t"+canonical,canonical+"\n",canonical+"+"];
        var code=new StringBuilder("using std; using std::Text; using std::Collections;int main(){Uuid value=new Uuid(1ul,2ul);\n");
        foreach(string text in invalid)
            code.AppendLine($$"""
                if(Uuid::TryParse({{Literal(text)}},{{text.Length}},&value)||!value.IsNil)return 1;
                value=new Uuid(1ul,2ul);if(Uuid::TryParse({{Literal(text)}},&value)||!value.IsNil)return 2;
                try{Uuid::Parse({{Literal(text)}},{{text.Length}});return 3;}catch(ref Exception error){}
                try{Uuid::Parse({{Literal(text)}});return 4;}catch(ref Exception error){}
                """);
        code.AppendLine("""
            readonly(char)* spelling="00112233-4455-6677-8899-aabbccddeeff";
            char[37] text;for(nuint i=0;i<36;i++)text[i]=spelling[i];text[36]='\0';
            for(nuint i=0;i<36;i++){
                char original=text[i];text[i]='x';value=new Uuid(1ul,2ul);
                if(Uuid::TryParse(text,36,&value)||!value.IsNil)return 5;text[i]=original;
            }
            text[35]=(char)255;if(Uuid::TryParse(text,36,&value)||!value.IsNil)return 6;
            text[35]='\0';if(Uuid::TryParse(text,36,&value)||!value.IsNil)return 7;
            for(nuint count=0;count<50;count++){
                if(count==36)continue;value=new Uuid(1ul,2ul);
                if(Uuid::TryParse((readonly(char)*)1,count,&value)||!value.IsNil)return 8;
            }
            char[37] unterminated;for(nuint i=0;i<37;i++)unterminated[i]='f';
            if(Uuid::TryParse(unterminated,&value)||!value.IsNil)return 9;
            Uuid[3] aligned;char* storage=(char*)&aligned[0];
            for(nuint i=0;i<36;i++)storage[i]=spelling[i];
            if(!Uuid::TryParse(storage,36,&aligned[0])||aligned[0].High!=0x0011223344556677ul||aligned[0].Low!=0x8899aabbccddeefful)return 10;
            nuint written=0;if(!aligned[0].TryFormat(storage,36,ref written)||written!=36)return 11;
            for(nuint i=0;i<36;i++)if(storage[i]!=spelling[i])return 12;
            storage[35]='x';if(Uuid::TryParse(storage,36,&aligned[0])||!aligned[0].IsNil)return 13;
            value=Uuid::Parse(spelling);char[36] output;for(nuint i=0;i<36;i++)output[i]='!';
            for(nuint capacity=0;capacity<36;capacity++){
                written=91;if(value.TryFormat(output,capacity,ref written)||written!=0)return 14;
                for(nuint i=0;i<36;i++)if(output[i]!='!')return 15;
            }
            written=91;if(value.TryFormat((char*)1,0,ref written)||written!=0)return 16;
            return 0;}
            """);Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UuidKeysInterpolationAndWriterExceptionsUseExistingContracts(int optimization)
        => Run("""
            using std; using std::Text; using std::Collections;
            struct Sink:ITextWriter{
                private char* data;public int Calls;public bool Fail;
                public Sink(char* output,bool fail){data=output;Calls=0;Fail=fail;}
                public void Write(readonly(char)* input,nuint count){Calls++;if(Fail)throw new Exception("output",73);
                    if(count!=36)throw new Exception("count",74);for(nuint i=0;i<count;i++)data[i]=input[i];}
            }
            int main(){
                Uuid key=Uuid::Parse("00112233-4455-6677-8899-aabbccddeeff");
                HashMap<Uuid,int> values=new();values.Add(key,42);Uuid same=Uuid::Parse("00112233-4455-6677-8899-AABBCCDDEEFF");
                if(values.Count!=1||values[&same]!=42||!values.ContainsKey(&same))return 1;
                String text=s$"uuid={key}";if(text.Length!=41||!text.StartsWith("uuid=00112233-4455-6677-8899-aabbccddeeff"))return 2;
                char[36] output;Sink sink=new(output,false);IFormattable* formatted=&key;formatted.Format(&sink);
                if(sink.Calls!=1)return 3;readonly(char)* spelling="00112233-4455-6677-8899-aabbccddeeff";
                for(nuint i=0;i<36;i++)if(output[i]!=spelling[i])return 4;
                Sink failing=new(output,true);try{formatted.Format(&failing);return 5;}catch(ref Exception error){if(error.GetCode()!=73)return 6;}
                return 0;
            }
            """,optimization);

    [Fact]
    public void CoreValueAndNothrowOperationsNeedNoStringSource()
    {
        string[] names=["core/Uuid.gf","core/Text.gf","core/IHashable.gf","core/IEquatable.gf","core/IComparable.gf","core/Hashing/primitive_hashing.gf",
            "core/Exception.gf","libc/Memory/Memory.gf","libc/Exceptions/ExceptionHandling.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",name))))
            .Append(new SourceFile("nothrow.gf","""
                using std; using std::Text; using std::Collections;
                bool Convert(readonly(char)* input,nuint count,char* output,nuint capacity,Uuid* result,ref nuint written)nothrow{
                    if(!Uuid::TryParse(input,count,result))return false;
                    return result.TryFormat(output,capacity,ref written);
                }
                """));
        Compiler.Emit(sources,options:new(){NoStdLib=true});
    }

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\t","\\t").Replace("\n","\\n")+"\"";
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("uuid.gf",code));
    private static void Run(string code,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
