using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class LlvmModuleNoUnwindTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SelectedValueAndVoidEntriesWithExplicitNoUnwindNeedNoExceptionBoundary(int optimization)
    {
        string ir = Emit("""
            int Value(int value)nothrow=>__llvm_module<int>("define i32 @entry(i32 %value) alwaysinline nounwind {\n%result = add i32 %value, 1\nret i32 %result\n}","entry",value);
            void Store(int* target,int value)nothrow=>__llvm_module<void>("define void @entry(i32* %target, i32 %value) nounwind alwaysinline {\nstore i32 %value, i32* %target\nret void\n}","entry",target,value);
            int main()nothrow{
                int value=0;Store(&value,Value(41));if(value!=42)return 1;return 0;
            }
            """);
        foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "gflat$Memory$" })
            Assert.DoesNotContain(forbidden, ir);
        Assert.Contains("call i32 @gflat$llvm$", ir);
        Assert.Contains("call void @gflat$llvm$", ir);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnannotatedSelectedEntriesAndLookalikeTokensKeepConservativeUnwindHandling(int optimization)
    {
        (string Name, string Module)[] modules = [
            ("unannotated", "define i32 @entry() { ret i32 42 }"),
            ("header-comment", "define i32 @entry() ; nounwind\n{ ret i32 42 }"),
            ("quoted-attribute", "define i32 @entry() \"nounwind\"=\"true\" { ret i32 42 }"),
            ("string-data", "@text = private constant [8 x i8] c\"nounwind\"\ndefine i32 @entry() { ret i32 42 }"),
            ("global-header", "@nounwind = private global i8 0\ndefine i32 @entry() prefix i8* @nounwind { ret i32 42 }"),
            ("type-header", "%nounwind = type { i8 }\ndefine i32 @entry() prefix %nounwind zeroinitializer { ret i32 42 }"),
            ("embedded-global-header", "@other.nounwind = private global i8 0\ndefine i32 @entry() prefix i8* @other.nounwind { ret i32 42 }"),
            ("other-function", "define i32 @other() nounwind { ret i32 7 }\ndefine i32 @entry() { ret i32 42 }"),
            ("body-label", "define i32 @entry() {\nnounwind:\nret i32 42\n}")
        ];
        var program = new StringBuilder();
        program.Append("int Check0()nothrow=>__llvm<int>(").Append(Literal("; nounwind\nret i32 42")).Append(");\n");
        for (int index = 0; index < modules.Length; index++)
            program.Append("int Check").Append(index + 1).Append("()nothrow=>__llvm_module<int>(")
                .Append(Literal(modules[index].Module)).Append(",\"entry\");\n");
        program.Append("int main()nothrow{");
        for (int index = 0; index <= modules.Length; index++)
            program.Append("if(Check").Append(index).Append("()!=42)return ").Append(index + 1).Append(';');
        program.Append("return 0;}");
        string ir = Emit(program.ToString());
        for (int index = 0; index <= modules.Length; index++)
        {
            string body = Regex.Match(ir, @"(?ms)^define [^\r\n]*@gflat\$Check" + index + @"\([^\r\n]*\{\r?\n(?<body>.*?)^\}")
                .Groups["body"].Value;
            string name = index == 0 ? "raw-body" : modules[index - 1].Name;
            Assert.True(body.Contains("invoke i32 @gflat$llvm$", StringComparison.Ordinal), name + " incorrectly suppressed unwind handling:\n" + body);
        }
        Assert.Contains("personality", ir);
        Assert.Contains("__gflat_eh_", ir);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    private static string Emit(string source)
        => Compiler.Emit(source, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });

    private static string Literal(string text)
        => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
