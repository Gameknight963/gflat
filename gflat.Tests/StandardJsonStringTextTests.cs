using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonStringTextTests
{
    private const string Allocator="""
        using std;extern void*? malloc(nuint size);extern void free(void* pointer);
        int allocations=0;int releases=0;int large=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint size){if(size>1024)large++;if(deny)return null;void*? result=malloc(size);if(result!=null)allocations++;return result;}
            public replace void Free(void* pointer){releases++;free(pointer);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningResultsMatchDotNetJsonSemanticsAndAllocateOnce(int optimization)
    {
        List<string> values=["","\0","a\0b","\"\\/\b\t\n\f\r",new string(Enumerable.Range(0,128).Select(i=>(char)i).ToArray()),
            "\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\ud800\udc00\udbff\udfff","\ufeff\u2028\u2029<> &"];
        var random=new Random(99132);
        for(int i=0;i<32;i++){
            var value=new StringBuilder();for(int j=0;j<random.Next(0,24);j++){
                int scalar=random.Next(0x110000);if(scalar is >=0xd800 and <=0xdfff)scalar=0;
                value.Append(char.ConvertFromUtf32(scalar));
            }values.Add(value.ToString());
        }
        string file=Path.Combine(Path.GetTempPath(),"gflat_json_string_text_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var output=new BinaryWriter(File.Create(file)))foreach(string value in values){
                byte[] raw=Encoding.UTF8.GetBytes(value),quoted=JsonSerializer.SerializeToUtf8Bytes(value);
                output.Write((uint)raw.Length);output.Write(raw);output.Write((uint)quoted.Length);output.Write(quoted);
            }
            string literal="\""+file.Replace("\\","\\\\")+"\"";
            string target="\""+(file+".jsonl").Replace("\\","\\\\")+"\"";
            Run(Allocator+$$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* file);extern int fclose(void* file);
                extern nuint fwrite(readonly(void)* data,nuint size,nuint count,void* file);extern int fputc(int value,void* file);
                int main(){
                    void*? file=fopen({{literal}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    void*? target=fopen({{target}},"wb");if(target==null)return 10;defer{fclose(target!);}
                    char[2048] raw;char[2048] encoded;uint size=0;nuint seen=0;
                    while(fread((void*)&size,4,1,file!)==1){
                        nuint length=(nuint)size;if(length>2048||fread((void*)&raw[0],1,length,file!)!=length)return 2;
                        if(fread((void*)&size,4,1,file!)!=1)return 3;
                        nuint count=(nuint)size;if(count>2048||fread((void*)&encoded[0],1,count,file!)!=count)return 4;
                        int before=allocations;
                        {
                            Span<readonly(char)> input=new(raw,length);
                            String quoted=JsonStringText::Quote(input);
                            if(allocations!=before+1||quoted.Data[quoted.Length]!='\0'||quoted.Length!=quoted.Capacity)return 5;
                            if(fwrite((readonly(void)*)quoted.Data,1,quoted.Length,target!)!=quoted.Length||fputc(10,target!)==-1)return 11;
                            before=allocations;Span<readonly(char)> token=new(encoded,count);
                            String decoded=JsonStringText::Unquote(token);
                            int expected=0;if(length!=0)expected=1;
                            if(allocations!=before+expected||decoded.Length!=length||decoded.Data[decoded.Length]!='\0')return 6;
                            for(nuint i=0;i<length;i++)if(decoded.Data[i]!=raw[i])return 7;
                        }
                        if(allocations!=releases)return 8;seen++;
                    }
                    if(seen!={{values.Count}})return 9;return 0;
                }
                """,optimization);
            string[] lines=File.ReadAllLines(file+".jsonl",Encoding.UTF8);
            Assert.Equal(values,lines.Select(line=>JsonSerializer.Deserialize<string>(line)).ToList());
        }
        finally{File.Delete(file);File.Delete(file+".jsonl");}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidInputPreflightsBeforeResultAllocationAndFailuresPreserveOwners(int optimization)
        =>Run(Allocator+"""
            int main(){
                char[5000] raw;char[5002] literal;
                for(nuint i=0;i<5000;i++){raw[i]='a';literal[i+1]='a';}literal[0]='"';literal[5001]='"';
                raw[4999]=(char)255;literal[5000]='\\';int before=large;int caught=0;
                try{JsonStringText::Quote(raw,5000);}catch(ref Exception error){caught++;}
                try{JsonStringText::Unquote(literal,5002);}catch(ref Exception error){caught++;}
                if(caught!=2||large!=before||raw[4999]!=(char)255||literal[5000]!='\\')return 1;
                caught=0;
                try{JsonStringText::Unquote("\"\\uD800\"",8);}catch(ref Exception error){caught++;}
                try{JsonStringText::Unquote("\"x\"y",4);}catch(ref Exception error){caught++;}
                try{JsonStringText::Unquote(" \"x\"",4);}catch(ref Exception error){caught++;}
                Span<readonly(char)> absent=new();
                try{JsonStringText::Unquote(absent);}catch(ref Exception error){caught++;}
                if(caught!=4)return 10;
                {
                    String output=s"unchanged";readonly(char)* old=output.Data;int balance=allocations-releases;
                    deny=true;
                    try{output=JsonStringText::Quote("x",1);return 2;}catch(ref BadAllocException error){}
                    try{output=JsonStringText::Unquote("\"x\"",3);return 3;}catch(ref BadAllocException error){}
                    deny=false;
                    if(output.Data!=old||output.Length!=9||allocations-releases!=balance)return 4;
                }
                char[3] data=['a','\0','b'];
                {
                    String quoted=JsonStringText::Quote(data,3);String decoded=JsonStringText::Unquote(quoted.Data,quoted.Length);
                    if(decoded.Data==&data[0]||decoded.Data==quoted.Data||decoded.Length!=3)return 5;
                    data[0]='X';quoted.Clear();if(decoded[0]!='a'||decoded[1]!='\0'||decoded[2]!='b')return 6;
                    if(!JsonStringText::Quote((readonly(char)*)1,0).StartsWith("\"\""))return 7;
                    before=allocations;String empty=JsonStringText::Unquote("\"\"",2);
                    if(!empty.IsEmpty||allocations!=before)return 8;
                    String emptyQuoted=JsonStringText::Quote(absent);
                    if(emptyQuoted.Length!=2||!emptyQuoted.StartsWith("\"\""))return 11;
                }
                if(allocations!=releases)return 9;return 0;
            }
            """,optimization);

    private static string Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("json-string-text.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");return result.StandardOutput;
    }
}
