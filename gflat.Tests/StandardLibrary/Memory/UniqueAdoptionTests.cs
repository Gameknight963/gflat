namespace gflat.Tests.Std.Memory;

public sealed class UniqueAdoptionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NullablePointersAndReleasedStorageBecomeEmptyOrSingleOwners(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Memory;

            int destroyed = 0;
            struct Payload
            {
                public int Number;
                public Payload(int number) { Number = number; }
                ~Payload() { destroyed++; }
            }
            Payload*? Create(bool present)
            {
                if (!present) return null;
                return new* Payload(42);
            }
            int main()
            {
                {
                    Unique<Payload> absent = new(Create(false));
                    Unique<Payload> explicitNull = new(null);
                    if (!absent.IsEmpty || !explicitNull.IsEmpty) return 1;
                    Unique<Payload> first = new(Create(true));
                    Payload*? address = first.Get();
                    if (address == null || first.Value.Number != 42) return 2;
                    Unique<Payload> second = new(first.Release());
                    if (!first.IsEmpty || second.Get() != address || destroyed != 0) return 3;
                    second.Value.Number++;
                    if (address!.Number != 43) return 4;
                }
                if (destroyed != 1) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Unique adoption exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NullableNativeFactoryResultsRetainNativeDeletion(int optimization)
    {
        var result = RunNative("""
            #lang cpp
            static int live = 0;
            struct NativePayload
            {
                int number;
                explicit NativePayload(int value) : number(value) { ++live; }
                ~NativePayload() { --live; }
                int Read() const noexcept { return number; }
            };
            NativePayload* MaybeCreate(bool present)
            {
                return present ? new NativePayload(42) : nullptr;
            }
            int LiveCount() { return live; }
            #lang gflat
            using Standard::Memory;

            int main()
            {
                {
                    Unique<NativePayload> absent = new(MaybeCreate(false));
                    if (!absent.IsEmpty || LiveCount() != 0) return 1;
                    Unique<NativePayload> owner = new(MaybeCreate(true));
                    if (owner.IsEmpty || owner.Value.Read() != 42 || LiveCount() != 1) return 2;
                    Unique<NativePayload> moved = move owner;
                    if (moved.Value.Read() != 42 || LiveCount() != 1) return 3;
                }
                if (LiveCount() != 0) return 4;
                return 0;
            }
            """, optimization);
        Assert.True(result.ExitCode == 0, $"Native adoption exit {result.ExitCode}: {result.StandardError}");
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => StandardTestSources.Load("core/Memory/Unique.gf").Append(new SourceFile("unique-adoption.gf", code));

    private static ExecutionResult RunNative(string code, int optimization)
    {
        var directory = Directory.CreateTempSubdirectory("gflat-unique-native-");
        try
        {
            var program = Compiler.EmitProgram(Sources(code), options: new()
            {
                InternalizeFunctions = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(),
                ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
            });
            string ir = Path.Combine(directory.FullName, "program.ll");
            string executable = Path.Combine(directory.FullName, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            return new(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        finally
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(root, Path.GetFullPath(directory.FullName), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            directory.Delete(recursive: true);
        }
    }
}
