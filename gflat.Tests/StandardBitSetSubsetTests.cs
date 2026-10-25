using System.Collections;

namespace gflat.Tests;

public sealed class StandardBitSetSubsetTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("bitset-subset.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static bool Subset(BitArray left, BitArray right)
    {
        for (int i = 0; i < left.Length; i++)
            if (left[i] && !right[i]) return false;
        return true;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PackedRelationsMatchIndependentLogicalBitOracleWithoutAllocationOrMutation(int optimization)
    {
        var random = new Random(527641);
        var cases = new List<(BitArray Left, BitArray Right)>();
        foreach (int length in new[] { 0, 1, 31, 32, 33, 63, 64, 65, 97, 129 })
        for (int mode = 0; mode < 6; mode++)
        {
            var left = new BitArray(length); var right = new BitArray(length);
            for (int i = 0; i < length; i++)
            {
                left[i] = mode switch { 0 => false, 1 => true, 2 => i % 3 == 0,
                    3 => i % 2 == 0, 4 => i == 0 || i + 1 == length, _ => random.Next(2) != 0 };
                right[i] = mode switch { 0 => false, 1 => true, 2 => left[i] || i % 5 == 0,
                    3 => i % 2 != 0, 4 => i + 1 == length, _ => random.Next(2) != 0 };
            }
            cases.Add((left, right));
        }
        for (int sample = 0; sample < 24; sample++)
        {
            int length = random.Next(130); var left = new BitArray(length); var right = new BitArray(length);
            for (int i = 0; i < length; i++) { left[i] = random.Next(2) != 0; right[i] = random.Next(2) != 0; }
            cases.Add((left, right));
        }
        using var packet = new MemoryStream();
        foreach (var pair in cases)
        {
            packet.WriteByte((byte)pair.Left.Length); packet.WriteByte((byte)(pair.Left.Length >> 8));
            packet.WriteByte((byte)((Subset(pair.Left, pair.Right) ? 1 : 0) | (Subset(pair.Right, pair.Left) ? 2 : 0)));
            for (int i = 0; i < pair.Left.Length; i++) packet.WriteByte(pair.Left[i] ? (byte)1 : (byte)0);
            for (int i = 0; i < pair.Right.Length; i++) packet.WriteByte(pair.Right[i] ? (byte)1 : (byte)0);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Memory + $$$"""
            nuint ReadByte(readonly(char)* text, ref nuint offset) {
                nuint value=0;
                for(int i=0;i<2;i++) {
                    char digit=text[offset];offset++;
                    nuint part=(nuint)((int)digit-(int)'0');
                    if(digit>='A')part=(nuint)((int)digit-(int)'A'+10);
                    value=value*16+part;
                }
                return value;
            }
            int main() {
                readonly(char)* packet="{{{hex}}}";nuint offset=0;
                for(int row=0;row<{{{cases.Count}}};row++) {
                    nuint length=ReadByte(packet,ref offset);length+=ReadByte(packet,ref offset)*256;
                    nuint flags=ReadByte(packet,ref offset);nuint start=offset;
                    {
                        BitSet left=new(length);BitSet right=new(length);
                        for(nuint i=0;i<length;i++)left[i]=ReadByte(packet,ref offset)!=0;
                        for(nuint i=0;i<length;i++)right[i]=ReadByte(packet,ref offset)!=0;
                        immutable(BitSet)* deep=&left;readonly(BitSet)* shallow=&left;
                        int before=attempts;deny=true;
                        bool subset=(flags&1)!=0;bool superset=(flags&2)!=0;
                        if(left.IsSubsetOf(&right)!=subset||left.IsSupersetOf(&right)!=superset)return 1;
                        if(right.IsSubsetOf(&left)!=superset||right.IsSupersetOf(&left)!=subset)return 2;
                        if(deep.IsSubsetOf(&right)!=subset||shallow.IsSupersetOf(&right)!=superset)return 3;
                        if(!left.IsSubsetOf(&left)||!left.IsSupersetOf(&left)||!right.IsSubsetOf(&right))return 4;
                        if(attempts!=before||left.Length!=length||right.Length!=length)return 5;
                        nuint verify=start;
                        for(nuint i=0;i<length;i++)if(left[i]!=(ReadByte(packet,ref verify)!=0))return 6;
                        for(nuint i=0;i<length;i++)if(right[i]!=(ReadByte(packet,ref verify)!=0))return 7;
                        if(verify!=offset)return 8;
                        deny=false;
                    }
                }
                if(offset!={{{hex.Length}}}||allocated!=released)return 9;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TailHistoriesAndDifferentDomainsPreserveTheirExactContracts(int optimization)
        => Run(Memory + """
            int main() {
                {
                    BitSet left=new(97);BitSet right=new(97);left.SetAll(true);right.SetAll(true);
                    nuint[9] lengths=[97,33,65,1,64,31,0,129,32];
                    for(nuint step=0;step<9;step++) {
                        nuint length=lengths[step];left.Resize(length);right.Resize(length);
                        left.ShiftLeft(1);right.ShiftRight(1);
                        if(length!=0)left[length-1]=true;
                        BitSet freshLeft=new(length);BitSet freshRight=new(length);
                        bool subset=true;bool superset=true;
                        for(nuint i=0;i<length;i++) {
                            bool a=left[i];bool b=right[i];freshLeft[i]=a;freshRight[i]=b;
                            if(a&&!b)subset=false;if(b&&!a)superset=false;
                        }
                        int before=attempts;deny=true;
                        if(left.IsSubsetOf(&right)!=subset||left.IsSupersetOf(&right)!=superset)return 1;
                        if(!left.IsSubsetOf(&freshLeft)||!left.IsSupersetOf(&freshLeft))return 2;
                        if(!right.IsSubsetOf(&freshRight)||!right.IsSupersetOf(&freshRight))return 3;
                        if(attempts!=before)return 4;
                        deny=false;
                        for(nuint i=0;i<length;i++)if(left[i]!=freshLeft[i]||right[i]!=freshRight[i])return 5;
                    }
                }
                {
                    BitSet left=new(65);BitSet right=new(64);left.SetAll(true);right.SetAll(true);
                    nuint leftHash=left.GetHashCode();nuint rightHash=right.GetHashCode();
                    for(int method=0;method<2;method++) {
                        bool result=true;bool caught=false;
                        try {
                            if(method==0)result=left.IsSubsetOf(&right);
                            else result=left.IsSupersetOf(&right);
                        }
                        catch(ref Exception error) {
                            if(!CString::Equals(error.GetMessage(),"BitSet lengths differ"))return 6;
                            caught=true;
                        }
                        if(!caught||!result||left.Length!=65||right.Length!=64)return 7;
                        if(left.GetHashCode()!=leftHash||right.GetHashCode()!=rightHash)return 8;
                    }
                    BitSet empty=new();bool caught=false;
                    try{empty.IsSubsetOf(&right);}catch(ref Exception error){caught=true;}
                    if(!caught||empty.Length!=0||right.Length!=64)return 9;
                }
                if(allocated!=released)return 10;
                return 0;
            }
            """, optimization);

    private const string Memory = """
        using std;
        bool deny=false;int attempts=0;int allocated=0;int released=0;
        extern void*? malloc(nuint count);extern void free(void* value);
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;if(deny)return null;
                void*? value=malloc(count);if(value!=null)allocated++;return value;
            }
            public replace void Free(void* value)nothrow{released++;free(value);}
        }
        """;
}
