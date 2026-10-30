using System.Buffers.Binary;
using System.Globalization;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerUnsignedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullUnsignedRangeMatchesHostDecimalArithmeticAndPrimitiveConversions(int optimization)
    {
        ulong[] values = Values();
        string Texts(Func<Integer, Integer> transform) => string.Join(",", values.Select(value =>
            "\"" + transform(new Integer(value)).ToString(CultureInfo.InvariantCulture) + "\""));
        string inputs = string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture) + "ul"));
        Run(Counters + $$"""
            int main(){
                ulong[{{values.Length}}] inputs=[{{inputs}}];
                readonly(char)*[{{values.Length}}] decimals=[{{Texts(value => value)}}];
                readonly(char)*[{{values.Length}}] sums=[{{Texts(value => value + 17)}}];
                readonly(char)*[{{values.Length}}] products=[{{Texts(value => value * 17)}}];
                for(nuint row=0;row<{{values.Length}};row++){
                    {
                        ulong original=inputs[row];BigInteger value=BigInteger::FromULong(original);
                        immutable(BigInteger)* deep=&value;readonly(BigInteger)* shallow=&value;
                        int expectedSign=1;if(original==0ul)expectedSign=0;
                        if(deep.Sign!=expectedSign||deep.ToULong()!=original)return 1;
                        ulong unsignedValue=73ul;
                        if(!shallow.TryToULong(ref unsignedValue)||unsignedValue!=original)return 2;
                        long signedValue=73l;bool fits=deep.TryToLong(ref signedValue);
                        if(original<=9223372036854775807ul){if(!fits||signedValue!=(long)original)return 3;}
                        else if(fits||signedValue!=0l)return 4;
                        String text=value.ToString();
                        if(!CString::Equals(text.Data,decimals[row]))return 5;
                        BigInteger increment=new(17);BigInteger sum=value.Add(&increment);BigInteger product=value.Multiply(&increment);
                        String sumText=sum.ToString();String productText=product.ToString();
                        if(!CString::Equals(sumText.Data,sums[row])||!CString::Equals(productText.Data,products[row]))return 6;
                        if(inputs[row]!=original||value.ToULong()!=original)return 7;
                        BigInteger copy=value;BigInteger moved=move copy;copy=new();value=new(-19);
                        if(moved.ToULong()!=original||moved.Sign!=expectedSign||copy.Sign!=0)return 8;
                    }
                    if(allocated!=released)return 9;
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroNeedsNoAllocationAndFailedConstructionPreservesExistingOwners(int optimization)
        => Run(Counters + """
            int main(){
                int before=attempts;deny=true;
                {
                    BigInteger zero=BigInteger::FromULong(0ul);ulong result=99ul;
                    if(zero.Sign!=0||!zero.TryToULong(ref result)||result!=0ul||attempts!=before)return 1;
                }
                deny=false;if(allocated!=released)return 2;
                {
                    ulong input=18446744073709551615ul;
                    BigInteger value=BigInteger::FromULong(input);BigInteger saved=value;
                    BigInteger copied=value;BigInteger moved=move copied;copied=new();
                    value=new(17);
                    if(saved.ToULong()!=input||moved.ToULong()!=input||value.ToLong()!=17l)return 3;
                    value=new(19);if(saved.ToULong()!=input||moved.ToULong()!=input)return 4;
                    captured=null;capture=true;BigInteger target=new(777);capture=false;void*? storage=captured;
                    if(storage==null)return 5;
                    int start=allocated;int points=0;
                    {
                        BigInteger measured=BigInteger::FromULong(input);points=allocated-start;
                        if(points==0||measured.ToULong()!=input)return 6;
                    }
                    for(int point=0;point<points;point++){
                        int balance=allocated-released;int deniedBefore=denied;
                        watched=storage;watchedFrees=0;failAfter=point;bool caught=false;
                        try{target=BigInteger::FromULong(input);return 7;}
                        catch(ref BadAllocException error){caught=true;}
                        failAfter=-1;
                        if(!caught||denied==deniedBefore||allocated-released!=balance||watchedFrees!=0)return 8;
                        if(input!=18446744073709551615ul||target.ToLong()!=777l||value.ToLong()!=19l
                            ||saved.ToULong()!=input||moved.ToULong()!=input)return 9;
                        watched=null;
                    }
                    target=BigInteger::FromULong(input);
                    if(target.ToULong()!=input||saved.ToULong()!=input||moved.ToULong()!=input)return 10;
                }
                if(allocated!=released)return 11;return 0;
            }
            """, optimization);

    private static ulong[] Values()
    {
        var values = new SortedSet<ulong> { 0, 1, 2, uint.MaxValue, (ulong)uint.MaxValue + 1,
            long.MaxValue, (ulong)long.MaxValue + 1, ulong.MaxValue - 1, ulong.MaxValue };
        foreach (ulong radixPower in new[] { 1_000_000_000ul, 1_000_000_000_000_000_000ul })
            foreach (ulong delta in new[] { 0ul, 1ul, 2ul })
            { values.Add(radixPower - delta); values.Add(radixPower + delta); }
        var random = new Random(642173);
        byte[] bytes = new byte[8];
        for (int sample = 0; sample < 64; sample++)
        { random.NextBytes(bytes); values.Add(BinaryPrimitives.ReadUInt64LittleEndian(bytes)); }
        return values.ToArray();
    }

    private const string Counters = """
        using Standard;using Standard::Text;using Standard::Numerics;
        extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        bool capture=false;void*? captured=null;void*? watched=null;int watchedFrees=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? storage=malloc(count);if(storage!=null){allocated++;if(capture)captured=storage;}return storage;
            }
            public replace void Free(void* storage)nothrow{
                if(storage==watched)watchedFrees++;released++;free(storage);
            }
        }
        """;

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("bigint-unsigned.gf", program));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
