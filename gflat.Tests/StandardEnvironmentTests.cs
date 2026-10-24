using System.Diagnostics;

namespace gflat.Tests;

public sealed class StandardEnvironmentTests
{
    private static void Run(string program, bool strings = false, int? optimization = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-environment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var names = new List<string> { "libc/Environment/Environment.gf" };
            if (strings) names.AddRange(["libc/Environment/EnvironmentText.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Formatting.gf"]);
            var files = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program));
            string ir = Compiler.Emit(files, options: new() { NoStdLib = !strings });
            if (!strings)
            {
                Assert.DoesNotContain("%Exception", ir);
                Assert.DoesNotContain("Memory$Allocate", ir);
                Assert.DoesNotContain("__gflat_eh", ir);
                Assert.DoesNotContain("%std$String", ir);
            }
            string llvm = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
            File.WriteAllText(llvm, ir);
            var compiled = NativeToolchain.Compile(llvm, exe, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment["GFLAT_STD_PRESENT"] = "value with spaces";
            start.Environment["GFLAT_STD_EMPTY"] = "";
            start.Environment["GFLAT_STD_EXTRA"] = "another";
            start.Environment.Remove("GFLAT_STD_MISSING");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Environment test timed out"); }
            Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}: {errors.GetAwaiter().GetResult()} {output.GetAwaiter().GetResult()}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TryLookupAllowsNameBorrowedFromItsAssignmentDestination(int optimization)
    {
        Run("""
            using std;
            int main() {
                String output = s"GFLAT_STD_PRESENT";
                if (!EnvironmentText::TryGetVariable(output.ToCString(), ref output)
                    || !output.Equals("value with spaces") || output.Length != 17) return 1;
                readonly(char)*? original = Environment::GetVariable("GFLAT_STD_PRESENT");
                if (original == null) return 2;
                for (nuint i = 0; i < 17; i++) if (original![i] != output.Data[i]) return 3;
                if (original![17] != '\0') return 4;
                output[0] = 'X';
                original = Environment::GetVariable("GFLAT_STD_PRESENT");
                if (original == null || original![0] != 'v') return 5;

                output = s"GFLAT_STD_EMPTY";
                if (!EnvironmentText::TryGetVariable(output.ToCString(), ref output)
                    || !output.IsEmpty || output.Data[0] != '\0') return 6;
                output = s"GFLAT_STD_MISSING";
                if (EnvironmentText::TryGetVariable(output.ToCString(), ref output)
                    || !output.IsEmpty || output.Data[0] != '\0') return 7;
                return 0;
            }
            """, strings: true, optimization: optimization);
    }

    [Fact]
    public void BorrowedLookupDistinguishesEmptyMissingAndNullTerminatedNamesWithoutStd()
    {
        Run("""
            using std;
            int main() {
                readonly(char)*? value = Environment::GetVariable("GFLAT_STD_PRESENT");
                if (value == null) return 1;
                immutable(char*) expected = "value with spaces";
                for (int i = 0; i < 17; i++) if (value![i] != expected[i]) return 2;
                if (value![17] != '\0') return 3;
                readonly(char)*? empty = Environment::GetVariable("GFLAT_STD_EMPTY");
                if (empty == null || empty![0] != '\0') return 4;
                if (Environment::GetVariable("GFLAT_STD_MISSING") != null) return 5;
                readonly(char)*? prefix = Environment::GetVariable("GFLAT_STD_PRESENT\0ignored");
                if (prefix == null || prefix![0] != 'v' || prefix![17] != '\0') return 6;
                return 0;
            }
            """);
    }

    [Fact]
    public void OwningCopiesRemainIndependentAndTryLookupClearsOnlyMissingOutput()
    {
        Run("""
            using std;
            int main() {
                String first = EnvironmentText::GetVariable("GFLAT_STD_PRESENT");
                String second = EnvironmentText::GetVariable("GFLAT_STD_EXTRA");
                if (first.Length != (nuint)17 || second.Length != (nuint)7) return 1;
                first[0] = 'X';
                readonly(char)*? original = Environment::GetVariable("GFLAT_STD_PRESENT");
                if (original == null || original![0] != 'v' || first.Data[0] != 'X') return 2;
                String saved = first; first.Clear();
                if (saved.Length != (nuint)17 || saved.Data[0] != 'X') return 3;
                if (!EnvironmentText::TryGetVariable("GFLAT_STD_EMPTY", ref saved) || saved.Length != (nuint)0 || saved.Data[0] != '\0') return 4;
                saved = s"old";
                if (EnvironmentText::TryGetVariable("GFLAT_STD_MISSING", ref saved) || saved.Length != (nuint)0 || saved.Data[0] != '\0') return 5;
                try { String missing = EnvironmentText::GetVariable("GFLAT_STD_MISSING"); return 6; } catch (Exception* error) { }
                String empty = EnvironmentText::GetVariable("GFLAT_STD_EMPTY");
                if (empty.Length != (nuint)0 || empty.Data[0] != '\0') return 7;
                return 0;
            }
            """, strings: true);
    }

    [Fact]
    public void AllocationFailurePreservesOldOutputAndBorrowedLookupDoesNotAllocate()
    {
        Run("""
            using std;
            extern void*? malloc(nuint size);
            extern void free(void* data);
            static class State { public static bool Fail; public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    State::Allocations++;
                    if (State::Fail && size == (nuint)18) return null;
                    return malloc(size);
                }
                public replace void Free(void* data) nothrow { free(data); }
            }
            int main() {
                readonly(char)*? borrowed = Environment::GetVariable("GFLAT_STD_PRESENT");
                if (borrowed == null || State::Allocations != 0) return 1;
                String output = s"old";
                readonly(char)* original = output.Data;
                nuint capacity = output.Capacity;
                State::Fail = true;
                try { EnvironmentText::TryGetVariable("GFLAT_STD_PRESENT", ref output); return 2; }
                catch (BadAllocException* error) { }
                if (output.Data != original || output.Capacity != capacity || output.Length != (nuint)3) return 3;
                if (output.Data[0] != 'o' || output.Data[1] != 'l' || output.Data[2] != 'd' || output.Data[3] != '\0') return 4;
                State::Fail = false;
                if (!EnvironmentText::TryGetVariable("GFLAT_STD_PRESENT", ref output) || output.Length != (nuint)17 || output.Data[0] != 'v') return 5;
                return 0;
            }
            """, strings: true);
    }
}
