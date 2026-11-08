using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeRecordImportTests
{
    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void RecordsShareNativeSizeOffsetsAndWritableStorage(string language, int optimization)
    {
        string code = "#lang " + language + "\n" + """
            typedef struct Point { char tag; double x; int count; } Point;
            static Point current = { 'a', 20.5, 21 };
            Point* GetPoint(void) { return &current; }
            int ReadPoint(const Point* point) { return point->tag == 'a' && point->x == 20.5 ? point->count : -1; }
            void SetPoint(Point* point, int count) { point->count = count; }
            int NativeSize(void) { return sizeof(Point); }
            #lang gflat
            int main()
            {
                Point* point = GetPoint()!;
                if ((int)sizeof(Point) != NativeSize()) return 1;
                if (point.tag != 'a' || point.x != 20.5 || point.count != 21) return 2;
                point.count = 42;
                if (ReadPoint(point) != 42) return 3;
                Point local = default(Point);
                local.tag = 'a';
                local.x = 20.5;
                SetPoint(&local, 42);
                return ReadPoint(&local);
            }
            """;
        Run(code, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamespacedCppRecordsKeepConstFieldsAndAliases(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                class Data { public: const int key; double value; };
                using Alias = Data;
                static Data data = {42, 1.5};
                const Alias* Get() { return &data; }
                int Read(const Alias* value) { return value->key; }
            }
            #lang gflat
            int main()
            {
                readonly(Native::Data)* data = Native::Get()!;
                if (data.value != 1.5) return 1;
                return Native::Read(data);
            }
            """, optimization);
    }

    [Theory]
    [InlineData("c", 0)]
    [InlineData("c", 2)]
    [InlineData("cpp", 0)]
    [InlineData("cpp", 2)]
    public void NativeTagNamesCanAlsoNameFunctions(string language, int optimization)
    {
        Run("#lang " + language + "\n" + """
            struct Record { int value; };
            int Record(const struct Record* value) { return value->value; }
            #lang gflat
            int main()
            {
                Record value = default(Record);
                value.value = 42;
                return Record(&value);
            }
            """, optimization);
    }

    [Fact]
    public void SharedHeaderRecordsAreDeduplicatedAcrossCAndCppAndCachedSnapshots()
    {
        var header = new SourceFile(Path.GetFullPath("native-record-shared.gfh"),
            "typedef struct Shared { int value;\n#ifdef __cplusplus\n" +
            "int Read() const { return value; } static int Twice(int value) { return value * 2; }\n#endif\n} Shared;");
        string code = "#lang c\n#include \"native-record-shared.gfh\"\nShared* InC(Shared* p) { return p; }\n" +
            "#lang cpp\n#include \"native-record-shared.gfh\"\nShared* InCpp(Shared* p) { return p; }\n" +
            "#lang gflat\nint main() { Shared value = default(Shared); return Shared::Twice(value.Read()); }";
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-record-header-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "native-record-shared.gfh"), header.Text);
            var options = Options() with { IncludeDirectories = [root] };
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var checkedProgram = Compiler.Check(new[]
                {
                    new SourceFile(Path.Combine(root, "main.gf"), code),
                    new SourceFile(Path.Combine(root, "second.gf"),
                        "#lang cpp\n#include \"native-record-shared.gfh\"\nShared* Second(Shared* p) { return p; }")
                }, options: options);
                Assert.Single(checkedProgram.Ast.Members.OfType<ast.StructDeclaration>(), declaration => declaration.Name == "Shared");
                checkedProgram.Ast.Members.Clear();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("struct Data { int value : 8; };")]
    [InlineData("#pragma pack(push, 1)\nstruct Data { char tag; int value; };\n#pragma pack(pop)")]
    [InlineData("struct alignas(32) Data { int value; };")]
    [InlineData("struct Data { int value[2]; };")]
    [InlineData("struct Data { Data() {} int value; };")]
    [InlineData("class Data { int value; };")]
    [InlineData("struct Data { virtual int Read() { return 42; } int value; };")]
    public void UnsupportedLayoutsAndLifetimesCannotBeMaterializedAsHostValues(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "#lang cpp\n" + declaration + "\n#lang gflat\nint main() { Data value = default(Data); return 0; }",
            options: Options()));
        Assert.Contains("Data", error.Description);
        Assert.Contains("pointer-only", error.Description);
    }

    private static CompilationOptions Options() => new()
    {
        NoStdLib = true,
        ForeignCompilerResolver = () => NativeToolchain.FindClang(),
        ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
    };

    private static void Run(string code, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-record-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var program = Compiler.EmitProgram([new SourceFile("records.gf", code)], options: Options());
            string ir = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 42, $"Native record exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
