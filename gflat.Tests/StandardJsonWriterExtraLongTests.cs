using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class StandardJsonWriterExtraLongTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StreamedInt128CorpusHasExactInvariantRootObjectAndArrayText(int optimization)
    {
        var values=new List<Int128>{Int128.MinValue,Int128.MaxValue,0,1,-1,long.MinValue,long.MaxValue,ulong.MaxValue};
        for(int bit=0;bit<127;bit++){Int128 value=(Int128)1<<bit;values.Add(value);values.Add(-value);}
        var random=new Random(91327);byte[] bits=new byte[16];
        for(int i=0;i<120;i++){random.NextBytes(bits);values.Add(BinaryPrimitives.ReadInt128LittleEndian(bits));}
        string file=Path.Combine(Path.GetTempPath(),"gflat_json_int128_"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            using(var output=File.Create(file))foreach(Int128 value in values)
            {
                byte[] record=new byte[57];BinaryPrimitives.WriteInt128LittleEndian(record,value);
                byte[] text=Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
                record[16]=(byte)text.Length;text.CopyTo(record,17);output.Write(record);
            }
            string literal="\""+file.Replace("\\","\\\\")+"\"";
            Run($$"""
                using Standard; using Standard::Text; using Standard::Json;
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* file);
                extern int fclose(void* file);
                bool Digits(ref immutable(String) actual,nuint start,readonly(byte)* expected,nuint count){
                    for(nuint i=0;i<count;i++)if(actual.Data[start+i]!=(char)expected[i])return false;return true;
                }
                int main(){
                    void*? file=fopen({{literal}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    byte[57] record;nuint seen=0;
                    while(fread((void*)&record[0],1,57,file!)==57){
                        extralong value=0;for(nuint i=0;i<16;i++)value=value|((extralong)record[i]<<(i*8));
                        nuint length=(nuint)record[16];readonly(byte)* expected=&record[17];
                        {
                            String text=s"";JsonWriter writer=new(&text);writer.WriteNumber(value);writer.Complete();
                            if(text.Length!=length||!Digits(ref text,0,expected,length))return 2;
                        }
                        {
                            String text=s"";JsonWriter writer=new(&text);writer.WriteStartObject();writer.WritePropertyName("n");writer.WriteNumber(value);writer.WriteEndObject();writer.Complete();
                            if(text.Length!=length+6||!text.StartsWith("{\"n\":")||text.Data[text.Length-1]!='}'||!Digits(ref text,5,expected,length))return 3;
                        }
                        {
                            String text=s"";JsonWriter writer=new(&text,true);writer.WriteStartArray();writer.WriteNumber(value);writer.WriteEndArray();writer.Complete();
                            if(text.Length!=length+6||!text.StartsWith("[\n  ")||text.Data[text.Length-2]!='\n'||text.Data[text.Length-1]!=']'||!Digits(ref text,4,expected,length))return 4;
                        }
                        seen++;
                    }
                    if(seen!={{values.Count}})return 5;return 0;
                }
                """,optimization);
        }
        finally{File.Delete(file);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WideNumbersPreserveWriterStateAndOutputFailureContracts(int optimization)
        =>Run("""
            using Standard; using Standard::Text; using Standard::Json;extern void*? malloc(nuint bytes);extern void free(void* pointer);
            int allocations=0;int calls=0;int fail=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){allocations++;return malloc(bytes);}
                public replace void Free(void* pointer) nothrow{free(pointer);}
            }
            struct Sink:ITextWriter{
                public nuint count;
                public Sink(){count=0;}
                public void Write(readonly(char)* data,nuint length){calls++;if(calls==fail)throw new Exception("sink",81);count+=length;}
            }
            int main(){
                extralong minimum=(extralong)1<<127;extralong maximum=minimum-(extralong)1;
                {
                    Sink sink=new();JsonWriter writer=new(&sink);int before=allocations;
                    writer.WriteNumber(minimum);writer.Complete();
                    if(allocations!=before||sink.count!=40)return 1;
                    nuint bytes=sink.count;int caught=0;
                    try{writer.WriteNumber(maximum);}catch(ref JsonWriteException error){caught++;}
                    if(caught!=1||sink.count!=bytes)return 2;
                }
                {
                    String text=s"";JsonWriter writer=new(&text);writer.WriteStartObject();int caught=0;
                    try{writer.WriteNumber(minimum);}catch(ref JsonWriteException error){caught++;}
                    if(caught!=1||text.Length!=1)return 3;
                    writer.WritePropertyName("n");writer.WriteNumber(maximum);writer.WriteEndObject();writer.Complete();
                    if(!text.StartsWith("{\"n\":170141183460469231731687303715884105727}"))return 4;
                }
                for(int failure=1;failure<=2;failure++){
                    Sink sink=new();JsonWriter writer=new(&sink);writer.WriteStartArray();writer.WriteNumber((extralong)0);
                    calls=0;fail=failure;int caught=0;
                    try{writer.WriteNumber(minimum);}catch(ref Exception error){if(error.GetCode()!=81)return 5;caught++;}
                    if(caught!=1||calls!=failure)return 6;
                    fail=0;int before=calls;
                    try{writer.WriteNumber(maximum);}catch(ref JsonWriteException error){caught++;}
                    if(caught!=2||calls!=before)return 7;
                }
                return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("json-writer-wide.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
