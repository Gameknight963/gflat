using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonTextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MinificationPreservesRawTokensAndRemovesOnlyTokenGapWhitespace(int optimization)
    {
        string[] documents = [" null \r\n", "\ttrue", " 123.4500E+999999 ", " {} ", "[ ]",
            " { \"x\" : \"a b\\t\\u0061\\/\", \"x\" : 1.00e-0001, \"empty\" : [ ] } ",
            " [ \"\\\\\\\"  x\", \"\\u0000\", \"\ud83d\ude00 \u20ac\", false, null, {}, [] ] ",
            new string('[',70) + " \"x\" " + new string(']',70)];
        var code = new StringBuilder("using std; using std::Json;extern int printf(readonly(char)* text,...);extern int putchar(int ch);int main(){\n");
        for (int id=0; id<documents.Length; id++)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(documents[id]);
            code.AppendLine($"{{char[{bytes.Length}] input;");
            for(int i=0;i<bytes.Length;i++)code.AppendLine($"input[{i}]=(char){bytes[i]};");
            code.AppendLine($"String text=new();JsonText::Minify(input,{bytes.Length},&text);for(nuint i=0;i<text.Length;i++)printf(\"%02X\",(uint)(byte)text.Data[i]);putchar(10);}}");
        }
        code.AppendLine("return 0;}");
        var result=CompilerTestHelper.Run(Sources(code.ToString()),optimization);
        Assert.True(result.ExitCode==0,result.StandardError);
        var lines=result.StandardOutput.Replace("\r\n","\n").Split('\n',StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(documents.Length,lines.Length);
        for(int i=0;i<lines.Length;i++)
        {
            byte[] actual=Convert.FromHexString(lines[i]);
            Assert.Equal(Tokens(Encoding.UTF8.GetBytes(documents[i]),false),Tokens(actual,true));
        }
        Assert.Equal("{\"x\":\"a b\\t\\u0061\\/\",\"x\":1.00e-0001,\"empty\":[]}",Encoding.UTF8.GetString(Convert.FromHexString(lines[5])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidationFailuresNeverCallOutputAndCountZeroDoesNotReadInput(int optimization)
    {
        byte[][] invalid=[[]," "u8.ToArray(),"[1,]"u8.ToArray(),"{} false"u8.ToArray(),"[\"\\uD800\"]"u8.ToArray(),[34,0xff,34],"["u8.ToArray()];
        var code=new StringBuilder("using std; using std::Json;struct Sink:ITextWriter{public int Calls;public Sink(){Calls=0;}public void Write(readonly(char)* input,nuint count){Calls++;throw new Exception(\"unexpected output\",99);}}int main(){Sink sink=new();char[32] input;\n");
        foreach(byte[] bytes in invalid)
        {
            for(int i=0;i<bytes.Length;i++)code.AppendLine($"input[{i}]=(char){bytes[i]};");
            code.AppendLine($"try{{JsonText::Minify(input,{bytes.Length},&sink);return 1;}}catch(ref JsonParseException error){{if(error.Offset>{bytes.Length})return 2;}}if(sink.Calls!=0)return 3;");
        }
        code.AppendLine("try{JsonText::Minify((readonly(char)*)1,0,&sink);return 4;}catch(ref JsonParseException error){if(error.Offset!=0)return 5;}return sink.Calls;}");
        Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidationStackIsReleasedBeforeOutputAndFailuresPreserveExceptions(int optimization)
        => Run("""
            using std; using std::Json;extern void*? malloc(nuint count);extern void free(void* pointer);
            int live=0;bool deny=false;int allowance=-1;
            namespace Memory{
                public replace void*? Allocate(nuint count){if(deny||allowance==0)return null;if(allowance>0)allowance--;void*? result=malloc(count);if(result!=null)live++;return result;}
                public replace void Free(void* pointer) nothrow{live--;free(pointer);}
            }
            struct Sink:ITextWriter{
                public int Calls;public nuint Bytes;public bool Fail;
                public Sink(bool fail){Calls=0;Bytes=0;Fail=fail;}
                public void Write(readonly(char)* input,nuint count){
                    if(live!=0)throw new Exception("validation stack still alive",88);
                    Calls++;if(count==0)throw new Exception("empty run",89);
                    if(Fail)throw new Exception("writer failure",73);Bytes+=count;
                }
            }
            int main(){
                Sink sink=new(false);JsonText::Minify(" { \"a\" : [ 1, 2 ] } ",20,&sink);
                if(sink.Bytes!=11||sink.Calls!=8||live!=0)return 1;
                Sink failing=new(true);try{JsonText::Minify(" [ 1 ] ",7,&failing);return 2;}
                catch(ref Exception error){if(error.GetCode()!=73)return 3;}
                if(live!=0||failing.Calls!=1)return 4;
                deny=true;try{JsonText::Minify("[]",2,&sink);return 5;}
                catch(ref BadAllocException error){}deny=false;
                if(live!=0||sink.Calls!=8)return 6;
                char[40] deep;for(int i=0;i<20;i++){deep[i]='[';deep[20+i]=']';}
                allowance=1;try{JsonText::Minify(deep,40,&sink);return 8;}
                catch(ref BadAllocException error){}allowance=-1;
                if(live!=0||sink.Calls!=8)return 9;
                deny=true;JsonText::Minify(" 123 ",5,&sink);deny=false;
                if(live!=0||sink.Bytes!=14||sink.Calls!=9)return 7;
                char[8194] large;large[0]='"';for(int i=1;i<8193;i++)large[i]='x';large[8193]='"';
                deny=true;JsonText::Minify(large,8194,&sink);deny=false;
                if(live!=0||sink.Bytes!=8208||sink.Calls!=10)return 10;
                return 0;
            }
            """,optimization);

    private static string[] Tokens(byte[] bytes,bool requireCompact)
    {
        var reader=new Utf8JsonReader(bytes,new JsonReaderOptions{MaxDepth=1024});var result=new List<string>();int end=0;
        while(reader.Read())
        {
            if(requireCompact)
                foreach(byte gap in bytes.AsSpan(end,(int)reader.TokenStartIndex-end))Assert.True(gap is (byte)',' or (byte)':');
            result.Add(reader.TokenType+":"+Convert.ToHexString(reader.ValueSpan));
            end=(int)reader.BytesConsumed;
        }
        if(requireCompact)Assert.Equal(bytes.Length,end);
        return result.ToArray();
    }
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
