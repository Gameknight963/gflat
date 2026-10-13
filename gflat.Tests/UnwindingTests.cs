using gflat.CompileExceptions;

namespace gflat.Tests;

public class UnwindingTests
{
    [Fact]
    public void FunctionPointerPropagatesWithoutChangingReturnType()
    {
        const string code = """
            int Fail() { throw new Exception(c"failure", 42); }
            int Bridge(int()* callback) { return callback(); }
            int main() { try { return Bridge(&Fail); } catch (Exception* e) { return e.code; } }
            """;
        Assert.Contains("define i32 @gflat$Fail()", CompilerTestHelper.EmitIr(code));
        Assert.Equal(42, CompilerTestHelper.Run(code).ExitCode);
    }

    [Fact]
    public void CleanupCanHandleItsOwnExceptionWhileUnwinding()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            void Fail() { throw new Exception(); }
            void Work() {
                defer { try { Fail(); } catch { putchar(65); } }
                Fail();
            }
            int main() { try { Work(); } catch { putchar(66); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("AB", result.StandardOutput);
    }

    [Theory]
    [InlineData("throw e;", "CDD")]
    [InlineData("throw move e;", "D")]
    public void ThrowUsesExplicitCopyAndMoveRules(string expression, string expected)
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            class E : Exception {
                public E() {}
                public copy E(readonly(E)* source) { putchar(67); }
                public ~E() { putchar(68); }
            }
            int main() { try { E e = new E();
            """ + expression + " } catch (E* e) {} return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Theory]
    [InlineData("void F() { throw; }")]
    [InlineData("void F() { Exception* e = new* Exception(); throw e; }")]
    [InlineData("void F() { try {} catch { void()* f = () => { throw; }; } }")]
    public void InvalidThrowsAreRejected(string code)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));

    [Fact]
    public void ForeignExceptionUnwindsThroughGflatAndPreservesItsType()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "bridge.ll"), cpp = Path.Combine(directory, "test.cpp"), executable = Path.Combine(directory, "test.exe");
            File.WriteAllText(ir, CompilerTestHelper.EmitIr("""
                extern void Cleanup();
                void Bridge(void()* callback) {
                    defer Cleanup();
                    try { callback(); } catch (Exception* e) { throw new Exception(); }
                }
                """));
            File.WriteAllText(cpp, """
                extern "C" void Bridge(void (*)()) asm("gflat$Bridge");
                int cleanups = 0;
                extern "C" void Cleanup() { ++cleanups; }
                void Fail() { throw 42; }
                int main() {
                    try { Bridge(Fail); }
                    catch (int n) { return n == 42 && cleanups == 1 ? 0 : 1; }
                    catch (...) { return 2; }
                    return 3;
                }
                """);
            var compilation = NativeToolchain.CompileCpp(cpp, executable, [ir]);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            Assert.Equal(0, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}
