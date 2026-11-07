namespace gflat.Tests.Std.Queries;

public sealed class GroupTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/HashMap.gf", "core/Threading/Interlocked.gf", "core/Memory/SharedStorage.gf",
            "core/Queries/Group.gf", "core/Queries/GroupJoinSource.gf", "core/Queries/enumerable_group_join.gf",
            "core/Queries/IntRange.gf", "core/Queries/FilteredIntRange.gf", "core/Queries/enumerable_elements.gf"))
        .Append(new SourceFile("shared-group.gf", code));

    [Fact]
    public void IteratorValuesAreImmutable()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Emit(Sources("""
            using Standard::Queries;

            void Change(Group<int>* group)
            {
                var iterator = group.GetEnumerator();
                if (iterator.MoveNext()) iterator.Current = 9;
            }
            int main() => 0;
            """)));
        Assert.Contains("readonly", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ConcurrentCopiesAndIteratorDestructionReleaseTheFinalOwner(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_group_threads_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Compiler.Emit(Sources("""
                using Standard::Queries;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                extern "C" int run_group_threads(int(void*)* worker, void* context) nothrow;
                int live = 0;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        void*? value = malloc(bytes);
                        if (value != null) live++;
                        return value;
                    }
                    public replace void Free(void* storage) nothrow { live--; free(storage); }
                }
                int Key(int value) => value % 2;
                Group<int> Keep(int outer, Group<int> values) => move values;
                int Worker(void* context) nothrow
                {
                    try
                    {
                        Group<int>* original = (Group<int>*)context;
                        for (int i = 0; i < 10000; i++)
                        {
                            Group<int> first = *original;
                            Group<int> second = first;
                            first = new Group<int>();
                            var iterator = second.GetEnumerator();
                            second = new Group<int>();
                            if (!iterator.MoveNext() || iterator.Current != 1 || !iterator.MoveNext()
                                || iterator.Current != 3 || iterator.MoveNext()) return 1;
                        }
                        return 0;
                    }
                    catch { return 2; }
                }
                int main()
                {
                    Group<int> saved = new();
                    {
                        var outer = new IntRange(1, 1).AsEnumerable();
                        var inner = new IntRange(0, 4).AsEnumerable();
                        saved = outer.GroupJoin(inner, Key, Key, Keep).First();
                    }
                    if (run_group_threads(Worker, &saved) != 0 || saved.Count != 2) return 1;
                    saved = new Group<int>();
                    if (live != 0) return 2;
                    return 0;
                }
                """), options: new() { InternalizeFunctions = true });
            string llvm = Path.Combine(directory, "main.ll"), fixture = Path.Combine(directory, "threads.cpp"), executable = Path.Combine(directory, "main.exe");
            File.WriteAllText(llvm, ir);
            File.WriteAllText(fixture, """
                #include <atomic>
                #include <thread>
                extern "C" int run_group_threads(int (*worker)(void*), void* context) noexcept {
                    std::atomic<bool> start{false};
                    std::thread threads[4];
                    int results[4] = {};
                    try {
                        for (int i = 0; i < 4; ++i) threads[i] = std::thread([&, i] {
                            while (!start.load(std::memory_order_acquire)) std::this_thread::yield();
                            results[i] = worker(context);
                        });
                    } catch (...) {
                        start.store(true, std::memory_order_release);
                        for (auto& thread : threads) if (thread.joinable()) thread.join();
                        return 1;
                    }
                    start.store(true, std::memory_order_release);
                    for (auto& thread : threads) thread.join();
                    for (int result : results) if (result != 0) return result;
                    return 0;
                }
                """);
            var arguments = new List<string> { "--driver-mode=g++", "-std=c++17", "-O" + optimization, "-fexceptions", fixture, llvm, "-o", executable };
            string compiler = NativeToolchain.FindClang();
            var runtimes = NativeToolchain.RequiredNativeRuntimes(ir);
            if (runtimes.Exceptions) arguments.Add(NativeRuntime.GetObject(compiler));
            if (runtimes.Stdio) arguments.Add(NativeRuntime.GetStdioObject(compiler, cppHost: true));
            arguments.AddRange(NativeRuntime.HostArguments());
            if (!OperatingSystem.IsWindows()) arguments.Add("-pthread");
            var compiled = NativeToolchain.Run(compiler, arguments, 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, [], 60000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardOutput}\n{result.StandardError}");
        }
        catch (Exception error) { error.Data["GroupThreadDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }
}
