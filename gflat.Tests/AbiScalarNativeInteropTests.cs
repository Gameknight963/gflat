namespace gflat.Tests;

public sealed class AbiScalarNativeInteropTests
{
    private const string Native = """
        _Bool native_bool(_Bool value){return value;}
        signed char native_sbyte(signed char value){return value;}
        unsigned char native_byte(unsigned char value){return value;}
        short native_short(short value){return value;}
        unsigned short native_ushort(unsigned short value){return value;}
        int native_promote(signed char value){return value;}
        int native_invoke(int(*callback)(signed char)){return callback(-123);}
        int native_invoke_bool(int(*callback)(_Bool)){return callback(1);}
        extern _Bool echo_bool(_Bool value) __asm__("gflat$EchoBool");
        extern signed char echo_sbyte(signed char value) __asm__("gflat$EchoSbyte");
        extern unsigned char echo_byte(unsigned char value) __asm__("gflat$EchoByte");
        extern short echo_short(short value) __asm__("gflat$EchoShort");
        extern unsigned short echo_ushort(unsigned short value) __asm__("gflat$EchoUShort");
        int native_check_gflat(void){
            if(!echo_bool(1)||echo_bool(0))return 10;
            if(echo_sbyte(-123)!=-123)return 11;
            if(echo_byte(251)!=251)return 12;
            if(echo_short(-31234)!=-31234)return 13;
            if(echo_ushort(65123)!=65123)return 14;
            return 0;
        }
        """;

    private const string Source = """
        extern bool native_bool(bool value);
        extern sbyte native_sbyte(sbyte value);
        extern byte native_byte(byte value);
        extern short native_short(short value);
        extern ushort native_ushort(ushort value);
        extern int native_promote(sbyte value);
        extern int native_invoke(int(sbyte)* callback);
        extern int native_invoke_bool(int(bool)* callback);
        extern int native_check_gflat();
        bool EchoBool(bool value)=>value;
        sbyte EchoSbyte(sbyte value)=>value;
        byte EchoByte(byte value)=>value;
        short EchoShort(short value)=>value;
        ushort EchoUShort(ushort value)=>value;
        int Promote(sbyte value)=>(int)value;
        int BoolAsInt(bool value){if(value)return 1;return 0;}
        int Invoke(int(sbyte)* callback)=>callback((sbyte)-123);
        int main(){
            if(!native_bool(true)||native_bool(false))return 1;
            if(native_sbyte((sbyte)-123)!=(sbyte)-123)return 2;
            if(native_byte((byte)251)!=(byte)251)return 3;
            if(native_short((short)-31234)!=(short)-31234)return 4;
            if(native_ushort((ushort)65123)!=(ushort)65123)return 5;
            if(native_promote((sbyte)-123)!=-123)return 6;
            if(Invoke(native_promote)!=-123)return 7;
            if(native_invoke(Promote)!=-123)return 8;
            int(sbyte)* callback=static(sbyte value)=>(int)value;
            if(native_invoke(callback)!=-123)return 9;
            if(native_invoke_bool(BoolAsInt)!=1)return 15;
            return native_check_gflat();
        }
        """;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 2)]
    public void CAndGflatAgreeForDirectCallsCallbacksAndScalarResults(int callerOptimization, int calleeOptimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-scalar-abi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string native = Path.Combine(directory, "helper.c"), ir = Path.Combine(directory, "program.ll");
            string gflatObject = Path.Combine(directory, "program.o"), nativeObject = Path.Combine(directory, "helper.o");
            string linkUnit = Path.Combine(directory, "link.cpp"), executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "program.exe" : "program");
            File.WriteAllText(native, Native);
            File.WriteAllText(ir, Compiler.Emit(Source, options: new() { NoStdLib = true }));
            File.WriteAllText(linkUnit, "// Link separately compiled C and gflat objects using the existing runtime.\n");
            string clang = NativeToolchain.FindClang();
            var c = NativeToolchain.Run(clang, ["-x", "c", "-c", "-O" + calleeOptimization, native, "-o", nativeObject]);
            Assert.True(c.ExitCode == 0, c.StandardError);
            var gflat = NativeToolchain.Run(clang, ["-c", "-O" + callerOptimization, ir, "-o", gflatObject]);
            Assert.True(gflat.ExitCode == 0, gflat.StandardError);
            var link = NativeToolchain.CompileCpp(linkUnit, executable, [gflatObject, nativeObject]);
            Assert.True(link.ExitCode == 0, link.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Gflat O{callerOptimization}, C O{calleeOptimization}: exit {result.ExitCode}\n{result.StandardError}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch { }
        }
    }
}
