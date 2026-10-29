namespace gflat.Tests;

public sealed class LlvmDebugMarkerTests
{
    public static IEnumerable<object[]> TextThatIsNotADebugCompileUnit()
    {
        yield return ["; !llvm.dbg.cu = !{!0}\n"];
        yield return ["@text = private constant [13 x i8] c\"!llvm.dbg.cu\\00\"\n"];
        yield return ["!0 = !{!\"!llvm.dbg.cu = !{!1}\"}\n"];
        yield return ["!0 = !DIFile(filename: \"!llvm.dbg.cu\", directory: \".\")\n"];
        // Preserve newlines while masking the whole quoted span: an anchored regex
        // alone must not find a metadata definition inside a multiline string.
        yield return ["!0 = !{!\"before\n!llvm.dbg.cu = !{!1}\nafter\"}\n"];
        // Runtime classification retains quoted @globals, but debug classification
        // must hide them too, including a newline within a quoted identifier.
        yield return ["@\"before\n!llvm.dbg.cu = !{!0}\nafter\" = private global i8 0\n"];
        yield return ["!0 = !{!\"escaped\\22quote\n!llvm.dbg.cu = !{!1}\"}\n"];
        // Similar names and uses are not the named metadata definition itself.
        yield return ["!llvm.dbg.cu.extra = !{}\n"];
        yield return ["!0 = !{!llvm.dbg.cu}\n"];
        yield return ["!\"llvm.dbg.cu\" = !{}\n"];
        yield return ["!llvm.dbg.cu = !0\n"];
    }

    [Theory]
    [MemberData(nameof(TextThatIsNotADebugCompileUnit))]
    public void QuotedTextCommentsAndSimilarNamesDoNotSelectDebugLinking(string ir)
    {
        Assert.Contains("llvm.dbg.cu", ir);
        Assert.False(NativeToolchain.HasDebugInformation(ir));
    }

    [Theory]
    [InlineData("!llvm.dbg.cu = !{}\n")]
    [InlineData("\t !llvm.dbg.cu\t=\t!{!0}\r\n")]
    [InlineData("!llvm.dbg.cu\n =\n !{!0}\n")]
    public void ActualNamedCompileUnitDefinitionSelectsDebugLinking(string ir)
    {
        Assert.True(NativeToolchain.HasDebugInformation(ir));
        // A decoy must not conceal the subsequent actual definition.
        foreach (object[] row in TextThatIsNotADebugCompileUnit())
            Assert.True(NativeToolchain.HasDebugInformation((string)row[0] + ir));
    }

    [Fact]
    public void ActualCompilerOutputDistinguishesLiteralTextFromDebugMetadata()
    {
        const string source = """
            int Marker() nothrow => __llvm_module<int>("; !llvm.dbg.cu = !{}\ndefine i32 @entry() nounwind { ret i32 0 }", "entry");
            int main() {
                readonly(char)* marker = "!llvm.dbg.cu";
                if (marker[0] != '!') return 1;
                return Marker();
            }
            """;
        string plain = Compiler.Emit(source, options: new() { NoStdLib = true });
        Assert.Contains("!llvm.dbg.cu", plain);
        Assert.False(NativeToolchain.HasDebugInformation(plain));
        string debug = Compiler.Emit(source, options: new() { NoStdLib = true, DebugSymbols = true });
        Assert.True(NativeToolchain.HasDebugInformation(debug));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("sentinel")]
    [InlineData("source")]
    public void WindowsNoDebugNativeBuildDoesNotWritePdbForCommentMarker(string pdbState)
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "gflat-debug-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool success = false;
        try
        {
            string output = Path.Combine(directory, "program.exe");
            string pdb = Path.ChangeExtension(output, ".pdb");
            // Raw inputs need no particular extension: a PDB-shaped source proves
            // that correcting the false positive also avoids destructive linking.
            string source = pdbState == "source" ? pdb : Path.Combine(directory, "main.gf");
            const string text = """
                int Marker() nothrow => __llvm_module<int>("; !llvm.dbg.cu = !{}\ndefine i32 @entry() nounwind { ret i32 0 }", "entry");
                int main() {
                    readonly(char)* marker = "!llvm.dbg.cu";
                    if (marker[0] != '!') return 1;
                    return Marker();
                }
                """;
            File.WriteAllText(source, text);
            const string sentinel = "preserve existing PDB-shaped user file";
            if (pdbState == "sentinel") File.WriteAllText(pdb, sentinel);
            Assert.Equal(0, Program.Main([source, "--no-stdlib", "--no-debug", "-o", output]));
            Assert.True(File.Exists(output));
            Assert.Equal(text, File.ReadAllText(source));
            if (pdbState == "absent") Assert.False(File.Exists(pdb));
            else Assert.Equal(pdbState == "source" ? text : sentinel, File.ReadAllText(pdb));
            var run = NativeToolchain.Run(output, []);
            File.WriteAllText(Path.Combine(directory, "stdout.txt"), run.StandardOutput);
            File.WriteAllText(Path.Combine(directory, "stderr.txt"), run.StandardError);
            Assert.Equal(0, run.ExitCode);
            success = true;
        }
        catch (Exception error)
        {
            error.Data["DebugMarkerArtifacts"] = directory;
            throw;
        }
        finally { if (success) Directory.Delete(directory, true); }
    }
}
