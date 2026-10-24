using System.Text;

namespace gflat.Tests;

public sealed class StandardFormattedLinePrefixWriterTests
{
    private const string Allocator="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;int formats=0;int destroyed=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SampledPartitionsFollowIndependentPerInputLfModelAndBorrowNoncopyableProviderWithoutAllocation(int optimization)
    {
        var rows=new List<(byte[] Input,byte[] Prefix,int[] Chunks)>();
        byte[][] prefixes=[[],[62],[10,0,255],[13,10,255,0]];
        foreach(byte[] input in new byte[][]{[],[10],[13,10],[97,13,10,10,98,10],[0,128,255,10,0]})
        foreach(byte[] prefix in prefixes)
        for(int mask=0;mask<(1<<Math.Max(0,input.Length-1));mask++){
            var chunks=new List<int>();int start=0;
            for(int i=1;i<input.Length;i++)if((mask&(1<<(i-1)))!=0){chunks.Add(i-start);start=i;}
            chunks.Add(input.Length-start);rows.Add((input,prefix,chunks.ToArray()));
        }
        rows.Add((Enumerable.Range(0,256).Select(value=>(byte)value).ToArray(),[10,0,255],Enumerable.Repeat(1,256).ToArray()));
        var random=new Random(795631);for(int sample=0;sample<24;sample++){
            byte[] input=new byte[random.Next(1,129)];random.NextBytes(input);var chunks=new List<int>();int position=0;
            while(position<input.Length){int count=Math.Min(random.Next(1,18),input.Length-position);chunks.Add(count);position+=count;}
            rows.Add((input,prefixes[sample%prefixes.Length],chunks.ToArray()));
        }
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var row in rows){
            var expected=new List<byte>();bool lineStart=true;int calls=0;
            foreach(byte value in row.Input){
                if(lineStart){calls++;if(row.Prefix.Length!=0){expected.Add((byte)('0'+calls%10));expected.AddRange(row.Prefix);}}
                expected.Add(value);lineStart=value==10;
            }
            writer.Write((byte)row.Prefix.Length);writer.Write((ushort)row.Input.Length);writer.Write((ushort)row.Chunks.Length);writer.Write((ushort)expected.Count);writer.Write((ushort)calls);
            writer.Write(row.Prefix);writer.Write(row.Input);writer.Write(expected.ToArray());foreach(int chunk in row.Chunks)writer.Write((ushort)chunk);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            struct Prefix:IFormattable{
                private readonly(char)* data;private nuint length;private String token;
                public Prefix(readonly(char)* bytes,nuint count){data=bytes;length=count;token=s"prefix";}
                public immutable void Format(ITextWriter* output){formats++;if(length!=0){char digit=(char)('0'+formats%10);output.Write(&digit,1);output.Write(data,length);}}
                ~Prefix(){destroyed++;}
            }
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
            nuint ReadSize(readonly(char)* text,ref nuint at)nothrow{int low=ReadByte(text,ref at);return (nuint)(low|(ReadByte(text,ref at)<<8));}
            void ReadBytes(readonly(char)* text,ref nuint at,char* output,nuint count)nothrow{for(nuint i=0;i<count;i++)output[i]=(char)ReadByte(text,ref at);}
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{rows.Count}}};row++){
                    nuint prefixLength=(nuint)ReadByte(data,ref at);nuint inputLength=ReadSize(data,ref at);nuint chunks=ReadSize(data,ref at);nuint expectedLength=ReadSize(data,ref at);nuint expectedCalls=ReadSize(data,ref at);
                    char[8] prefixBytes;char[256] input;char[2048] expected;char[2049] output;
                    ReadBytes(data,ref at,prefixBytes,prefixLength);ReadBytes(data,ref at,input,inputLength);ReadBytes(data,ref at,expected,expectedLength);
                    for(nuint i=0;i<2049;i++)output[i]='!';formats=0;int dtorBefore=destroyed;
                    {
                        Prefix prefix=new(prefixBytes,prefixLength);immutable(IFormattable)* provider=&prefix;BufferWriter sink=new(output,2049);
                        int before=attempts;deny=true;
                        {LinePrefixWriter writer=new(&sink,provider);readonly(char)*? absent=null;writer.Write(absent!,0);nuint position=0;
                            for(nuint part=0;part<chunks;part++){nuint count=ReadSize(data,ref at);writer.Write((readonly(char)*)input+position,count);writer.Write(absent!,0);position+=count;}
                            if(position!=inputLength||sink.Length!=expectedLength||formats!=(int)expectedCalls||attempts!=before)return 1;
                            for(nuint i=0;i<expectedLength;i++)if(output[i]!=expected[i])return 2;
                            if(output[expectedLength]!='\0'||output[expectedLength+1]!='!')return 3;
                        }
                        deny=false;if(destroyed!=dtorBefore||sink.Length!=expectedLength)return 4;
                    }
                    if(destroyed!=dtorBefore+1||allocated!=released)return 5;
                }
                if(at!={{{hex.Length}}})return 6;
                {
                    char[1] prefixBytes=['p'];Prefix prefix=new(prefixBytes,1);immutable(IFormattable)* provider=&prefix;char[64] bytes;BufferWriter sink=new(bytes,64);formats=0;
                    LinePrefixWriter first=new(&sink,provider);first.Write("a",1);LinePrefixWriter copied=first;sink.Clear();
                    copied.Write("b\n",2);if(sink.Length!=2||bytes[0]!='b'||bytes[1]!='\n'||formats!=1)return 7;
                    sink.Clear();first.Write("c",1);if(sink.Length!=1||bytes[0]!='c'||formats!=1)return 8;
                    sink.Clear();prefixBytes[0]='q';LinePrefixWriter moved=move copied;moved.Write("d",1);
                    if(sink.Length!=3||bytes[0]!='2'||bytes[1]!='q'||bytes[2]!='d'||formats!=2)return 9;
                }
                if(allocated!=released)return 10;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PartialFormatterAndSinkFailuresKeepOriginalErrorAndPoisonCopiesAndEmptyWrites(int optimization)
        =>Run(Allocator+"""
            bool formatterFails=false;
            struct Prefix:IFormattable{
                private String token;public Prefix(){token=s"prefix";}
                public immutable void Format(ITextWriter* output){formats++;output.Write("p",1);if(formatterFails)throw new Exception("formatter",71);output.Write(":",1);}
                ~Prefix(){destroyed++;}
            }
            struct Sink:ITextWriter{
                public int Calls;public nuint Count;private nuint limit;private char* bytes;
                public Sink(char* storage,nuint budget){Calls=0;Count=0;limit=budget;bytes=storage;}
                public void Write(readonly(char)* data,nuint count){Calls++;nuint at=0;while(at<count&&Count<limit){bytes[Count]=data[at];Count++;at++;}if(at!=count)throw new Exception("sink",77);}
            }
            int main(){
                for(int mode=0;mode<2;mode++)for(nuint budget=0;budget<10;budget++){
                    {
                        Prefix prefix=new();immutable(IFormattable)* provider=&prefix;char[16] bytes;nuint limit=budget;
                        formatterFails=mode==0;if(formatterFails)limit=16;
                        Sink sink=new(bytes,limit);LinePrefixWriter writer=new(&sink,provider);formats=0;
                        int live=allocated-released;bool caught=false;
                        try{writer.Write("a\n\nb",4);}catch(ref Exception error){int expected=77;if(formatterFails)expected=71;if(error.GetCode()!=expected)return 1;caught=true;}
                        if(!caught||allocated-released!=live)return 2;
                        if(formatterFails){if(sink.Count!=1||bytes[0]!='p'||formats!=1)return 3;}
                        else{if(sink.Count!=budget)return 4;for(nuint i=0;i<budget;i++)if(bytes[i]!="p:a\np:\np:b"[i])return 5;}
                        int calls=sink.Calls;int providerCalls=formats;int errors=0;readonly(char)*? absent=null;
                        try{writer.Write("x",1);}catch(ref Exception error){errors++;}
                        try{writer.Write(absent!,0);}catch(ref Exception error){errors++;}
                        LinePrefixWriter copied=writer;try{copied.Write("x",1);}catch(ref Exception error){errors++;}
                        if(errors!=3||sink.Calls!=calls||formats!=providerCalls||allocated-released!=live)return 6;
                    }
                    if(allocated!=released)return 7;
                    if(mode==0)break;
                }
                return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("formatted-prefix.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Formatted prefix exit {result.ExitCode}: {result.StandardError}");
    }
}
