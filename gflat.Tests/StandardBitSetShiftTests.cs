using System.Collections;

namespace gflat.Tests;

public sealed class StandardBitSetShiftTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShiftsAndChainsMatchLogicalBitArrayOracleIncludingPaddingAndEnumeration(int optimization)
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "oracle.bin");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                var random = new Random(81329);
                int[] lengths = [0, 1, 2, 31, 32, 33, 63, 64, 65, 95, 96, 97, 127, 128, 129, 191, 192, 193];
                foreach (int length in lengths)
                {
                    uint[] counts = [0, 1, 31, 32, 33, (uint)Math.Max(0, length - 1), (uint)length, (uint)length + 1, uint.MaxValue];
                    foreach (int pattern in Enumerable.Range(0, 4))
                    foreach (uint count in counts)
                    foreach (bool left in new[] { false, true })
                    {
                        var initial = new BitArray(length);
                        for (int i = 0; i < length; i++)
                            initial[i] = pattern switch { 0 => false, 1 => true, 2 => random.Next(2) != 0, _ => i == 0 || i == length - 1 };
                        var expected = new BitArray(initial);
                        Shift(expected, count, left);
                        WriteCase(writer, initial, expected, count, left, reset: true);
                    }
                    // Carry the previous result into the next operation; do not reconstruct the source.
                    var chain = new BitArray(length);
                    for (int i = 0; i < length; i++) chain[i] = random.Next(2) != 0;
                    for (int step = 0; step < 36; step++)
                    {
                        var initial = new BitArray(chain);
                        uint count = step % 9 == 8 ? (uint)length : (uint)random.Next(0, 34);
                        bool left = random.Next(2) != 0;
                        Shift(chain, count, left);
                        WriteCase(writer, initial, chain, count, left, reset: step == 0);
                        if (step % 9 == 8)
                        {
                            // Re-seed after complete clearing so each chain still exercises live bits.
                            for (int i = 0; i < length; i++) chain[i] = random.Next(2) != 0;
                            WriteCase(writer, chain, chain, 0, left: true, reset: true);
                        }
                    }
                }
            }
            Run(directory, $$"""
                using std;
                extern void*? fopen(immutable(char*) path,immutable(char*) mode);
                extern nuint fread(void* output,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);
                extern int ferror(void* stream);
                extern int printf(immutable(char*) format,...);
                int main(){
                    void*? input=fopen({{Literal(path)}},"rb");if(input==null)return 1;
                    uint[19] row;BitSet bits=new();
                    while(true){
                        nuint bytes=fread((void*)&row[0],1,76,input!);if(bytes==0)break;if(bytes!=76)return 2;
                        nuint length=(nuint)row[0];
                        if(row[3]!=0u){
                            bits.Resize(length);bits.Clear();
                            for(nuint i=0;i<length;i++)bits[i]=(row[4+i/32]&((uint)1<<(int)(i%32)))!=0u;
                        }
                        nuint count=(nuint)row[2];
                        if(row[2]==(uint)4294967295)count=(nuint)0-(nuint)1;
                        if(row[1]!=0u)bits.ShiftLeft(count);else bits.ShiftRight(count);
                        if(bits.Length!=length||bits.CountSetBits()!=(nuint)row[18]){
                            printf("length %u left %u shift %u reset %u actual %u expected %u\n",row[0],row[1],row[2],row[3],(uint)bits.CountSetBits(),row[18]);return 3;}
                        for(nuint i=0;i<length;i++){
                            bool expected=(row[11+i/32]&((uint)1<<(int)(i%32)))!=0u;
                            if(bits[i]!=expected)return 4;
                        }
                        nuint seen=0;BitSet::Enumerator enumerator=bits.GetEnumerator();
                        while(enumerator.MoveNext()){
                            nuint index=enumerator.Current;
                            if(index>=length||(row[11+index/32]&((uint)1<<(int)(index%32)))==0u)return 5;
                            seen++;
                        }
                        if(seen!=(nuint)row[18])return 6;
                        // Reveals uncleared storage above a partial final word, including left shifts.
                        bits.Resize(length+31);
                        for(nuint i=length;i<bits.Length;i++)if(bits[i])return 7;
                        bits.Resize(length);
                    }
                    if(ferror(input!)!=0)return 8;return fclose(input!);
                }
                """, optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShiftingMovedOwnersIsAllocationFreeAndReleasesExactlyOnce(int optimization)
        => WithDirectory(directory => Run(directory, """
            using std;
            int allocations=0;int releases=0;bool deny=false;
            extern void*? malloc(nuint size);extern void free(void* storage);
            namespace Memory{
                public replace void*? Allocate(nuint size){if(deny)return null;allocations++;return malloc(size);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            int main(){
                {BitSet original=new(65);original[0]=true;original[32]=true;original[64]=true;
                 BitSet bits=move original;int before=allocations;int freed=releases;deny=true;
                 bits.ShiftLeft(32);
                 if(bits.Length!=65||bits.CountSetBits()!=2||!bits[32]||!bits[64])return 1;
                 bits.ShiftRight(31);
                 if(bits.CountSetBits()!=2||!bits[1]||!bits[33])return 2;
                 bits.ShiftLeft(0);bits.ShiftRight(0);
                 bits.ShiftRight((nuint)0-(nuint)1);
                 if(bits.Length!=65||bits.CountSetBits()!=0)return 3;
                 bits.SetAll(true);bits.ShiftLeft(65);if(bits.CountSetBits()!=0)return 4;
                 bits.SetAll(true);bits.ShiftRight(66);if(bits.CountSetBits()!=0)return 5;
                 BitSet empty=new();empty.ShiftLeft(0);empty.ShiftRight((nuint)0-(nuint)1);
                 if(empty.Length!=0||empty.CountSetBits()!=0)return 6;
                 if(allocations!=before||releases!=freed)return 7;
                 deny=false;original=new BitSet(1);original[0]=true;
                 if(!original[0]||bits.Length!=65||bits.CountSetBits()!=0)return 8;}
                return allocations-releases;
            }
            """, optimization));

    private static void Shift(BitArray bits, uint count, bool left)
    {
        var before = new BitArray(bits);
        // Model each logical index from an immutable snapshot, independently of
        // either implementation's packed-word shifting and padding behavior.
        for (int i = 0; i < bits.Length; i++)
        {
            long source = left ? (long)i - count : (long)i + count;
            bits[i] = source >= 0 && source < before.Length && before[(int)source];
        }
    }

    private static void WriteCase(BinaryWriter writer, BitArray initial, BitArray expected, uint count, bool left, bool reset)
    {
        writer.Write((uint)initial.Length);writer.Write(left ? 1u : 0u);writer.Write(count);writer.Write(reset ? 1u : 0u);
        WriteWords(writer, initial);WriteWords(writer, expected);
        writer.Write((uint)expected.Cast<bool>().Count(value => value));
    }

    private static void WriteWords(BinaryWriter writer, BitArray bits)
    {
        for (int word = 0; word < 7; word++)
        {
            uint value = 0;
            for (int bit = 0; bit < 32 && word * 32 + bit < bits.Length; bit++)
                if (bits[word * 32 + bit]) value |= 1u << bit;
            writer.Write(value);
        }
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_bit_shifts_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); } finally { Directory.Delete(directory, true); }
    }

    private static void Run(string directory, string program, int optimization)
    {
        var sources = new[] { "Collections/IEnumerable.gf", "Collections/Enumerable.gf", "Collections/BitSet.gf", "IEquatable.gf", "IHashable.gf", "Hashing.gf" }
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("shifts.gf", program));
        string ir = Path.Combine(directory, "shifts.ll"), executable = Path.Combine(directory, "shifts.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compilation.ExitCode == 0, compilation.StandardError);
        var result = NativeToolchain.Run(executable, []);
        Assert.True(result.ExitCode == 0, $"Native bit shifts exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
