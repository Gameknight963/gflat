using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonPrettyPrintTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Json; using std::IO;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LayoutMatchesDotNetWhereEscapingAgreesAndRawLexemesSurviveFormatting(int optimization)
    {
        var rows=new List<(byte[] Input,byte[] Expected,ulong Width,bool Default)>();
        void Fixed(string input,string expected,ulong width=2,bool useDefault=false)=>rows.Add((Encoding.UTF8.GetBytes(input),Encoding.UTF8.GetBytes(expected),width,useDefault));
        foreach(string input in new[]{"null"," true "," 17 ","\"abc\"","{}","[ ]","[1,2,3]",
            "{\"name\":\"abc\",\"items\":[1,{},[],{\"ok\":true}],\"empty\":{}}",
            new string('[',70)+"0"+new string(']',70)}){
            using var document=JsonDocument.Parse(input,new JsonDocumentOptions{MaxDepth=1024});
            using var output=new MemoryStream();using(var writer=new Utf8JsonWriter(output,new JsonWriterOptions{Indented=true,MaxDepth=1024,NewLine="\n"}))document.WriteTo(writer);
            rows.Add((Encoding.UTF8.GetBytes(input),output.ToArray(),2,true));
        }
        const string layout="{\n|1|\"a\": [\n|2|1,\n|2|{},\n|2|[]\n|1|],\n|1|\"b\": {\n|2|\"c\": 2\n|1|}\n}";
        foreach(int width in new[]{0,1,3,64,65})
            Fixed(" { \"a\" : [ 1, { }, [ ] ], \"b\" : { \"c\" : 2 } } ",layout.Replace("|1|",new string(' ',width)).Replace("|2|",new string(' ',width*2)),(ulong)width);
        Fixed("""
             { "x" : "\u0061\/\uD83D\ude00\u00eA\\\" \t\u0000", "x": -0.1200E+0009, "items": [ {}, [ ], "punct: [,]:", "😀€" ] }
            ""","""
            {
              "x": "\u0061\/\uD83D\ude00\u00eA\\\" \t\u0000",
              "x": -0.1200E+0009,
              "items": [
                {},
                [],
                "punct: [,]:",
                "😀€"
              ]
            }
            """);
        string huge="-"+new string('9',2400)+".0000e-0000000000001";
        Fixed(" [ "+huge+", 0, 1E+99999999999999 ] ","[\n  "+huge+",\n  0,\n  1E+99999999999999\n]");
        foreach(string scalar in new[]{"-0","1.23000e+99999999","\"\\u0000\\/\""})Fixed(" \r\n"+scalar+"\t ",scalar);
        Fixed(" { } ","{}",(ulong)nuint.MaxValue);Fixed(" [ ] ","[]",(ulong)nuint.MaxValue);
        string path=Path.Combine(Path.GetTempPath(),"gflat_json_pretty_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var writer=new BinaryWriter(File.Create(path)))foreach(var row in rows){writer.Write(row.Input.Length);writer.Write(row.Expected.Length);writer.Write(row.Width);writer.Write(row.Default?1:0);writer.Write(row.Input);writer.Write(row.Expected);}
            Run(Allocator+$$"""
                struct Sink:ITextWriter{
                    private char* data;public nuint Length;public Sink(char* storage){data=storage;Length=0;}
                    public void Write(readonly(char)* text,nuint count){
                        if(allocated!=released)throw new Exception("validation still owns memory",88);
                        bool spaces=count!=0;for(nuint i=0;i<count;i++)if(text[i]!=' ')spaces=false;
                        if(spaces&&count>64)throw new Exception("unbounded space block",89);
                        for(nuint i=0;i<count;i++)data[Length+i]=text[i];Length+=count;
                    }
                }
                int main(){
                    File file=File::OpenRead({{Literal(path)}});char[20000] input;char[20000] original;char[40000] expected;char[40002] output;
                    for(int row=0;row<{{rows.Count}};row++){
                        int inputSize=FileBinary::ReadInt32LittleEndian(&file);int outputSize=FileBinary::ReadInt32LittleEndian(&file);ulong width=FileBinary::ReadUInt64LittleEndian(&file);int useDefault=FileBinary::ReadInt32LittleEndian(&file);
                        if(inputSize<0||inputSize>20000||outputSize<0||outputSize>40000)return 1;
                        for(nuint i=0;i<20000;i++)input[i]='Z';for(nuint i=0;i<40002;i++)output[i]='Z';
                        file.ReadExactly(&input[0],(nuint)inputSize);file.ReadExactly(&expected[0],(nuint)outputSize);for(nuint i=0;i<20000;i++)original[i]=input[i];
                        {Sink sink=new(&output[1]);if(useDefault!=0)JsonText::PrettyPrint(&input[0],(nuint)inputSize,&sink);else JsonText::PrettyPrint(&input[0],(nuint)inputSize,&sink,(nuint)width);
                         if(sink.Length!=(nuint)outputSize||output[0]!='Z'||output[1+(nuint)outputSize]!='Z'||output[40001]!='Z'){
                             String failure=s$"row={row}, actual={sink.Length}, expected={outputSize}";Console::WriteLine(failure.Data,failure.Length);return 2;
                         }
                         for(nuint i=0;i<sink.Length;i++)if(output[1+i]!=expected[i])return 3;}
                        for(nuint i=0;i<20000;i++)if(input[i]!=original[i])return 4;
                        {String before=new();String after=new();JsonText::Minify(&input[0],(nuint)inputSize,&before);JsonText::Minify(&output[1],(nuint)outputSize,&after);if(!before.Equals(&after))return 5;}
                        if(allocated!=released)return 6;
                    }
                    file.Close();return 0;
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidationUnicodeTruncationOverflowAndAllocationFailuresWriteNothing(int optimization)
    {
        List<byte[]> invalid=[[]," "u8.ToArray(),"[1,]"u8.ToArray(),"{} false"u8.ToArray(),"01"u8.ToArray(),"+1"u8.ToArray(),"NaN"u8.ToArray(),
            "\"\\uD800\""u8.ToArray(),"\"\\uDC00\""u8.ToArray(),[34,255,34],[34,192,175,34],[34,237,160,128,34],[34,244,144,128,128,34]];
        byte[] valid="{\"a\":[\"x\",1,true,null],\"b\":{}}"u8.ToArray();for(int count=0;count<valid.Length;count++)invalid.Add(valid[..count]);
        for(byte control=0;control<32;control++)invalid.Add([34,control,34]);
        var blob=new List<byte>();var rows=new List<int>();foreach(byte[] bytes in invalid){rows.AddRange([blob.Count,bytes.Length]);blob.AddRange(bytes);}blob.Add(0);
        Run(Allocator+$$"""
            struct Sink:ITextWriter{public int Calls;public nuint Bytes;public Sink(){Calls=0;Bytes=0;}public void Write(readonly(char)* data,nuint count){if(allocated!=released)throw new Exception("validation still alive",88);Calls++;Bytes+=count;} }
            int main(){
                byte[{{blob.Count}}] blob=[{{string.Join(',',blob.Select(value=>"(byte)"+value))}}];int[{{rows.Count}}] rows=[{{string.Join(',',rows)}}];Sink sink=new();
                for(nuint row=0;row<{{rows.Count}};row+=2){
                    bool caught=false;try{JsonText::PrettyPrint((readonly(char)*)&blob[(nuint)rows[row]],(nuint)rows[row+1],&sink);}catch(ref JsonParseException error){if(error.Offset>(nuint)rows[row+1])return 1;caught=true;}
                    if(!caught||sink.Calls!=0||allocated!=released)return 2;
                }
                bool caught=false;try{JsonText::PrettyPrint((readonly(char)*)1,0,&sink);}catch(ref JsonParseException error){if(error.Offset!=0)return 3;caught=true;}if(!caught||sink.Calls!=0)return 4;
                nuint maximum=(nuint)0-(nuint)1;nuint[2] widths=[maximum,maximum/2+1];
                for(nuint i=0;i<2;i++){
                    caught=false;try{JsonText::PrettyPrint("[[0]]",5,&sink,widths[i]);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"JSON indentation length overflow"))return 5;caught=true;}
                    if(!caught||sink.Calls!=0||allocated!=released)return 6;
                }
                char[601] deep;for(nuint i=0;i<300;i++){deep[i]='[';deep[301+i]=']';}deep[300]='0';
                int initial=allocated;JsonText::PrettyPrint(&deep[0],601,&sink,0);int points=allocated-initial;if(points==0||allocated!=released)return 7;
                for(int point=0;point<points;point++){
                    sink.Calls=0;sink.Bytes=0;int deniedBefore=denied;failAfter=point;caught=false;
                    try{JsonText::PrettyPrint(&deep[0],601,&sink);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                    if(!caught||denied==deniedBefore||sink.Calls!=0||allocated!=released)return 8;
                }
                int before=attempts;deny=true;sink.Calls=0;sink.Bytes=0;JsonText::PrettyPrint(" 123 ",5,&sink,maximum);deny=false;
                if(sink.Bytes!=3||attempts!=before||allocated!=released)return 9;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryWriterFailurePreservesItsSuccessfulPrefixAndAStatelessRetryAppendsAgain(int optimization)
        =>Run(Allocator+"""
            int destroyed=0;
            struct Sink:ITextWriter{
                private char* data;private nuint* starts;public nuint Length;public int Calls;public int FailAt;
                public Sink(char* output,nuint* offsets){data=output;starts=offsets;Length=0;Calls=0;FailAt=0;}
                public void Write(readonly(char)* input,nuint count){
                    if(allocated!=released)throw new Exception("validation still alive",88);
                    starts[(nuint)Calls]=Length;Calls++;if(Calls==FailAt)throw new Exception("sink failure",73);
                    for(nuint i=0;i<count;i++)data[Length+i]=input[i];Length+=count;
                }
                ~Sink(){destroyed++;}
            }
            int main(){
                readonly(char)* input="{\"x\":[1,2],\"y\":{}}";nuint count=18;
                char[1000] expected;char[1000] actual;nuint[128] offsets;nuint[128] scratch;int calls=0;nuint length=0;
                {Sink proof=new(&expected[0],&offsets[0]);JsonText::PrettyPrint(input,count,&proof);calls=proof.Calls;length=proof.Length;if(calls==0||calls>=64||length==0||expected[length-1]=='\n'||destroyed!=0)return 1;}
                for(int failure=1;failure<=calls;failure++){
                    int before=destroyed;
                    {Sink sink=new(&actual[0],&scratch[0]);sink.FailAt=failure;bool caught=false;
                     try{JsonText::PrettyPrint(input,count,&sink);}catch(ref Exception error){if(error.GetCode()!=73||!CString::Equals(error.GetMessage(),"sink failure"))return 2;caught=true;}
                     nuint prefix=offsets[(nuint)failure-1];if(!caught||sink.Calls!=failure||sink.Length!=prefix||destroyed!=before)return 3;
                     for(nuint i=0;i<prefix;i++)if(actual[i]!=expected[i])return 4;
                     sink.FailAt=0;JsonText::PrettyPrint(input,count,&sink);
                     if(sink.Length!=prefix+length||sink.Calls!=failure+calls||destroyed!=before)return 5;
                     for(nuint i=0;i<length;i++)if(actual[prefix+i]!=expected[i])return 6;
                     sink.Write("!",1);if(actual[sink.Length-1]!='!'||destroyed!=before)return 7;}
                    if(destroyed!=before+1||allocated!=released)return 8;
                }
                return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("json-pretty.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"JSON PrettyPrint exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
