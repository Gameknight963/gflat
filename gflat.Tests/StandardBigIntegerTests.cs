using System.Globalization;
using System.Text;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests;

public sealed class StandardBigIntegerTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("main.gf", code));

    private static void Run(string code, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources(code), optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StreamedArithmeticAndDecimalFormattingMatchIndependentBigInteger(int optimization)
    {
        string path=Path.Combine(Path.GetTempPath(),$"gflat-big-integer-{Guid.NewGuid():N}.bin");
        try
        {
            int rows=0;
            using(var output=new BinaryWriter(File.Create(path)))
            {
                var boundaries=new List<Integer>{0,1,-1,long.MinValue,long.MaxValue};
                foreach(int digits in new[]{9,18,27,36,72})
                {
                    Integer power=Integer.Pow(10,digits);
                    boundaries.AddRange(new[]{power-1,power,power+1,-power+1,-power,-power-1});
                }
                foreach(Integer a in boundaries)
                foreach(Integer b in boundaries)Write(a,b);
                var random=new Random(94731);
                for(int i=0;i<256;i++)Write(RandomInteger(),RandomInteger());
                Integer huge=Integer.Pow(10,3600)-1;
                foreach(Integer b in new[]{Integer.Zero,Integer.One,-Integer.One,huge,-huge})Write(huge,b);
                void Write(Integer a,Integer b)
                {
                    foreach(Integer value in new[]{a,b,a+b,a-b,a*b})
                    {
                        byte[] bytes=Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
                        output.Write(bytes.Length);output.Write(bytes);
                    }
                    output.Write(a.CompareTo(b));rows++;
                }
                Integer RandomInteger()
                {
                    int digits=random.Next(1,501);
                    var text=new StringBuilder(digits+1);
                    if(random.Next(2)==0)text.Append('-');
                    text.Append((char)('1'+random.Next(9)));
                    for(int digit=1;digit<digits;digit++)text.Append((char)('0'+random.Next(10)));
                    return Integer.Parse(text.ToString(),CultureInfo.InvariantCulture);
                }
            }
            Run("""
                using std;
                extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* buffer,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);
                String ReadText(void* stream){
                    int length=0;
                    if(fread(&length,4,1,stream)!=1||length<0||length>8192)throw new Exception("bad oracle length");
                    char[8192] bytes;
                    if(fread((void*)(char*)bytes,1,(nuint)length,stream)!=(nuint)length)throw new Exception("short oracle");
                    return String::From(bytes,(nuint)length);
                }
                int main(){
                    void* stream=fopen("PATH","rb");
                    for(int row=0;row<ROWS;row++){
                        String aText=ReadText(stream);String bText=ReadText(stream);
                        BigInteger a=BigInteger::Parse(aText.Data,aText.Length);
                        BigInteger b=BigInteger::Parse(bText.Data,bText.Length);
                        String normalized=a.ToString();if(!normalized.Equals(&aText))return 1;
                        BigInteger sum=a.Add(&b);BigInteger difference=a.Subtract(&b);BigInteger product=a.Multiply(&b);
                        String expected=ReadText(stream);String actual=sum.ToString();if(!actual.Equals(&expected))return 2;
                        expected=ReadText(stream);actual=difference.ToString();if(!actual.Equals(&expected))return 3;
                        expected=ReadText(stream);actual=product.ToString();if(!actual.Equals(&expected))return 4;
                        int comparison=0;if(fread(&comparison,4,1,stream)!=1||a.CompareTo(&b)!=comparison)return 5;
                        BigInteger parsed=BigInteger::Parse(aText.Data,aText.Length);
                        if(!a.Equals(&parsed)||a.GetHashCode()!=parsed.GetHashCode())return 6;
                        actual=a.ToString();if(!actual.Equals(&aText))return 7;
                        actual=b.ToString();if(!actual.Equals(&bText))return 8;
                    }
                    return fclose(stream);
                }
                """.Replace("PATH",path.Replace("\\","/")).Replace("ROWS",rows.ToString()),optimization);
        }
        finally{File.Delete(path);}
    }

    private const string Allocator="""
        using std;
        int allocations=0;int releases=0;bool deny=false;int failAfter=-1;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        namespace Memory{
            public replace void*? Allocate(nuint bytes){
                if(deny||failAfter==0)return null;if(failAfter>0)failAfter--;
                allocations++;return malloc(bytes);
            }
            public replace void Free(void* storage){releases++;free(storage);}
        }
        bool TextEquals(immutable(BigInteger)* value,readonly(char)* text){
            String actual=value.ToString();String expected=String::From(text);return actual.Equals(&expected);
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CanonicalZeroStrictParsingAndLongBoundaries(int optimization)
    {
        string[] invalid={"","+","-"," 1","1 ","\t1","1\n","1.0","1_0","--1","+-1","12\0" ,"123456789123456789123456789x","１２"};
        var cases=new StringBuilder();
        foreach(string text in invalid)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(text);
            string values=string.Join(",",bytes.Select(value=>$"(char){value}"));
            cases.AppendLine($"{{char[{Math.Max(1,bytes.Length)}] bytes=[{(bytes.Length==0?"(char)0":values)}];");
            cases.AppendLine($"try{{BigInteger parsed=BigInteger::Parse(bytes,{bytes.Length});return 10;}}catch(ref BadAllocException error){{return 11;}}catch(ref Exception error){{}}}}");
        }
        Run(Allocator+"""
            int main(){
                {
                    deny=true;BigInteger empty=new();BigInteger zero=BigInteger::Parse("-00000");
                    BigInteger positiveZero=BigInteger::Parse("+0000",5);deny=false;
                    if(empty.Sign!=0||!zero.Equals(&empty)||!positiveZero.Equals(&empty)||allocations!=0)return 1;
                    if(zero.GetHashCode()!=empty.GetHashCode())return 2;
                    BigInteger low=new BigInteger((long)((ulong)1<<63));
                    BigInteger high=new BigInteger((long)(((ulong)1<<63)-1ul));
                    if(!TextEquals(&low,"-9223372036854775808")||!TextEquals(&high,"9223372036854775807"))return 3;
                    BigInteger a=BigInteger::Parse("123456789123456789");
                    BigInteger b=BigInteger::Parse("-123456789123456789");
                    int before=allocations;deny=true;
                    BigInteger sum=a.Add(&b);BigInteger difference=a.Subtract(&a);BigInteger product=a.Multiply(&zero);
                    deny=false;
                    if(allocations!=before||sum.Sign!=0||difference.Sign!=0||product.Sign!=0)return 4;
                    char[4] counted=['4','2','\0','x'];
                    BigInteger prefix=BigInteger::Parse(counted,2);
                    if(!TextEquals(&prefix,"42"))return 5;
                    // Invalid syntax is validated before any limb buffer is allocated.
                    deny=true;
            """+cases+"""
                    deny=false;
                }
                if(allocations!=releases)return 6;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeepCopiesMovesCollectionsAndFailedAssignmentsPreserveOwnership(int optimization)
        =>Run(Allocator+"""
            int main(){
                {
                    BigInteger source=BigInteger::Parse("123456789012345678901234567890");
                    BigInteger other=BigInteger::Parse("98765432109876543210");
                    BigInteger saved=source;BigInteger target=new BigInteger(777);
                    for(int operation=0;operation<6;operation++){
                        int live=allocations-releases;deny=true;
                        try{
                            if(operation==0)target=source;
                            if(operation==1)target=source.Add(&other);
                            if(operation==2)target=source.Subtract(&other);
                            if(operation==3)target=source.Multiply(&other);
                            if(operation==4)target=BigInteger::Parse("999999999999999999999");
                            if(operation==5){BigInteger failed=source;}
                            return 1;
                        }catch(ref BadAllocException error){}
                        deny=false;
                        if(allocations-releases!=live||!source.Equals(&saved)||!TextEquals(&target,"777"))return 2;
                    }
                    int renderingAllocations=0;
                    {int before=allocations;String rendered=source.ToString();renderingAllocations=allocations-before;}
                    for(int failure=0;failure<renderingAllocations;failure++){
                        int live=allocations-releases;failAfter=failure;
                        try{String failed=source.ToString();return 11;}catch(ref BadAllocException error){}
                        failAfter=-1;
                        if(allocations-releases!=live||!source.Equals(&saved))return 12;
                    }
                    Vector<BigInteger> values=new();values.Add(source);values.Add(move target);target=new();
                    int before=allocations;
                    BigInteger moved=move source;source=new();
                    if(allocations!=before)return 9;
                    source=new BigInteger(7);
                    if(!moved.Equals(&saved)||!TextEquals(&source,"7")||!TextEquals(&values[1],"777"))return 3;
                    values.Reserve(32);if(!values[0].Equals(&saved))return 4;
                    HashSet<BigInteger> set=new();
                    if(!set.Add(saved)||set.Add(BigInteger::Parse("+000123456789012345678901234567890")))return 5;
                    if(set.Count!=1||!set.Contains(&moved))return 6;
                    if(!set.Remove(&moved)||set.Count!=0)return 7;
                    values.Clear();set.Clear();
                    BigInteger factorial=new BigInteger(1);
                    for(long factor=2l;factor<=100l;factor++){
                        BigInteger operand=new BigInteger(factor);factorial=factorial.Multiply(&operand);
                    }
                    if(!TextEquals(&factorial,"FACTORIAL"))return 10;
                }
                if(allocations!=releases)return 8;return 0;
            }
            """.Replace("FACTORIAL",Enumerable.Range(1,100).Aggregate(Integer.One,(value,factor)=>value*factor)
                .ToString(CultureInfo.InvariantCulture)),optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StreamingWriterFailureLeavesSourceUnchangedAndCleansOwnedOutput(int optimization)
        =>Run(Allocator+"""
            struct Writer:ITextWriter{
                public String Text;public int Budget;public int Calls;
                public Writer(int budget){Text=new();Budget=budget;Calls=0;}
                public void Write(readonly(char)* data,nuint length){
                    if(Budget==0)throw new Exception("writer failed",47);
                    if(Budget>0)Budget--;Calls++;Text.Append(data,length);
                }
            }
            int main(){
                {
                    BigInteger value=BigInteger::Parse("-12345678901234567890123456789012345678901234567890");
                    BigInteger saved=value;int calls=0;
                    {Writer complete=new(-1);value.Format(&complete);calls=complete.Calls;
                     if(!TextEquals(&value,complete.Text.Data))return 1;}
                    for(int failure=0;failure<calls;failure++){
                        int live=allocations-releases;
                        {
                            Writer partial=new(failure);
                            try{value.Format(&partial);return 2;}catch(ref Exception error){if(error.code!=47)return 3;}
                            if(partial.Calls!=failure||!value.Equals(&saved))return 4;
                        }
                        if(allocations-releases!=live)return 5;
                    }
                    String interpolated=s$"value={value}";
                    if(interpolated.Length!=57)return 6;
                }
                if(allocations!=releases)return 7;return 0;
            }
            """,optimization);
}
