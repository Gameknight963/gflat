namespace gflat.Tests;

public sealed class NativeUnicodeTypeTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void UnicodeScalarsAndPointersUseNativeWidthsAndSignedness(int optimization, bool shortWchar)
    {
        const string code = """
            #lang cpp
            struct WideRecord { wchar_t Value; };
            static WideRecord wide = { (wchar_t)-1 };
            WideRecord* GetWide() { return &wide; }
            int WideBytes() { return sizeof(wchar_t); }
            long long WideValue() { return wide.Value; }
            wchar_t EchoWide(wchar_t value) { return value; }
            const char8_t* Utf8Text() { return u8"\u00e9"; }
            const char16_t* Utf16Text() { return u"\u03a9"; }
            const char32_t* Utf32Text() { return U"\U0001f642"; }
            char8_t Echo8(char8_t value) { return value; }
            char16_t Echo16(char16_t value) { return value; }
            char32_t Echo32(char32_t value) { return value; }
            #lang gflat
            int main()
            {
                WideRecord* wide = GetWide()!;
                if ((int)sizeof(WideRecord) != WideBytes()) return 1;
                if ((long)wide.Value != WideValue()) return 2;
                wide.Value = EchoWide(wide.Value);
                readonly(byte)* utf8 = Utf8Text()!;
                readonly(ushort)* utf16 = Utf16Text()!;
                readonly(uint)* utf32 = Utf32Text()!;
                if (utf8[0] != (byte)0xc3 || utf8[1] != (byte)0xa9 || utf8[2] != (byte)0) return 3;
                if (utf16[0] != (ushort)0x03a9 || utf16[1] != (ushort)0) return 4;
                if (utf32[0] != (uint)0x1f642 || utf32[1] != (uint)0) return 5;
                if (Echo8((byte)0xff) != (byte)0xff) return 6;
                if (Echo16((ushort)0xd800) != (ushort)0xd800) return 7;
                if (Echo32((uint)0xffffffff) != (uint)0xffffffff) return 8;
                return 42;
            }
            """;
        var arguments = NativeRuntime.HostCompilerArguments(true).Concat(new[] { "-std=c++20" })
            .Concat(shortWchar ? ["-fshort-wchar"] : Array.Empty<string>()).ToArray();
        var program = Compiler.EmitProgram([new SourceFile("native-unicode.gf", code)], options: new()
        {
            NoStdLib = true,
            ForeignCompilerResolver = () => NativeToolchain.FindClang(),
            ForeignCompilerArguments = arguments
        });
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-unicode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RemovingCompilerPredefinesDoesNotBreakOrdinaryCppImports()
    {
        var (unit, _) = Compiler.Check([new SourceFile("native-undef.gf", """
            #lang cpp
            int NativeAnswer() { return 42; }
            #lang gflat
            int Read() => NativeAnswer();
            """)], options: new()
        {
            NoStdLib = true,
            ForeignCompilerResolver = () => NativeToolchain.FindClang(),
            ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true).Concat(new[] { "-undef" }).ToArray()
        });
        Assert.Contains(unit.Members.OfType<ast.MethodDeclaration>(), method => method.Name == "NativeAnswer");
    }

    [Fact]
    public void PreCpp20Char8AliasesKeepTheirDeclaredRepresentation()
    {
        var (unit, _) = Compiler.Check([new SourceFile("native-char8-alias.gf", """
            #lang cpp
            typedef int char8_t;
            char8_t Echo(char8_t value) { return value; }
            #lang gflat
            int Read() => Echo(100000);
            """)], options: new()
        {
            NoStdLib = true,
            ForeignCompilerResolver = () => NativeToolchain.FindClang(),
            ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true).Concat(new[] { "-std=c++17" }).ToArray()
        });
        var echo = Assert.Single(unit.Members.OfType<ast.MethodDeclaration>(), method => method.Name == "Echo");
        Assert.Equal("int", TypeChecker.TypeName(echo.ReturnType));
    }
}
