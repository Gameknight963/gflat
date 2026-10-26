using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardCsvReaderTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("main.gf",program));

    private static void Run(string program,int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(Sources(program),optimization).ExitCode);

    private const string Allocator="""
        using std; using std::Text; using std::Csv; using std::Memory;
        int attempts=0;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        namespace Memory{
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GeneratedByteFieldsRoundTripWithOffsetsGuardsAndNoReaderAllocations(int optimization)
    {
        string path=Path.Combine(Path.GetTempPath(),$"gflat-csv-{Guid.NewGuid():N}.bin");
        try
        {
            int documents=0;
            var random=new Random(8257);
            using(var output=new BinaryWriter(File.Create(path)))
            {
                Write([],false);
                Write([[[]]],true);
                Write([[Bytes("aaa"),Bytes("bbb"),Bytes("ccc")],[Bytes("zzz"),Bytes("yyy"),Bytes("xxx")]],false);
                Write([[Bytes("aaa"),Bytes("b\r\nbb"),Bytes("ccc")],[Bytes("aaa"),Bytes("b\"bb"),Bytes("ccc")]],true);
                Write([[[],[]],[[]],[Bytes("last"),[]]],true);
                Write([[Enumerable.Range(0,256).Select(i=>(byte)i).ToArray()]],false);
                Write([[Enumerable.Repeat((byte)'"',4096).ToArray(),Bytes("end")]],false);
                for(int length=0;length<=32;length++)Write([[Enumerable.Repeat((byte)'x',length).ToArray()]],true);
                for(int document=0;document<200;document++)
                {
                    byte[][][] rows=Enumerable.Range(0,random.Next(1,9)).Select(_=>
                        Enumerable.Range(0,random.Next(1,9)).Select(_=>
                        {
                            byte[] bytes=new byte[random.Next(129)];random.NextBytes(bytes);return bytes;
                        }).ToArray()).ToArray();
                    Write(rows,random.Next(2)==0);
                }
                void Write(byte[][][] rows,bool finalEnding)
                {
                    using var document=new MemoryStream();
                    var fields=new List<(int Offset,int RawLength,byte[] Value,bool Quoted,bool End)>();
                    for(int row=0;row<rows.Length;row++)
                    {
                        for(int column=0;column<rows[row].Length;column++)
                        {
                            byte[] value=rows[row][column];
                            bool quoted=value.Any(b=>b is 34 or 44 or 13 or 10)||random.Next(2)==0;
                            // Zero bytes mean no records, so encode a sole empty field explicitly.
                            if(rows.Length==1&&rows[0].Length==1&&value.Length==0&&!finalEnding)quoted=true;
                            int start=(int)document.Length;
                            if(quoted)document.WriteByte(34);
                            foreach(byte b in value){document.WriteByte(b);if(quoted&&b==34)document.WriteByte(34);}
                            if(quoted)document.WriteByte(34);
                            bool last=column==rows[row].Length-1;
                            fields.Add((start,(int)document.Length-start,value,quoted,last));
                            if(!last)document.WriteByte(44);
                        }
                        if(row!=rows.Length-1||finalEnding)
                        {
                            if(random.Next(2)==0)document.WriteByte(13);
                            document.WriteByte(10);
                        }
                    }
                    Assert.InRange(document.Length,0,32768);
                    output.Write((int)document.Length);output.Write(fields.Count);output.Write(document.ToArray());
                    foreach(var field in fields)
                    {
                        output.Write(field.Offset);output.Write(field.RawLength);output.Write(field.Value.Length);
                        output.Write(field.Quoted?1:0);output.Write(field.End?1:0);output.Write(field.Value);
                    }
                    documents++;
                }
            }
            Run(Allocator+"""
                extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);
                int main(){
                    void* stream=fopen("PATH","rb");
                    char[32768] input;char[32768] original;char[8192] expected;char[8194] output;
                    int[2] header;int[5] metadata;
                    for(int document=0;document<DOCUMENTS;document++){
                        if(fread((void*)(int*)header,8,1,stream)!=1)return 1;
                        if(fread((void*)(char*)input,1,(nuint)header[0],stream)!=(nuint)header[0])return 2;
                        for(nuint i=0;i<(nuint)header[0];i++)original[i]=input[i];
                        CsvReader reader=new(input,(nuint)header[0]);nuint written=9;
                        if(reader.TryCopyValue((char*)1,0,ref written)||written!=0||reader.RawField.Data!=null)return 3;
                        for(int field=0;field<header[1];field++){
                            if(fread((void*)(int*)metadata,20,1,stream)!=1)return 4;
                            nuint count=(nuint)metadata[2];
                            if(fread((void*)(char*)expected,1,count,stream)!=count)return 5;
                            if(!reader.Read()||reader.FieldOffset!=(nuint)metadata[0]||reader.ValueLength!=count)return 6;
                            if(reader.IsQuoted!=(metadata[3]!=0)||reader.EndOfRecord!=(metadata[4]!=0))return 7;
                            Span<readonly(char)> raw=reader.RawField;
                            if(raw.Length!=(nuint)metadata[1])return 8;
                            if(raw.Length!=0&&raw.Data!=(readonly(char)*)input+metadata[0])return 9;
                            if(raw.Length==0&&raw.Data!=null)return 10;
                            nuint capacity=0;
                            while(capacity<count){
                                for(nuint i=0;i<count+2;i++)output[i]='?';
                                written=17;
                                if(reader.TryCopyValue((char*)output+1,capacity,ref written)||written!=0)return 11;
                                for(nuint i=0;i<count+2;i++)if(output[i]!='?')return 12;
                                if(count<=32)capacity++;
                                else if(capacity==0)capacity=count-1;
                                else break;
                            }
                            for(nuint i=0;i<count+2;i++)output[i]='?';
                            if(!reader.TryCopyValue((char*)output+1,count,ref written)||written!=count)return 13;
                            for(nuint i=0;i<count;i++)if(output[i+1]!=expected[i])return 14;
                            if(output[0]!='?'||output[count+1]!='?')return 15;
                            if(!reader.TryCopyValue((char*)output+1,count+1,ref written)||written!=count)return 16;
                            if(count==0&&!reader.TryCopyValue((char*)1,0,ref written))return 17;
                        }
                        if(reader.Read()||reader.Read()||reader.RawField.Data!=null||reader.EndOfRecord)return 18;
                        if(reader.ValueLength!=0||reader.IsQuoted||reader.FieldOffset!=0)return 19;
                        if(reader.TryCopyValue((char*)1,0,ref written)||written!=0)return 20;
                        for(nuint i=0;i<(nuint)header[0];i++)if(input[i]!=original[i])return 21;
                    }
                    if(attempts!=0)return 22;return fclose(stream);
                }
                """.Replace("PATH",path.Replace("\\","/")).Replace("DOCUMENTS",documents.ToString()),optimization);
        }
        finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyRecordsTrailingFieldsReaderCopiesAndResetHaveExplicitSemantics(int optimization)
        => Run(Allocator+"""
            bool Shape(readonly(char)* input,nuint length,int fields,int records){
                CsvReader reader=new(input,length);int actualFields=0;int actualRecords=0;
                while(reader.Read()){actualFields++;if(reader.EndOfRecord)actualRecords++;}
                reader.Reset();int repeated=0;while(reader.Read())repeated++;
                return actualFields==fields&&actualRecords==records&&repeated==fields;
            }
            bool Inspect(ref immutable(CsvReader) reader,char* output,ref nuint written){
                return reader.TryCopyValue(output,16,ref written)&&reader.RawField.Length!=0;
            }
            int main(){
                if(!Shape((readonly(char)*)1,0,0,0)||!Shape("\n",1,1,1)||!Shape("\r\n",2,1,1))return 1;
                if(!Shape(",",1,2,1)||!Shape("a,",2,2,1)||!Shape("a\n",2,1,1))return 2;
                if(!Shape("a\n\n",3,2,2)||!Shape(",\n",2,2,1)||!Shape("\"\"",2,1,1))return 3;
                CsvReader reader=new(" first,\"x\"\"y\",last\r\n",20);
                if(!reader.Read())return 4;Span<readonly(char)> saved=reader.RawField;
                CsvReader independent=reader;
                if(!reader.Read()||reader.FieldOffset!=7||!reader.IsQuoted)return 5;
                char[16] output;nuint written=0;
                if(!Inspect(ref reader,output,ref written)||written!=3||output[0]!='x'||output[1]!='"'||output[2]!='y')return 6;
                reader.Reset();if(!reader.Read()||saved[0]!=' '||saved.Length!=6)return 7;
                if(!independent.Read()||independent.FieldOffset!=7)return 8;
                if(!independent.Read()||!independent.EndOfRecord)return 9;
                if(independent.Read()||saved[1]!='f')return 10;
                if(attempts!=0)return 11;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedSuffixesReportExactOffsetsClearCurrentAndLatchUntilReset(int optimization)
    {
        (string Text,int Offset,int Fields)[] cases=
        [
            ("a\"b",1,0),("\"abc",4,0),("\"a\"x",3,0),("\"a\" ",3,0),
            ("\"a\"\t",3,0),("\"a\"\0",3,0),("a\rb",2,0),("a\r",2,0),
            ("\"a\"\rb",4,0),("\"a\"\r",4,0),("\"a\"\"",4,0),
            (",\"bad",5,1),("\"a\",x\"bad",5,1),("a,b\n\"ok\",c\n\"bad\"x",16,4)
        ];
        var program=new StringBuilder("using std; using std::Text; using std::Csv; using std::Memory;int main(){\n");
        foreach(var item in cases)
        {
            byte[] bytes=Bytes(item.Text);
            program.AppendLine($"{{char[{bytes.Length}] input=[{string.Join(",",bytes.Select(b=>$"(char){b}"))}];");
            program.AppendLine($"CsvReader reader=new(input,{bytes.Length});for(int retry=0;retry<2;retry++){{int fields=0;");
            program.AppendLine($"try{{while(reader.Read())fields++;return 1;}}catch(ref CsvParseException error){{if(error.Offset!={item.Offset})return 2;}}");
            program.AppendLine($"if(fields!={item.Fields}||reader.RawField.Data!=null||reader.ValueLength!=0||reader.IsQuoted||reader.EndOfRecord)return 3;");
            program.AppendLine("char[2] output=['?','?'];nuint written=17;if(reader.TryCopyValue(output,2,ref written)||written!=0||output[0]!='?'||output[1]!='?')return 4;");
            program.AppendLine("try{reader.Read();return 5;}catch(ref CsvParseException error){}reader.Reset();}} ");
        }
        program.AppendLine("return 0;}");
        Run(program.ToString(),optimization);
    }

    [Theory]
    [InlineData("readonly","Read")]
    [InlineData("immutable","Read")]
    [InlineData("readonly","Reset")]
    [InlineData("immutable","Reset")]
    public void ProtectedReaderCannotAdvanceOrReset(string protection,string method)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(
            $"void F(ref {protection}(std::Csv::CsvReader) reader){{reader.{method}();}}")));
        Assert.Contains(method,error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InventoryImportDecodesOnlyQuantityAndStreamsExactSum(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Text; using std::Csv; using std::Memory;
            int main(){
                readonly(char)* document="\"bolts, large\",12\r\n\"m4 \"\"nut\"\"\",8\r\n";
                CsvReader reader=new(document,CString::Length(document));
                char[32] number;nuint column=0;extralong total=0;
                while(reader.Read()){
                    if(column>=2)throw new Exception("Expected two CSV columns");
                    if(column==1){
                        nuint written=0;
                        if(!reader.TryCopyValue(number,32,ref written))throw new Exception("Quantity exceeds caller buffer");
                        long quantity=0;
                        if(!TryParseLong(number,written,&quantity))throw new Exception("Invalid quantity");
                        total+=(extralong)quantity;
                    }
                    column++;
                    if(reader.EndOfRecord){
                        if(column!=2)throw new Exception("Expected two CSV columns");
                        column=0;
                    }
                }
                total.Format(Console::Out);Console::WriteLine();Console::Flush();return 0;
            }
            """),optimization);
        Assert.Equal(0,result.ExitCode);
        Assert.Equal("20"+Environment.NewLine,result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    private static byte[] Bytes(string text)=>Encoding.UTF8.GetBytes(text);
}
