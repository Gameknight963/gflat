using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardBinary128Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Signed128WireValuesMatchDotNetAtUnalignedAddressesAndAliasedOutputs(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_binary128_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "oracle.bin");
            var values = new HashSet<Int128> { 0, 1, -1, Int128.MinValue, Int128.MaxValue };
            for (int bit = 0; bit < 128; bit++)
            {
                Int128 value = Int128.One << bit;
                values.Add(value);values.Add(~value);
                values.Add(unchecked(value - 1));values.Add(unchecked(value + 1));
            }
            var random = new Random(1612864);
            for (int i = 0; i < 256; i++)
            {
                byte[] bytes = new byte[16];random.NextBytes(bytes);
                values.Add(BinaryPrimitives.ReadInt128LittleEndian(bytes));
            }
            using (var writer = new BinaryWriter(File.Create(path)))
                foreach (Int128 value in values)
                {
                    byte[] bytes = new byte[32];
                    BinaryPrimitives.WriteInt128LittleEndian(bytes, value);
                    BinaryPrimitives.WriteInt128BigEndian(bytes.AsSpan(16), value);
                    writer.Write(bytes);
                }
            string program = $$"""
                using std;
                extern void*? fopen(readonly(char)* path,readonly(char)* mode) nothrow;
                extern nuint fread(void* data,nuint size,nuint count,void* stream) nothrow;
                extern int fclose(void* stream) nothrow;extern int ferror(void* stream) nothrow;
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    byte[32] row;byte[64] output;byte[64] before;extralong[4] aligned;
                    byte* raw=(byte*)&aligned[0];
                    while(true){
                        nuint read=fread((void*)&row[0],1,32,file!);if(read==0)break;if(read!=32)return 2;
                        extralong expected=(extralong)0;
                        for(int i=15;i>=0;i--)expected=(expected<<8)|(extralong)row[i];
                        for(int order=0;order<2;order++){
                            byte* wire=&row[order*16];
                            for(nuint offset=0;offset<=32;offset++){
                                for(nuint i=0;i<64;i++)output[i]=(byte)90;
                                bool written=false;
                                if(order==0)written=BinaryPrimitives::TryWriteExtraLongLittleEndian(&output[offset],64-offset,expected);
                                else written=BinaryPrimitives::TryWriteExtraLongBigEndian(&output[offset],64-offset,expected);
                                if(!written)return 3;
                                for(nuint i=0;i<64;i++){
                                    if(i>=offset&&i-offset<16){if(output[i]!=wire[i-offset])return 4;}
                                    else if(output[i]!=(byte)90)return 5;
                                }
                                extralong actual=(extralong)99;bool success=false;
                                if(order==0)success=BinaryPrimitives::TryReadExtraLongLittleEndian(&output[offset],64-offset,ref actual);
                                else success=BinaryPrimitives::TryReadExtraLongBigEndian(&output[offset],64-offset,ref actual);
                                if(!success||actual!=expected)return 6;
                                for(nuint count=0;count<16;count++){
                                    for(nuint i=0;i<64;i++)output[i]=(byte)90;
                                    if(order==0)written=BinaryPrimitives::TryWriteExtraLongLittleEndian(&output[offset],count,expected);
                                    else written=BinaryPrimitives::TryWriteExtraLongBigEndian(&output[offset],count,expected);
                                    if(written)return 7;
                                    for(nuint i=0;i<64;i++)if(output[i]!=(byte)90)return 8;
                                }
                                // Outputs aligned by actual extralong storage; source may start at any byte.
                                for(nuint slot=1;slot<=2;slot++){
                                    for(nuint i=0;i<64;i++)raw[i]=(byte)90;
                                    for(nuint i=0;i<16;i++)raw[offset+i]=wire[i];
                                    for(nuint i=0;i<64;i++)before[i]=raw[i];
                                    if(order==0)success=BinaryPrimitives::TryReadExtraLongLittleEndian(raw+offset,16,ref aligned[slot]);
                                    else success=BinaryPrimitives::TryReadExtraLongBigEndian(raw+offset,16,ref aligned[slot]);
                                    if(!success||aligned[slot]!=expected)return 9;
                                    for(nuint i=0;i<64;i++)if((i<slot*16||i>=slot*16+16)&&raw[i]!=before[i])return 10;
                                }
                            }
                        }
                    }
                    if(ferror(file!)!=0)return 11;return fclose(file!);
                }
                """;
            Run(directory, program, optimization);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShortBuffersPreflightWithoutDereferencingAndAliasedFailuresOnlyZeroOutput(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_binary128_short_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Run(directory, """
                using std;
                int main(){
                    readonly(byte)* unreadable=(readonly(byte)*)1;byte* unwritable=(byte*)1;
                    extralong value=(extralong)99;extralong[4] aligned;byte* raw=(byte*)&aligned[0];
                    for(nuint count=0;count<16;count++){
                        value=(extralong)99;
                        if(BinaryPrimitives::TryReadExtraLongLittleEndian(unreadable,count,ref value)||value!=(extralong)0)return 1;
                        value=(extralong)99;
                        if(BinaryPrimitives::TryReadExtraLongBigEndian(unreadable,count,ref value)||value!=(extralong)0)return 2;
                        if(BinaryPrimitives::TryWriteExtraLongLittleEndian(unwritable,count,value))return 3;
                        if(BinaryPrimitives::TryWriteExtraLongBigEndian(unwritable,count,value))return 4;
                        for(nuint i=0;i<64;i++)raw[i]=(byte)90;
                        if(BinaryPrimitives::TryReadExtraLongLittleEndian(raw+9,count,ref aligned[1]))return 5;
                        for(nuint i=0;i<64;i++){
                            if(i>=16&&i<32){if(raw[i]!=(byte)0)return 6;}
                            else if(raw[i]!=(byte)90)return 7;
                        }
                        for(nuint i=0;i<64;i++)raw[i]=(byte)90;
                        if(BinaryPrimitives::TryReadExtraLongBigEndian(raw+25,count,ref aligned[2]))return 8;
                        for(nuint i=0;i<64;i++){
                            if(i>=32&&i<48){if(raw[i]!=(byte)0)return 9;}
                            else if(raw[i]!=(byte)90)return 10;
                        }
                    }
                    byte[16] data;
                    value=(extralong)1<<127;
                    if(!BinaryPrimitives::TryWriteExtraLongBigEndian(data,16,value))return 11;
                    if(!BinaryPrimitives::TryReadExtraLongBigEndian(data,16,ref value)||value!=((extralong)1<<127))return 12;
                    return 0;
                }
                """, optimization);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Run(string directory, string program, int optimization)
    {
        var sources = new[]
        {
            new SourceFile("BinaryPrimitives.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Binary/BinaryPrimitives.gf"))),
            new SourceFile("main.gf", program)
        };
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        var methods = Regex.Matches(ir, @"(?ms)^define [^\r\n]*\$BinaryPrimitives\$Try(Read|Write)ExtraLong[^\r\n]*\{\r?\n.*?^\}");
        Assert.Equal(4, methods.Count);
        foreach (Match method in methods)
        {
            Assert.Contains("nounwind", method.Value.Split('\n')[0]);
            Assert.DoesNotContain("invoke", method.Value);
        }
        Assert.DoesNotContain("__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        Assert.DoesNotContain("$Memory$Allocate", ir);
        Assert.DoesNotContain("fptosi", ir);
        Assert.DoesNotContain("sitofp", ir);
        Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));
        string source = Path.Combine(directory, "binary.ll"), executable = Path.Combine(directory, "binary.exe");
        File.WriteAllText(source, ir);
        var compiled = NativeToolchain.Compile(source, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(executable, [], 30000);
        Assert.True(result.ExitCode == 0, $"Native binary128 exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
