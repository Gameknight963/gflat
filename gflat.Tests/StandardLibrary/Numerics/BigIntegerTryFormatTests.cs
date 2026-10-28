using System.Globalization;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerTryFormatTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ZeroSignsAndDecimalLimbBoundariesAreAtomicAtEveryCapacity(int optimization)
    {
        var values = new List<Integer> { 0, 1, -1, long.MinValue, long.MaxValue };
        foreach (int digits in new[] { 1, 2, 8, 9, 10, 18, 19, 27, 28, 63, 64, 699, 700 })
        {
            Integer power = Integer.Pow(10, digits - 1);
            foreach (Integer value in new[] { power - 1, power, power + 1 })
            { values.Add(value); values.Add(-value); }
        }
        RunRows(values, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RandomLargeValuesFormatWithoutAllocationOrReceiverMutation(int optimization)
    {
        var random = new Random(571991);
        var values = new List<Integer>();
        for (int row = 0; row < 48; row++)
        {
            int digits = row == 0 ? 700 : random.Next(1, 701);
            char[] text = new char[digits];
            text[0] = (char)('1' + random.Next(9));
            for (int i = 1; i < digits; i++) text[i] = (char)('0' + random.Next(10));
            Integer value = Integer.Parse(new string(text), CultureInfo.InvariantCulture);
            values.Add(row % 2 == 0 ? value : -value);
        }
        RunRows(values, optimization);
    }

    private static void RunRows(IEnumerable<Integer> values, int optimization)
    {
        string[] rows = values.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();
        string data = string.Join('|', rows) + "|";
        string program = $$"""
            using std; using std::Numerics;
            extern void*? malloc(nuint count); extern void free(void* value);
            int attempts=0; int allocated=0; int released=0; bool deny=false;
            namespace Memory {
                public replace void*? Allocate(nuint count){
                    attempts++; if(deny)return null; void*? value=malloc(count);
                    if(value!=null)allocated++; return value;
                }
                public replace void Free(void* value)nothrow{released++;free(value);}
            }
            bool Probe(immutable(BigInteger)* value,char* output,nuint capacity,ref nuint written)nothrow
                =>value.TryFormat(output,capacity,ref written);
            int main(){
                {
                    readonly(char)* data="{{data}}"; nuint offset=0;
                    for(int row=0;row<{{rows.Length}};row++){
                        nuint count=0;while(data[offset+count]!='|')count++;
                        BigInteger value=BigInteger::Parse(data+offset,count);BigInteger saved=value;
                        immutable(BigInteger)* deep=&value;readonly(BigInteger)* shallow=&value;
                        nuint hash=value.GetHashCode();int before=attempts;int live=allocated-released;deny=true;
                        nuint written=123;char*? nowhere=null;
                        if(Probe(deep,nowhere!,0,ref written)||written!=0)return 1;
                        char[704] bytes;
                        for(nuint capacity=0;capacity<=count+1;capacity++){
                            for(nuint i=0;i<704;i++)bytes[i]='#';
                            written=123;bool okay=Probe(deep,&bytes[1],capacity,ref written);
                            nuint expectedWritten=0;if(okay)expectedWritten=count;
                            if(okay!=(capacity>=count)||written!=expectedWritten)return 2;
                            for(nuint i=0;i<704;i++){
                                char expected='#';
                                if(okay&&i>=1&&i<=count)expected=data[offset+i-1];
                                if(bytes[i]!=expected)return 3;
                            }
                            // Retry immediately at exact capacity, including after every short failure.
                            written=123;
                            if(!shallow.TryFormat(&bytes[1],count,ref written)||written!=count)return 4;
                            for(nuint i=0;i<count;i++)if(bytes[i+1]!=data[offset+i])return 5;
                        }
                        if(attempts!=before||allocated-released!=live||!value.Equals(&saved)||value.GetHashCode()!=hash)return 6;
                        deny=false;offset+=count+1;
                    }
                    if(offset!={{data.Length}})return 7;
                }
                if(allocated!=released)return 8;return 0;
            }
            """;
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("bigint-try-format.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"BigInteger TryFormat exit {result.ExitCode}: {result.StandardError}");
    }
}
