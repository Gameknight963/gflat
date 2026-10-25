namespace gflat.Tests;

public sealed class StandardMath128Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactWideOverloadsMatchInt128WithoutFloatingPointRounding(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_math128_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "oracle.bin");
            Int128[] boundaries = [Int128.MinValue, Int128.MinValue + 1, -(Int128.One << 100),
                -((Int128.One << 64) + 1), -(Int128.One << 63), -((Int128.One << 53) + 1), -1, 0, 1,
                (Int128.One << 53) - 1, Int128.One << 53, (Int128.One << 53) + 1,
                (Int128.One << 63) - 1, Int128.One << 63, (Int128.One << 64) + 1,
                (Int128.One << 100) - 1, Int128.One << 100, (Int128.One << 100) + 1,
                Int128.MaxValue - 1, Int128.MaxValue];
            var random = new Random(128643);
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                foreach (Int128 a in boundaries)
                foreach (Int128 b in boundaries)
                {
                    WriteCase(writer, a, b, a, a, b);
                    WriteCase(writer, a, b, b, Int128.MinValue, Int128.MaxValue);
                    WriteCase(writer, a, b, Random128(random), a, a);
                }
                for (int i = 0; i < 256; i++)
                    WriteCase(writer, Random128(random), Random128(random), Random128(random), Random128(random), Random128(random));
            }
            var sources = new[]
            {
                new SourceFile("Math.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Numerics/Math.gf"))),
                new SourceFile("main.gf", $$"""
                    using std;
                    extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                    extern nuint fread(void* data,nuint size,nuint count,void* stream);
                    extern int ferror(void* stream);extern int fclose(void* stream);
                    extralong Wide(ulong low,ulong high) nothrow => ((extralong)high<<64)|(extralong)low;
                    int main(){
                        // These bindings require exact return AND argument signatures.
                        extralong(extralong,extralong)* minimum=&Math::Min;
                        extralong(extralong,extralong)* maximum=&Math::Max;
                        extralong(extralong,extralong,extralong)* clamp=&Math::Clamp;
                        extralong(extralong)* absolute=&Math::Abs;
                        extralong smallest=(extralong)1<<127;
                        void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                        ulong[18] halves;
                        while(true){
                            nuint read=fread((void*)&halves[0],1,144,file!);if(read==0)break;if(read!=144)return 2;
                            extralong a=Wide(halves[0],halves[1]);extralong b=Wide(halves[2],halves[3]);
                            extralong value=Wide(halves[4],halves[5]);
                            extralong low=Wide(halves[6],halves[7]);extralong high=Wide(halves[8],halves[9]);
                            extralong expectedMin=Wide(halves[10],halves[11]);
                            extralong expectedMax=Wide(halves[12],halves[13]);
                            extralong expectedClamp=Wide(halves[14],halves[15]);
                            extralong expectedAbs=Wide(halves[16],halves[17]);
                            if(Math::Min(a,b)!=expectedMin||Math::Max(a,b)!=expectedMax)return 3;
                            if(minimum(a,b)!=expectedMin||maximum(a,b)!=expectedMax)return 4;
                            if(low>high){
                                try{Math::Clamp(value,low,high);return 5;}catch(ref Exception error){}
                                try{clamp(value,low,high);return 6;}catch(ref Exception error){}
                            }else{
                                if(Math::Clamp(value,low,high)!=expectedClamp||clamp(value,low,high)!=expectedClamp)return 7;
                            }
                            if(a==smallest){
                                try{Math::Abs(a);return 8;}catch(ref Exception error){}
                                try{absolute(a);return 9;}catch(ref Exception error){}
                            }else if(Math::Abs(a)!=expectedAbs||absolute(a)!=expectedAbs)return 10;
                        }
                        if(ferror(file!)!=0)return 11;return fclose(file!);
                    }
                    """)
            };
            string ir = Path.Combine(directory, "math.ll"), executable = Path.Combine(directory, "math.exe");
            string llvm = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
            // Wide comparison, branching, and negation do not need floating conversion/division helpers.
            Assert.False(NativeToolchain.RequiresWideIntegerRuntime(llvm));
            File.WriteAllText(ir, llvm);
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native Math128 exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Int128 Random128(Random random)
    {
        byte[] bytes = new byte[16];random.NextBytes(bytes);
        UInt128 bits = ((UInt128)BitConverter.ToUInt64(bytes, 8) << 64) | BitConverter.ToUInt64(bytes, 0);
        return unchecked((Int128)bits);
    }

    private static void WriteCase(BinaryWriter writer, Int128 a, Int128 b, Int128 value, Int128 low, Int128 high)
    {
        foreach (Int128 item in new[] { a, b, value, low, high, Int128.Min(a, b), Int128.Max(a, b),
            low > high ? 0 : Int128.Clamp(value, low, high), a == Int128.MinValue ? 0 : Int128.Abs(a) })
        {
            UInt128 bits = unchecked((UInt128)item);
            writer.Write(unchecked((ulong)bits));writer.Write((ulong)(bits >> 64));
        }
    }
    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
