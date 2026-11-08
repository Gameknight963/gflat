using gflat.TestSupport;

namespace gflat.Tests;

public sealed class CppExceptionAdapterTests
{
    [Theory]
    [InlineData("StaticLibrary", false, 0)]
    [InlineData("StaticLibrary", true, 2)]
    [InlineData("DynamicLibrary", false, 2)]
    [InlineData("DynamicLibrary", true, 0)]
    public void CppCatchesGflatExceptionsThroughStdException(string kind, bool debug, int optimization)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-cpp-exceptions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "api.gf"), support = Path.Combine(root, "errors.gf");
            File.WriteAllText(support, """
                class MessageError : Exception
                {
                    public immutable override readonly(char)* GetMessage() => "overridden message";
                }
                class FailingMessage : Exception
                {
                    public immutable override readonly(char)* GetMessage() { throw new Exception("getter failed"); }
                }
                """);
            File.WriteAllText(source, """
                void Raise(int mode)
                {
                    if (mode == 0) throw new Exception("plain message", 42);
                    if (mode == 1) throw new MessageError();
                    throw new FailingMessage();
                }
                int Code()
                {
                    try { Raise(0); }
                    catch (ref Exception error) { return error.GetCode(); }
                    return 0;
                }
                """);
            string exports = Path.Combine(root, "exports.txt");
            File.WriteAllText(exports, source + Environment.NewLine);
            string library = Path.Combine(root, OperatingSystem.IsWindows() ? "errors." + (kind == "StaticLibrary" ? "lib" : "dll") :
                "liberrors." + (kind == "StaticLibrary" ? "a" : "so"));
            string standard = Path.Combine(ProjectFileFixture.RepositoryRoot, "Standard");
            Assert.Equal(0, Program.Main([source, support, Path.Combine(standard, "core", "Exception.gf"),
                Path.Combine(standard, "libc", "Memory", "allocation.gf"), "--no-stdlib", "--output-type", kind,
                "--export-source-list", exports, debug ? "--debug" : "--no-debug", "--optimization", optimization.ToString(), "-o", library]));
            string consumer = Path.Combine(root, "consumer.cpp"), executable = Path.Combine(root, "consumer.exe");
            File.WriteAllText(consumer, """
                #include <exception>
                #include <cstring>
                """ + "\n#include \"" + library.Replace('\\', '/') + ".hpp\"\n" + """
                int main() {
                    const char* expected[] = {"plain message", "overridden message", "Gflat exception"};
                    if (Code() != 42) return 1;
                    for (int mode = 0; mode != 3; ++mode) {
                        std::exception_ptr retained;
                        try { Raise(mode); return 2; }
                        catch (const std::exception& error) {
                            if (std::strcmp(error.what(), expected[mode]) != 0) return 3;
                            retained = std::current_exception();
                        }
                        try { std::rethrow_exception(retained); }
                        catch (const std::exception& error) {
                            if (std::strcmp(error.what(), expected[mode]) != 0) return 4;
                        }
                    }
                    return 42;
                }
                """);
            string linkInput = OperatingSystem.IsWindows() && kind == "DynamicLibrary" ? Path.ChangeExtension(library, ".lib") : library;
            var result = NativeToolchain.CompileCpp(consumer, executable, [linkInput]);
            Assert.True(result.ExitCode == 0, result.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
