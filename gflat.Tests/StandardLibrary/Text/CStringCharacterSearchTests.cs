using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class CStringCharacterSearchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryNeedleMatchesHostSearchOfThePrefixBeforeNull(int optimization)
    {
        List<byte[]> cases = [[], [0], [61], [61, 61, 61], "mode=fast=checked"u8.ToArray(),
            [255, 128, 255, 0, 128, 255], [0, 61, 255], [1, 2, 3, 0, 3, 2, 1],
            Enumerable.Range(1, 255).Select(value => (byte)value).ToArray(),
            Enumerable.Range(1, 255).Reverse().Select(value => (byte)value).ToArray()];
        var random = new Random(52719);
        for (int trial = 0; trial < 12; trial++)
        {
            byte[] bytes = new byte[random.Next(1, 65)];
            random.NextBytes(bytes);
            if (trial % 2 == 0) bytes[random.Next(bytes.Length)] = 0;
            cases.Add(bytes);
        }
        var code = new StringBuilder("using std::Text;int main(){char[260] storage;\n");
        foreach (byte[] bytes in cases)
        {
            int zero = Array.IndexOf(bytes, (byte)0);
            byte[] content = zero < 0 ? bytes : bytes[..zero];
            int[] first = Enumerable.Range(0, 256).Select(value => Array.IndexOf(content, (byte)value)).ToArray();
            int[] last = Enumerable.Range(0, 256).Select(value => Array.LastIndexOf(content, (byte)value)).ToArray();
            code.AppendLine($"{{storage[0]='!';storage[{bytes.Length + 1}]='\\0';storage[{bytes.Length + 2}]='?';");
            for (int i = 0; i < bytes.Length; i++) code.AppendLine($"storage[{i + 1}]=(char){bytes[i]};");
            code.AppendLine($"int[256] first=[{string.Join(',', first)}];int[256] last=[{string.Join(',', last)}];");
            code.AppendLine("""
                for(int value=0;value<256;value++){
                    nuint expectedFirst=CString::NotFound; nuint expectedLast=CString::NotFound;
                    if(first[value]>=0)expectedFirst=(nuint)first[value];
                    if(last[value]>=0)expectedLast=(nuint)last[value];
                    if(CString::IndexOf(&storage[1],(char)value)!=expectedFirst)return 1;
                    if(CString::LastIndexOf(&storage[1],(char)value)!=expectedLast)return 2;
                }
                """);
            for (int i = 0; i < bytes.Length; i++) code.AppendLine($"if(storage[{i + 1}]!=(char){bytes[i]})return 3;");
            code.AppendLine($"if(storage[0]!='!'||storage[{bytes.Length + 1}]!='\\0'||storage[{bytes.Length + 2}]!='?')return 4;}}");
        }
        code.AppendLine("if(CString::NotFound!=(nuint)0-(nuint)1)return 5;return 0;}");
        Run(code.ToString(), optimization, minimal: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedArgumentOffsetsNeedNoAllocationsAndWorkInNothrowCode(int optimization)
        => Run("""
            using std::Text;
            int allocations=0;int releases=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* value) nothrow{releases++;}
            }
            int Check(readonly(char)* argument) nothrow{
                nuint first=CString::IndexOf(argument,'=');
                nuint last=CString::LastIndexOf(argument,'=');
                if(first!=4||last!=9)return 1;
                if(!CString::Equals(argument+first+1,"fast=checked"))return 2;
                if(!CString::Equals(argument+last+1,"checked"))return 3;
                if(CString::IndexOf(argument,'\0')!=CString::NotFound||CString::LastIndexOf(argument,'\0')!=CString::NotFound)return 4;
                if(CString::IndexOf("no-delimiter",'=')!=CString::NotFound||CString::LastIndexOf("no-delimiter",'=')!=CString::NotFound)return 5;
                return 0;
            }
            int main(){
                char[18] argument=['m','o','d','e','=','f','a','s','t','=','c','h','e','c','k','e','d','\0'];
                int result=Check(argument);if(result!=0)return result;
                if(allocations!=0||releases!=0)return 6;return 0;
            }
            """, optimization, minimal: false);

    private static void Run(string program, int optimization, bool minimal)
    {
        SourceFile[] sources = [
            new("CString.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/CString.gf"))),
            new("character-search.gf", program)
        ];
        if (!minimal)
        {
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
            return;
        }
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "@__gflat_eh_", "gflat$Memory$", "@malloc", "@free", "@strlen", "@strchr", "@strrchr" })
            Assert.DoesNotContain(forbidden, ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-cstring-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "main.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
