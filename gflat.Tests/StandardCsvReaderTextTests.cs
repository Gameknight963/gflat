using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardCsvReaderTextTests
{
    private const string Counters="""
        using Standard; using Standard::Text; using Standard::Csv; using Standard::Memory; using Standard::Queries; using Standard::Collections;extern void*? malloc(nuint bytes);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;int failAfter=-1;bool deny=false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;if(deny||failAfter==0)return null;if(failAfter>0)failAfter--;
                void*? p=malloc(bytes);if(p!=null)allocated++;return p;
            }
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        bool Same(immutable(CsvReader)* a,immutable(CsvReader)* b) {
            Span<readonly(char)> first=a.RawField;Span<readonly(char)> second=b.RawField;
            return a.HasValue==b.HasValue&&a.FieldOffset==b.FieldOffset&&a.ValueLength==b.ValueLength
                &&a.IsQuoted==b.IsQuoted&&a.EndOfRecord==b.EndOfRecord&&first.Data==second.Data&&first.Length==second.Length;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentCsvFieldCorpusPreservesCountedBytesAndReusesReservedStorageWithoutAllocation(int optimization)
    {
        List<byte[]> fields=[[],[0],[(byte)'"'],[(byte)','],[(byte)'\r',(byte)'\n'],[65,0,66],
            Enumerable.Range(0,256).Select(value=>(byte)value).ToArray()];
        var random=new Random(192863);
        foreach(int length in new[]{0,1,2,3,7,8,31,32,63,64,127,128,255,256}) {
            byte[] bytes=new byte[length];random.NextBytes(bytes);fields.Add(bytes);
        }
        // Independent writer oracle: byte-by-byte quoting, with no product reader/decoder calls.
        var document=new List<byte>();
        for(int index=0;index<fields.Count;index++) {
            if(index!=0)document.Add((byte)',');
            bool quote=fields[index].Length==0||fields[index].Any(value=>value is 34 or 44 or 13 or 10)||index%2==0;
            if(quote)document.Add(34);
            foreach(byte value in fields[index]){document.Add(value);if(quote&&value==34)document.Add(34);}
            if(quote)document.Add(34);
        }
        string input=Convert.ToHexString(document.ToArray()),expected=Convert.ToHexString(fields.SelectMany(value=>value).ToArray());
        Run(Counters+$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int main(){
                char[{{document.Count}}] input;readonly(char)* encoded="{{input}}";readonly(char)* expected="{{expected}}";
                for(nuint index=0;index<{{document.Count}};index++)input[index]=(char)((Digit(encoded[index*2])<<4)|Digit(encoded[index*2+1]));
                int[{{fields.Count}}] lengths=[{{string.Join(',',fields.Select(value=>value.Length))}}];
                {
                    CsvReader reader=new(input,{{document.Count}});String output=s"previous";output.Reserve(256);
                    readonly(char)* storage=output.Data;nuint capacity=output.Capacity;nuint offset=0;int before=attempts;deny=true;
                    if(reader.HasValue)return 1;
                    for(nuint row=0;row<{{fields.Count}};row++){
                        if(!reader.Read()||!reader.HasValue)return 2;CsvReader saved=reader;
                        CsvReaderText::CopyValue(&reader,ref output);
                        if(output.Length!=(nuint)lengths[row]||output.Data!=storage||output.Capacity!=capacity||output.Data[output.Length]!='\0'||!Same(&reader,&saved))return 3;
                        for(nuint index=0;index<output.Length;index++)if((byte)output[index]!=(byte)((Digit(expected[(offset+index)*2])<<4)|Digit(expected[(offset+index)*2+1])))return 4;
                        if(output.Length!=0)output[0]='!';
                        CsvReaderText::CopyValue(&reader,ref output);
                        for(nuint index=0;index<output.Length;index++)if((byte)output[index]!=(byte)((Digit(expected[(offset+index)*2])<<4)|Digit(expected[(offset+index)*2+1])))return 5;
                        offset+=output.Length;
                    }
                    if(reader.Read()||reader.HasValue||attempts!=before)return 6;deny=false;
                    if(offset*2!={{expected.Length}})return 7;
                    for(nuint index=0;index<{{document.Count}};index++)if((byte)input[index]!=(byte)((Digit(encoded[index*2])<<4)|Digit(encoded[index*2+1])))return 8;
                }
                if(allocated!=released)return 9;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidCurrentBeforeReadEofResetAndFailedReadRejectsBeforeChangingDestinationOrReader(int optimization)
        =>Run(Counters+"""
            bool Reject(immutable(CsvReader)* reader,ref String output){
                CsvReader saved=*reader;readonly(char)* storage=output.Data;nuint capacity=output.Capacity;int balance=allocated-released;
                bool caught=false;deny=true;
                try{CsvReaderText::CopyValue(reader,ref output);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"CsvReader has no current field"))return false;caught=true;}
                deny=false;
                return caught&&!reader.HasValue&&Same(reader,&saved)&&output.Data==storage&&output.Capacity==capacity
                    &&output.Length==9&&CString::Equals(output.Data,"unchanged")&&allocated-released==balance;
            }
            int main(){
                {
                    String output=s"unchanged";CsvReader reader=new("x",1);
                    if(!Reject(&reader,ref output))return 1;
                    if(!reader.Read()||!reader.HasValue||reader.Read()||reader.HasValue||!Reject(&reader,ref output))return 2;
                    reader.Reset();if(reader.HasValue||!Reject(&reader,ref output))return 3;
                    CsvReader failed=new("a,\"bad",6);
                    if(!failed.Read()||!failed.HasValue)return 4;
                    bool caught=false;try{failed.Read();}catch(ref CsvParseException error){caught=true;}
                    if(!caught||failed.HasValue||!Reject(&failed,ref output))return 5;
                    caught=false;try{failed.Read();}catch(ref CsvParseException error){caught=true;}
                    if(!caught||failed.HasValue||!Reject(&failed,ref output))return 6;
                    failed.Reset();if(failed.HasValue||!failed.Read()||!failed.HasValue)return 7;
                    CsvReader saved=failed;CsvReaderText::CopyValue(&failed,ref output);
                    if(output.Length!=1||output[0]!='a'||!Same(&failed,&saved))return 8;
                }
                if(allocated!=released)return 9;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryMeasuredGrowthAllocationFailurePreservesDestinationAndReaderAndRetrySucceeds(int optimization)
        =>Run(Counters+"""
            int main(){
                char[96] input;for(nuint index=0;index<96;index++)input[index]=(char)('a'+(int)(index%26));
                {
                    CsvReader reader=new(input,96);if(!reader.Read()||!reader.HasValue)return 1;CsvReader saved=reader;
                    int points=0;
                    {String measured=s"old";int before=allocated;CsvReaderText::CopyValue(&reader,ref measured);points=allocated-before;if(points!=1||measured.Length!=96)return 2;}
                    for(int point=0;point<points;point++){
                        String output=s"unchanged";readonly(char)* storage=output.Data;nuint capacity=output.Capacity;int balance=allocated-released;
                        failAfter=point;bool caught=false;
                        try{CsvReaderText::CopyValue(&reader,ref output);}catch(ref BadAllocException error){caught=true;}
                        failAfter=-1;
                        if(!caught||!Same(&reader,&saved)||!reader.HasValue||output.Data!=storage||output.Capacity!=capacity||!CString::Equals(output.Data,"unchanged")||allocated-released!=balance)return 3;
                        for(nuint index=0;index<96;index++)if(input[index]!=(char)('a'+(int)(index%26)))return 4;
                        int before=allocated;CsvReaderText::CopyValue(&reader,ref output);
                        if(allocated!=before+1||output.Length!=96||output.Data[96]!='\0'||!Same(&reader,&saved))return 5;
                        for(nuint index=0;index<96;index++)if(output[index]!=input[index])return 6;
                    }
                }
                if(allocated!=released)return 7;return 0;
            }
            """,optimization);

    [Fact]
    public void HasValueUsesActualCoreReaderWithoutStringDependencyOrExceptionBoundary()
    {
        string[] names=["core/Csv/CsvReader.gf","core/Csv/CsvParseException.gf","core/Memory/Span.gf",
            "core/Collections/IEnumerable.gf","core/Queries/Enumerable.gf","core/Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Standard",name))))
            .Append(new SourceFile("csv-value-contract.gf","""
                namespace Memory{public void*? Allocate(nuint bytes){return null;}public void Free(void* storage)nothrow{}}
                bool Observe(immutable(Standard::Csv::CsvReader)* reader)nothrow=>reader.HasValue;
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true});
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Observe\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke",body);Assert.DoesNotContain("__gflat_eh_",body);
        Assert.DoesNotContain("$String",ir);
    }

    private static void Run(string program,int optimization){
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("csv-reader-text.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"CsvReaderText exit {result.ExitCode}: {result.StandardError}");
    }
}

