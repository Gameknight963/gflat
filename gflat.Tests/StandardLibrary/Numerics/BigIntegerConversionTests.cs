using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerConversionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CheckedPrimitiveConversionsMatchIndependentExactRanges(int optimization)
        => await WithManifest((directory,manifest)=>Run(directory,Counters+ReadRow+"""
            int main(int argc,char** argv){
                {
                    File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                    for(int row=0;row<rows;row++){
                        String text=ReadText(&input);bool fitsLong=FileBinary::ReadInt32LittleEndian(&input)!=0;
                        long expectedLong=FileBinary::ReadInt64LittleEndian(&input);
                        bool fitsULong=FileBinary::ReadInt32LittleEndian(&input)!=0;
                        ulong expectedULong=FileBinary::ReadUInt64LittleEndian(&input);
                        BigInteger value=BigInteger::Parse(text.Data,text.Length);BigInteger saved=value;nuint hash=value.GetHashCode();
                        immutable(BigInteger)* deep=&value;readonly(BigInteger)* shallow=&value;
                        long signedValue=73l;ulong unsignedValue=79ul;
                        if(deep.TryToLong(ref signedValue)!=fitsLong||signedValue!=expectedLong)return 1;
                        if(shallow.TryToULong(ref unsignedValue)!=fitsULong||unsignedValue!=expectedULong)return 2;
                        long signedTarget=123l;ulong unsignedTarget=123ul;bool caught=false;
                        try{signedTarget=value.ToLong();}
                        catch(Exception* error){caught=true;if(!CString::Equals(error.GetMessage(),"BigInteger does not fit in long"))return 3;}
                        if(fitsLong){if(caught||signedTarget!=expectedLong)return 4;}
                        else if(!caught||signedTarget!=123l)return 5;
                        caught=false;
                        try{unsignedTarget=deep.ToULong();}
                        catch(Exception* error){caught=true;if(!CString::Equals(error.GetMessage(),"BigInteger does not fit in ulong"))return 6;}
                        if(fitsULong){if(caught||unsignedTarget!=expectedULong)return 7;}
                        else if(!caught||unsignedTarget!=123ul)return 8;
                        String unchanged=value.ToString();
                        if(!unchanged.Equals(&text)||!value.Equals(&saved)||value.GetHashCode()!=hash)return 9;
                    }
                    input.Close();
                }
                if(allocated!=released)return 10;return 0;
            }
            """,[manifest],optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NormalConversionsAllocateNothingAndOverflowKeepsTargetsUnderAllocationDenial(int optimization)
        => await WithManifest((directory,manifest)=>Run(directory,Counters+ReadRow+"""
            int main(int argc,char** argv){
                {
                    File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                    for(int row=0;row<rows;row++){
                        String text=ReadText(&input);bool fitsLong=FileBinary::ReadInt32LittleEndian(&input)!=0;
                        long expectedLong=FileBinary::ReadInt64LittleEndian(&input);
                        bool fitsULong=FileBinary::ReadInt32LittleEndian(&input)!=0;
                        ulong expectedULong=FileBinary::ReadUInt64LittleEndian(&input);
                        BigInteger value=BigInteger::Parse(text.Data,text.Length);BigInteger saved=value;
                        readonly(BigInteger)* shallow=&value;immutable(BigInteger)* deep=&value;
                        int before=attempts;int live=allocated-released;deny=true;
                        long signedValue=73l;ulong unsignedValue=79ul;
                        if(shallow.TryToLong(ref signedValue)!=fitsLong||signedValue!=expectedLong)return 1;
                        if(deep.TryToULong(ref unsignedValue)!=fitsULong||unsignedValue!=expectedULong)return 2;
                        if(fitsLong&&value.ToLong()!=expectedLong)return 3;
                        if(fitsULong&&deep.ToULong()!=expectedULong)return 4;
                        deny=false;
                        if(attempts!=before||allocated-released!=live||!value.Equals(&saved))return 5;
                        // Overflow remains a typed range exception even when its payload
                        // allocation needs the existing emergency reserve.
                        if(!fitsLong){
                            long target=123l;bool caught=false;live=allocated-released;deny=true;
                            try{target=value.ToLong();}
                            catch(Exception* error){caught=CString::Equals(error.GetMessage(),"BigInteger does not fit in long");}
                            deny=false;if(!caught||target!=123l||allocated-released!=live)return 6;
                        }
                        if(!fitsULong){
                            ulong target=123ul;bool caught=false;live=allocated-released;deny=true;
                            try{target=shallow.ToULong();}
                            catch(Exception* error){caught=CString::Equals(error.GetMessage(),"BigInteger does not fit in ulong");}
                            deny=false;if(!caught||target!=123ul||allocated-released!=live)return 7;
                        }
                        if(!value.Equals(&saved))return 8;
                    }
                    input.Close();
                }
                if(allocated!=released)return 9;return 0;
            }
            """,[manifest],optimization));

    [Fact]
    public void PublicTryDefinitionsAndCallsRetainTheirNothrowContract()
    {
        string ir=Compiler.Emit(Sources("""
            using Standard; using Standard::Text; using Standard::IO; using Standard::Memory; using Standard::Numerics; using Standard::Collections;
            bool Convert(immutable(BigInteger)* value,ref long signedValue,ref ulong unsignedValue)nothrow{
                bool first=value.TryToLong(ref signedValue);bool second=value.TryToULong(ref unsignedValue);return first&&second;
            }
            """),options:new(){InternalizeFunctions=true});
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Convert\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke",body);
        foreach(string name in new[]{"TryToLong","TryToULong"})
        {
            string definition=Assert.Single(ir.Split('\n'),line=>line.StartsWith("define ",StringComparison.Ordinal)&&
                line.Contains("$BigInteger$"+name+"(",StringComparison.Ordinal));
            Assert.Contains(" nounwind",definition);Assert.Contains("$"+name+"(",body);
        }
    }

    [Fact]
    public async Task ManifestActionFailureRetainsItsOriginalExceptionAndFixture()
    {
        const string ir = "controlled fixture IR\n";
        byte[] executable = [1, 2, 3, 4];
        byte[] expectedManifest = [];
        string? manifestPath = null;
        var original = new InvalidOperationException("controlled manifest action failure");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => WithManifest((directory, manifest) =>
        {
            manifestPath = manifest;
            expectedManifest = File.ReadAllBytes(manifest);
            File.WriteAllText(Path.Combine(directory, "main.ll"), ir);
            File.WriteAllBytes(Path.Combine(directory, "main.exe"), executable);
            throw original;
        }));
        string retained = Assert.IsType<string>(error.Data["BigIntegerConversionDirectory"]);
        try
        {
            Assert.Same(original, error);
            Assert.Contains("WithManifest", error.StackTrace);
            Assert.True(Directory.Exists(retained));
            Assert.NotNull(manifestPath);
            Assert.Equal(retained, Path.GetDirectoryName(manifestPath));
            Assert.NotEmpty(expectedManifest);
            Assert.Equal(expectedManifest, File.ReadAllBytes(manifestPath!));
            Assert.Equal(ir, File.ReadAllText(Path.Combine(retained, "main.ll")));
            Assert.Equal(executable, File.ReadAllBytes(Path.Combine(retained, "main.exe")));
        }
        catch(Exception assertionError)
        {
            assertionError.Data["BigIntegerConversionDirectory"] = retained;
            throw;
        }
        // This was an expected failure; remove its fixture after validating retention.
        await TestDirectoryCleanup.DeleteAsync(retained);
    }

    [Fact]
    public async Task SuccessfulManifestActionCleansItsFixture()
    {
        string? completed = null;
        await WithManifest((directory, manifest) =>
        {
            completed = directory;
            Assert.True(File.Exists(manifest));
            Assert.True(new FileInfo(manifest).Length > 0);
        });
        Assert.NotNull(completed);
        Assert.False(Directory.Exists(completed), $"Successful fixture was retained: {completed}");
    }

    private static Integer[] Values()
    {
        var values=new SortedSet<Integer>{0,1,-1,int.MinValue,int.MaxValue,long.MinValue,long.MaxValue,ulong.MaxValue};
        foreach(int bits in new[]{7,8,15,16,31,32,63,64,65,95,96,127,128,255,256})
            foreach(int delta in new[]{-2,-1,0,1,2})
            {
                Integer value=(Integer.One<<bits)+delta;values.Add(value);values.Add(-value);
            }
        foreach(Integer tail in new Integer[]{0,1,long.MaxValue,ulong.MaxValue})
        {
            values.Add((Integer.One<<64)*1000000000+tail);values.Add(-((Integer.One<<64)*1000000000+tail));
            values.Add(Integer.Pow(10,90)+tail);values.Add(-Integer.Pow(10,90)-tail);
        }
        var random=new Random(646327);
        for(int sample=0;sample<100;sample++)
        {
            byte[] bytes=new byte[random.Next(1,129)];random.NextBytes(bytes);values.Add(new Integer(bytes));
        }
        return values.ToArray();
    }
    private static async Task WithManifest(Action<string,string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bigint-convert-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Integer[] values=Values();string manifest=Path.Combine(directory,"ranges.bin");
            using(var writer=new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(values.Length);
                foreach(Integer value in values)
                {
                    byte[] text=Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));writer.Write(text.Length);writer.Write(text);
                    bool fitsLong=value>=long.MinValue&&value<=long.MaxValue;
                    bool fitsULong=value>=0&&value<=ulong.MaxValue;
                    writer.Write(fitsLong?1:0);writer.Write(fitsLong?(long)value:0L);
                    writer.Write(fitsULong?1:0);writer.Write(fitsULong?(ulong)value:0ul);
                }
            }
            action(directory,manifest);
        }
        catch(Exception error){error.Data["BigIntegerConversionDirectory"]=directory;throw;}
        await TestDirectoryCleanup.DeleteAsync(directory);
    }
    private const string Counters="""
        using Standard; using Standard::Text; using Standard::IO; using Standard::Memory; using Standard::Numerics; using Standard::Collections;
        extern void*? malloc(nuint count);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* value)nothrow{released++;free(value);}
        }
        """;
    private const string ReadRow="""
        String ReadText(File* input){
            nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);String text=new();text.Resize(count);
            if(count!=0){Span<char> bytes=text.AsSpan();input.ReadExactly(bytes.Data!,count);}return move text;
        }
        """;
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-conversions.gf",program));
    private static void Run(string directory,string program,string[] arguments,int optimization)
    {
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(Sources(program),options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(executable,arguments);Assert.True(result.ExitCode==0,$"BigInteger conversions exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
