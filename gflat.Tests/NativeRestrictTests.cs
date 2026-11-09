namespace gflat.Tests;

public sealed class NativeRestrictTests
{
    [Theory]
    [InlineData("c", "restrict", 0)]
    [InlineData("c", "restrict", 2)]
    [InlineData("c", "__restrict", 0)]
    [InlineData("c", "__restrict__", 2)]
    [InlineData("cpp", "__restrict", 0)]
    [InlineData("cpp", "__restrict__", 2)]
    public void RestrictedPointersAndCallbacksKeepTheirValueTypes(string language, string qualifier, int optimization)
    {
        Run("#lang " + language + "\n" + $$"""
            typedef const int* ConstPointer;
            int Read(ConstPointer {{qualifier}} input) { return *input; }
            int ReadIndirect(ConstPointer {{qualifier}}* {{qualifier}} input) { return **input; }
            int Apply(int (*const callback)(int), ConstPointer {{qualifier}} value) { return callback(*value); }
            #lang gflat
            int Increment(int value) => value + 1;
            int main()
            {
                int value = 41;
                if (Read(&value) != 41) return 1;
                readonly(int)*? pointer = &value;
                if (ReadIndirect(&pointer) != 41) return 2;
                return Apply(Increment, &value);
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CppRecordNamedRestrictRemainsAType(int optimization)
        => Run("""
            #lang cpp
            struct restrict { int value; };
            int Read(const struct restrict* input) { return input->value; }
            #lang gflat
            int main()
            {
                @restrict value = new();
                value.value = 42;
                return Read(&value);
            }
            """, optimization);

    [Theory]
    [InlineData("__restrict", 0)]
    [InlineData("__restrict__", 2)]
    public void RestrictedCppReferencesRemainBorrowedArguments(string qualifier, int optimization)
        => Run("#lang cpp\nvoid Increment(int& " + qualifier + " value) { ++value; }\n" + """
            #lang gflat
            int main()
            {
                int value = 41;
                Increment(ref value);
                return value;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeStdioHeadersExposeStreamCallsWithoutManualDeclarations(int optimization)
        => Run("""
            #lang c
            #include <stdio.h>
            #lang gflat
            int main()
            {
                var file = tmpfile();
                if (file == null) return 1;
                if (fputs("*", file) < 0)
                {
                    fclose(file);
                    return 2;
                }
                rewind(file);
                int value = fgetc(file);
                if (fclose(file) != 0) return 3;
                return value;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-restrict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new CompilationOptions
            {
                NoStdLib = true,
                InternalizeFunctions = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(),
                ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
            };
            var program = Compiler.EmitProgram([new SourceFile("native-restrict.gf", code)], options: options);
            string ir = Path.Combine(root, "program.ll"), executable = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 42, $"Native restrict exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
