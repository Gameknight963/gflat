using System.Text;

namespace gflat.Tests;

public sealed class StandardCsvWriterTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("csv-writer.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0,
            $"CSV writer exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private const string Allocator = """
        using std;
        int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GeneratedOpaqueFieldsMatchIndependentCanonicalByteOracleWithoutAllocating(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gflat-csv-writer-{Guid.NewGuid():N}.bin");
        try
        {
            int documents = 0;
            var random = new Random(45039);
            using (var fixture = new BinaryWriter(File.Create(path)))
            {
                Write([]);
                Write([[], [[]], [[], []], [Encoding.ASCII.GetBytes("plain"), []]]);
                Write([[Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()]]);
                Write([[Enumerable.Repeat((byte)'"', 4096).ToArray()]]);
                Write([[Encoding.ASCII.GetBytes(" leading "), Encoding.ASCII.GetBytes("a,b"),
                    Encoding.ASCII.GetBytes("a\"b"), Encoding.ASCII.GetBytes("a\rb\nc\r\nd")]]);
                for (int i = 0; i < 160; i++)
                    Write(Enumerable.Range(0, random.Next(1, 7)).Select(_ =>
                        Enumerable.Range(0, random.Next(0, 7)).Select(_ =>
                        {
                            byte[] bytes = new byte[random.Next(0, 129)];
                            random.NextBytes(bytes);
                            return bytes;
                        }).ToArray()).ToArray());

                void Write(byte[][][] rows)
                {
                    using var expected = new MemoryStream();
                    fixture.Write(rows.Length);
                    foreach (byte[][] row in rows)
                    {
                        fixture.Write(row.Length);
                        for (int column = 0; column < row.Length; column++)
                        {
                            byte[] field = row[column];
                            fixture.Write(field.Length);
                            fixture.Write(field);
                            if (column != 0) expected.WriteByte(44);
                            bool quoted = field.Any(value => value is 44 or 34 or 13 or 10);
                            if (quoted) expected.WriteByte(34);
                            foreach (byte value in field)
                            {
                                expected.WriteByte(value);
                                if (quoted && value == 34) expected.WriteByte(34);
                            }
                            if (quoted) expected.WriteByte(34);
                        }
                        expected.WriteByte(13);
                        expected.WriteByte(10);
                    }
                    Assert.InRange(expected.Length, 0, 65536);
                    fixture.Write((int)expected.Length);
                    fixture.Write(expected.ToArray());
                    documents++;
                }
            }

            Run(Allocator + """
                extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);
                struct Sink:ITextWriter {
                    private char* data;public nuint Count;public nuint Calls;
                    public Sink(char* target){data=target;Count=0;Calls=0;}
                    public void Write(readonly(char)* text,nuint count){
                        Calls++;
                        if(count==0||Count+count>65536)throw new Exception("Invalid output span");
                        for(nuint i=0;i<count;i++)data[Count+i]=text[i];
                        Count+=count;
                    }
                }
                int ReadInt(void* stream){
                    int value=0;if(fread((void*)&value,4,1,stream)!=1)return -1;return value;
                }
                int main(){
                    void* stream=fopen("PATH","rb");
                    char[8192] field;char[65536] output;char[65536] expected;
                    for(int document=0;document<DOCUMENTS;document++){
                        Sink sink=new(output);CsvWriter writer=new(&sink);
                        int rows=ReadInt(stream);if(rows<0)return 1;
                        for(int row=0;row<rows;row++){
                            int columns=ReadInt(stream);if(columns<0)return 2;
                            for(int column=0;column<columns;column++){
                                int size=ReadInt(stream);if(size<0||size>8192)return 3;
                                if(fread((void*)(char*)field,1,(nuint)size,stream)!=(nuint)size)return 4;
                                if(size==0)writer.WriteField((readonly(char)*)1,0);
                                else writer.WriteField(field,(nuint)size);
                            }
                            writer.WriteEndRecord();
                        }
                        int size=ReadInt(stream);if(size<0||sink.Count!=(nuint)size)return 5;
                        if(fread((void*)(char*)expected,1,(nuint)size,stream)!=(nuint)size)return 6;
                        for(nuint i=0;i<sink.Count;i++)if(output[i]!=expected[i])return 7;
                    }
                    if(attempts!=0)return 8;return fclose(stream);
                }
                """.Replace("PATH", path.Replace("\\", "/"))
                    .Replace("DOCUMENTS", documents.ToString()), optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryPartialSinkFailurePropagatesAndPoisonsFutureOperations(int optimization)
        => Run("""
            using std;
            class SinkFailure:Exception{public SinkFailure():base("sink failed",77){}}
            struct Sink:ITextWriter {
                public int Budget;public int Calls;public nuint Count;
                public Sink(int budget){Budget=budget;Calls=0;Count=0;}
                public void Write(readonly(char)* data,nuint count){
                    Calls++;
                    for(nuint i=0;i<count;i++){
                        if(Budget==0)throw new SinkFailure();
                        Budget--;Count++;
                    }
                }
            }
            bool Scenario(int budget){
                Sink sink=new(budget);CsvWriter writer=new(&sink);int caught=0;
                try{
                    writer.WriteField("plain",5);
                    writer.WriteField("a\"b,\r\n",6);
                    writer.WriteField((readonly(char)*)1,0);
                    writer.WriteEndRecord();
                    writer.WriteEndRecord();
                }catch(SinkFailure* error){if(error.GetCode()!=77)return false;caught++;}
                catch{return false;}
                if(caught!=1||sink.Count!=(nuint)budget)return false;
                int calls=sink.Calls;sink.Budget=999;
                try{writer.WriteField((readonly(char)*)1,0);}catch(Exception* error){caught++;}
                try{writer.WriteField("later",5);}catch(Exception* error){caught++;}
                try{writer.WriteEndRecord();}catch(Exception* error){caught++;}
                CsvWriter copy=writer;
                try{copy.WriteEndRecord();}catch(Exception* error){caught++;}
                return caught==5&&sink.Calls==calls&&sink.Count==(nuint)budget;
            }
            int main(){for(int budget=0;budget<20;budget++)if(!Scenario(budget))return budget+1;return 0;}
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyMoveScopeAndEmptyRecordsKeepDestinationBorrowedAndRoundTrip(int optimization)
        => Run(Allocator + """
            struct Sink:ITextWriter {
                private char* data;public nuint Count;public nuint Calls;
                public Sink(char* target){data=target;Count=0;Calls=0;}
                public void Write(readonly(char)* text,nuint count){
                    Calls++;for(nuint i=0;i<count;i++)data[Count+i]=text[i];Count+=count;
                }
            }
            int main(){
                char[128] output;Sink sink=new(output);
                {
                    CsvWriter writer=new(&sink);writer.WriteField("x",1);
                    CsvWriter copy=writer;copy.WriteField("y",1);
                    CsvWriter moved=move copy;moved.WriteEndRecord();
                }
                nuint calls=sink.Calls;
                {CsvWriter untouched=new(&sink);}
                if(sink.Calls!=calls||sink.Count!=5)return 1;
                {
                    CsvWriter writer=new(&sink);
                    writer.WriteEndRecord();
                    writer.WriteField((readonly(char)*)1,0);writer.WriteEndRecord();
                    writer.WriteField("a\"b",3);writer.WriteField("c,d",3);writer.WriteEndRecord();
                }
                CsvReader reader=new(output,sink.Count);char[8] field;nuint written=0;int index=0;
                while(reader.Read()){
                    if(!reader.TryCopyValue(field,8,ref written))return 2;
                    if(index==0&&(written!=1||field[0]!='x'||reader.EndOfRecord))return 3;
                    if(index==1&&(written!=1||field[0]!='y'||!reader.EndOfRecord))return 4;
                    if((index==2||index==3)&&(written!=0||!reader.EndOfRecord))return 5;
                    if(index==4&&(written!=3||field[0]!='a'||field[1]!='"'||field[2]!='b'||reader.EndOfRecord))return 6;
                    if(index==5&&(written!=3||field[0]!='c'||field[1]!=','||field[2]!='d'||!reader.EndOfRecord))return 7;
                    index++;
                }
                if(index!=6||attempts!=0)return 8;
                Sink plain=new(output);CsvWriter runs=new(&plain);
                runs.WriteField("abcdefghijklmnop",16);if(plain.Calls!=1)return 9;
                runs.WriteField("a,b",3);if(plain.Calls!=5)return 10;
                return 0;
            }
            """, optimization);
}
