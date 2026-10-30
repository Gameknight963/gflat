using System.Globalization;
using System.Text;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerParsingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ValidDecimalSpellingsMatchIndependentBigIntegerAndPreserveInput(int optimization)
    {
        await WithDirectory(directory =>
        {
            var values = new List<Integer> { 0, 1, -1, int.MinValue, int.MaxValue, long.MinValue, long.MaxValue };
            foreach (int digits in new[] { 9, 18, 27, 72, 3600, 12000 })
            {
                Integer power = Integer.Pow(10, digits);
                values.AddRange([power - 1, power, power + 1, -power + 1, -power, -power - 1]);
            }
            var random = new Random(991327);
            for (int sample = 0; sample < 100; sample++)
            {
                byte[] bytes = new byte[random.Next(1, 513)]; random.NextBytes(bytes); values.Add(new Integer(bytes));
            }
            var rows = new List<(string Text, string Expected)>();
            foreach (Integer value in values)
            {
                string expected = value.ToString(CultureInfo.InvariantCulture);
                rows.Add((expected, expected));
                rows.Add(((value.Sign < 0 ? "-" : "+") + "00000" + Integer.Abs(value).ToString(CultureInfo.InvariantCulture), expected));
            }
            rows.Add(("-000000000000000000000", "0")); rows.Add(("+0", "0")); rows.Add(("000", "0"));
            string manifest = Path.Combine(directory, "valid.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Count);
                foreach (var row in rows) { WriteText(writer, Encoding.ASCII.GetBytes(row.Text)); WriteText(writer, Encoding.ASCII.GetBytes(row.Expected)); }
            }
            Run(directory, Counters + ReadText + """
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String text=ReadText(&input);String saved=text;String expected=ReadText(&input);
                            BigInteger first=new(777);BigInteger second=new(-777);
                            if(!BigInteger::TryParse(text.Data,text.Length,ref first)||!BigInteger::TryParse(text.Data,ref second))return 1;
                            String actual=first.ToString();String other=second.ToString();
                            if(!actual.Equals(&expected)||!other.Equals(&expected)||!text.Equals(&saved))return 2;
                            BigInteger legacy=BigInteger::Parse(text.Data,text.Length);
                            if(!first.Equals(&second)||!first.Equals(&legacy)||first.GetHashCode()!=legacy.GetHashCode())return 3;
                            first=new(9);String unchanged=second.ToString();if(!unchanged.Equals(&expected)||!text.Equals(&saved))return 4;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 5;return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MalformedAndZeroInputsDoNotAllocateAndReleasePreviousOwnersExactlyOnce(int optimization)
    {
        await WithDirectory(directory =>
        {
            string[] malformed = ["", "+", "-", " 1", "1 ", "\t1", "1\r\n", "1.0", "1_000", "1,000",
                "0x10", "1e3", "--1", "++1", "+-1", "-+1", "12\0", "\012", "1\02",
                new string('9', 24000) + "x", "+" + new string('0', 24000) + "x", "１２", "−1"];
            var rows = malformed.Select(Encoding.UTF8.GetBytes).ToList(); rows.Add([49, 255]); rows.Add([128, 49]);
            string manifest = Path.Combine(directory, "invalid.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Count); foreach (byte[] row in rows) WriteText(writer, row);
            }
            Run(directory, Counters + ReadText + """
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String text=ReadText(&input);String saved=text;bool nul=false;
                            for(nuint i=0;i<text.Length;i++)if(text.Data[i]=='\0')nul=true;
                            for(int kind=0;kind<2;kind++){
                                if(kind==1&&nul)continue;
                                int before=allocated;BigInteger result=BigInteger::Parse("123456789012345678901234567890");
                                int owned=allocated-before;int freedBefore=released;int attemptsBefore=attempts;deny=true;bool success=false;
                                if(kind==0)success=BigInteger::TryParse(text.Data,text.Length,ref result);
                                else success=BigInteger::TryParse(text.Data,ref result);
                                deny=false;
                                if(success||result.Sign!=0||attempts!=attemptsBefore||released-freedBefore!=owned||!text.Equals(&saved))return 1;
                            }
                        }
                        // Zero-length counted input must not be dereferenced.
                        int before=allocated;BigInteger target=new(777);int owned=allocated-before;
                        int freedBefore=released;int attemptsBefore=attempts;deny=true;
                        if(BigInteger::TryParse(((nuint)1).reinterpret<readonly(char)*>(),0,ref target))return 2;
                        deny=false;if(target.Sign!=0||attempts!=attemptsBefore||released-freedBefore!=owned)return 3;
                        readonly(char)* zeros="-000000000000000000000000000000000000000000000000000000000000";
                        nuint zeroCount=0;while(zeros[zeroCount]!='\0')zeroCount++;
                        for(int kind=0;kind<2;kind++){
                            before=allocated;target=new(777);owned=allocated-before;freedBefore=released;attemptsBefore=attempts;deny=true;
                            bool success=false;
                            if(kind==0)success=BigInteger::TryParse(zeros,zeroCount,ref target);
                            else success=BigInteger::TryParse(zeros,ref target);
                            deny=false;
                            if(!success||target.Sign!=0||attempts!=attemptsBefore||released-freedBefore!=owned)return 4;
                        }
                        char[4] prefix=['4','2','\0','x'];if(!BigInteger::TryParse(prefix,ref target))return 5;
                        BigInteger expected=new(42);if(!target.Equals(&expected))return 6;
                        input.Close();
                    }
                    if(allocated!=released)return 7;return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MeasuredConstructionFailuresPreserveOldOwnerAndSuccessCommitsWithoutDeepCopy(int optimization)
    {
        await WithDirectory(directory =>
        {
            string manifest = Path.Combine(directory, "allocation.bin");
            string[] values = ["1", "-999999999999999999999999999", new string('9', 24000)];
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(values.Length); foreach (string value in values) WriteText(writer, Encoding.ASCII.GetBytes(value));
            }
            Run(directory, Counters + ReadText + """
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        BigInteger target=BigInteger::Parse("777777777777777777777");BigInteger saved=target;
                        for(int row=0;row<rows;row++){
                            String text=ReadText(&input);String untouched=text;
                            int before=allocated;int parsePoints=0;
                            {BigInteger probe=BigInteger::Parse(text.Data,text.Length);parsePoints=allocated-before;}
                            if(parsePoints==0)return 1;
                            for(int kind=0;kind<2;kind++){
                                for(int point=0;point<parsePoints;point++){
                                    int live=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                    try{
                                        bool success=false;
                                        if(kind==0)success=BigInteger::TryParse(text.Data,text.Length,ref target);
                                        else success=BigInteger::TryParse(text.Data,ref target);
                                        return 2;
                                    }catch(BadAllocException* error){caught=true;}
                                    failAfter=-1;
                                    // Denied EH payload calls can use the emergency reserve;
                                    // count only successful app allocations when checking leaks.
                                    if(!caught||denied==deniedBefore||allocated-released!=live||!target.Equals(&saved)||!text.Equals(&untouched))return 3;
                                }
                                before=allocated;BigInteger destination=new(73);int oldAllocations=allocated-before;
                                int freedBefore=released;before=allocated;bool success=false;
                                if(kind==0)success=BigInteger::TryParse(text.Data,text.Length,ref destination);
                                else success=BigInteger::TryParse(text.Data,ref destination);
                                if(!success||allocated-before!=parsePoints)return 4;
                                // A successful replacement frees the prior owner, and parsing
                                // adds no result-copy allocation beyond the legacy fresh owner.
                                if(released-freedBefore<oldAllocations)return 5;
                                BigInteger expected=BigInteger::Parse(text.Data,text.Length);
                                if(!destination.Equals(&expected)||!target.Equals(&saved)||!text.Equals(&untouched))return 6;
                            }
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 7;return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ReadableOldLimbBytesStayAliveUntilParsingCommits(int optimization)
        => await WithDirectory(directory => Run(directory, Counters + """
            int main(){
                {
                    for(int kind=0;kind<4;kind++){
                        captured=null;capture=true;BigInteger owner=BigInteger::Parse("1");capture=false;
                        if(captured==null||capturedBytes<4)return 1;
                        char* text=(char*)captured!;
                        // The initialized uint remains below radix 10^9 and nonzero
                        // with either native byte order; owner metadata is untouched.
                        text[0]='1';text[1]='2';text[2]='3';text[3]='4';
                        if(kind==1)text[3]='\0';
                        if(kind==2)text[1]='x';
                        watched=captured;watchedFrees=0;
                        if(kind==3){
                            BigInteger saved=owner;int live=allocated-released;failAfter=0;bool caught=false;
                            try{BigInteger::TryParse(text,4,ref owner);return 2;}catch(BadAllocException* error){caught=true;}
                            failAfter=-1;
                            if(!caught||watchedFrees!=0||!owner.Equals(&saved)||allocated-released!=live)return 3;
                            watched=null;
                        }else{
                            bool success=false;
                            if(kind==1)success=BigInteger::TryParse(text,ref owner);
                            else success=BigInteger::TryParse(text,4,ref owner);
                            if(watchedFrees!=1)return 4;watched=null;
                            if(kind==2){if(success||owner.Sign!=0)return 5;}
                            else{BigInteger expected=new(1234);if(kind==1)expected=new(123);
                                if(!success||!owner.Equals(&expected))return 6;}
                        }
                    }
                }
                if(allocated!=released)return 7;return 0;
            }
            """, [], optimization));

    private const string Counters = """
        using Standard; using Standard::Text; using Standard::IO; using Standard::Memory; using Standard::Numerics; using Standard::Collections;
        extern void*? malloc(nuint count);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        bool capture=false;void*? captured=null;nuint capturedBytes=0;void*? watched=null;int watchedFrees=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null){allocated++;if(capture&&captured==null){captured=value;capturedBytes=count;}}
                return value;
            }
            public replace void Free(void* value)nothrow{if(value==watched)watchedFrees++;released++;free(value);}
        }
        """;
    private const string ReadText = """
        String ReadText(File* input){
            nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);String text=new();text.Resize(count);
            if(count!=0){Span<char> bytes=text.AsSpan();input.ReadExactly(bytes.Data!,count);}return move text;
        }
        """;
    private static void WriteText(BinaryWriter writer,byte[] text){writer.Write(text.Length);writer.Write(text);}
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bigint-parse-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string program,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-try-parse.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(executable,arguments,30000);Assert.True(result.ExitCode==0,$"BigInteger parsing exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
