namespace gflat.Tests.Std.Threading;

public sealed class InterlockedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScalarOperationsPreserveRefStorageAndReturnDocumentedWrappingValues(int optimization)
    {
        string ir = Emit("""
            using Standard::Threading;
            int main()nothrow{
                int value=7;readonly(int) readOnly=-31;
                if(Interlocked::Read(ref value)!=7||Interlocked::Read(ref readOnly)!=-31)return 1;
                if(Interlocked::Exchange(ref value,-3)!=7||value!=-3)return 2;
                if(Interlocked::CompareExchange(ref value,19,8)!=-3||value!=-3)return 3;
                if(Interlocked::CompareExchange(ref value,19,-3)!=-3||value!=19)return 4;
                if(Interlocked::Add(ref value,-7)!=12||value!=12)return 5;
                if(Interlocked::Increment(ref value)!=13||Interlocked::Decrement(ref value)!=12)return 6;
                value=2147483647;if(Interlocked::Add(ref value,1)!=-2147483648||value!=-2147483648)return 7;
                if(Interlocked::Add(ref value,-1)!=2147483647||value!=2147483647)return 8;
                if(Interlocked::Increment(ref value)!=-2147483648||Interlocked::Decrement(ref value)!=2147483647)return 9;
                value=-2147483648;if(Interlocked::Add(ref value,-2147483648)!=0||Interlocked::Read(ref value)!=0)return 10;
                if(readOnly!=-31)return 11;
                long wide=4294967297l;readonly(long) readOnlyWide=-5000000000l;
                if(Interlocked::Read(ref wide)!=4294967297l||Interlocked::Read(ref readOnlyWide)!=-5000000000l)return 12;
                if(Interlocked::Exchange(ref wide,-4294967297l)!=4294967297l||wide!=-4294967297l)return 13;
                wide=4294967297l;
                if(Interlocked::CompareExchange(ref wide,8589934593l,1l)!=4294967297l||wide!=4294967297l)return 14;
                if(Interlocked::CompareExchange(ref wide,8589934593l,4294967297l)!=4294967297l||wide!=8589934593l)return 15;
                if(Interlocked::Add(ref wide,-4294967296l)!=4294967297l||wide!=4294967297l)return 16;
                if(Interlocked::Increment(ref wide)!=4294967298l||Interlocked::Decrement(ref wide)!=4294967297l)return 17;
                wide=9223372036854775807l;
                if(Interlocked::Add(ref wide,1l)!=-9223372036854775807l-1l||wide!=-9223372036854775807l-1l)return 18;
                if(Interlocked::Add(ref wide,-1l)!=9223372036854775807l||wide!=9223372036854775807l)return 19;
                if(Interlocked::Increment(ref wide)!=-9223372036854775807l-1l||Interlocked::Decrement(ref wide)!=9223372036854775807l)return 20;
                wide=-9223372036854775807l-1l;
                if(Interlocked::Add(ref wide,-9223372036854775807l-1l)!=0l||Interlocked::Read(ref wide)!=0l)return 21;
                if(readOnlyWide!=-5000000000l)return 22;return 0;
            }
            """);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RealJoinedWorkersPreserveAddCasAndExchangeAccountingForBothWidths(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-interlocked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Emit("""
                using Standard::Threading;
                extern int run_atomic_threads(void(int,int,int,int*,long*,long*)* worker,int mode,int* narrow,long* wide,long* total)nothrow;
                void Worker(int mode,int workerIndex,int iterations,int* narrow,long* wide,long* result)nothrow{
                    long sum=0l;
                    for(int index=0;index<iterations;index++){
                        int sequence=workerIndex*iterations+index+1;
                        if(mode==0)sum+=(long)Interlocked::Add(ref *narrow,1);
                        else if(mode==1){
                            while(true){
                                int previous=Interlocked::Read(ref *narrow);
                                if(Interlocked::CompareExchange(ref *narrow,previous+1,previous)==previous){sum+=(long)previous+1l;break;}
                            }
                        }
                        else if(mode==2)sum+=(long)Interlocked::Exchange(ref *narrow,sequence);
                        else if(mode==3)sum+=Interlocked::Add(ref *wide,1l);
                        else if(mode==4){
                            while(true){
                                long previous=Interlocked::Read(ref *wide);
                                if(Interlocked::CompareExchange(ref *wide,previous+1l,previous)==previous){sum+=previous+1l;break;}
                            }
                        }
                        else sum+=Interlocked::Exchange(ref *wide,4294967296l+(long)sequence);
                    }
                    *result=sum;
                }
                int main()nothrow{
                    long count=80000l;long triangular=count*(count+1l)/2l;
                    for(int mode=0;mode<6;mode++){
                        int narrow=0;long wide=4294967296l;long total=0l;
                        if(run_atomic_threads(Worker,mode,&narrow,&wide,&total)!=0)return 1;
                        // All workers are joined: ordinary final reads no longer overlap atomic writes.
                        if(mode<2){if(narrow!=80000||total!=triangular)return 2;}
                        else if(mode==2){if(narrow<1||narrow>80000||total+(long)narrow!=triangular)return 3;}
                        else if(mode<5){if(wide!=4295047296l||total!=count*4294967296l+triangular)return 4;}
                        else if(wide<4294967297l||wide>4295047296l||total+wide!=(count+1l)*4294967296l+triangular)return 5;
                    }
                    return 0;
                }
                """);
            string llvm = Path.Combine(directory, "main.ll"), fixture = Path.Combine(directory, "threads.cpp"), executable = Path.Combine(directory, "main.exe");
            File.WriteAllText(llvm, ir);File.WriteAllText(fixture, ThreadRunner);
            var arguments = new List<string> { "--driver-mode=g++", "-std=c++17", "-O" + optimization, "-fexceptions", fixture, llvm, "-o", executable };
            arguments.AddRange(NativeRuntime.HostArguments());
            if (!OperatingSystem.IsWindows()) arguments.Add("-pthread");
            var compiled = NativeToolchain.Run(NativeToolchain.FindClang(), arguments, 60000);
            File.WriteAllText(Path.Combine(directory, "compile.stdout"), compiled.StandardOutput);
            File.WriteAllText(Path.Combine(directory, "compile.stderr"), compiled.StandardError);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, [], 60000);
            File.WriteAllText(Path.Combine(directory, "run.stdout"), result.StandardOutput);
            File.WriteAllText(Path.Combine(directory, "run.stderr"), result.StandardError);
            Assert.True(result.ExitCode == 0, $"Interlocked native exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        catch (Exception error) { error.Data["InterlockedThreadDirectory"] = directory;throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static string Emit(string program)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Standard/core/Threading/Interlocked.gf");
        string ir = Compiler.Emit([new SourceFile(path, File.ReadAllText(path)), new SourceFile("interlocked.gf", program)],
            options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "gflat$Memory$", "@malloc", "@free", "__gflat_gc", "cmpxchg weak", "add nsw", "add nuw" })
            Assert.DoesNotContain(forbidden, ir);
        string[] methods = ir.Split('\n').Where(line => line.StartsWith("define ", StringComparison.Ordinal)
            && line.Contains("$Interlocked$", StringComparison.Ordinal)).ToArray();
        Assert.Equal(12, methods.Length);Assert.All(methods, line => Assert.Contains("nounwind", line));
        foreach (string operation in new[] { "load atomic i32", "load atomic i64", "atomicrmw xchg i32*", "atomicrmw xchg i64*",
            "atomicrmw add i32*", "atomicrmw add i64*", "cmpxchg i32*", "cmpxchg i64*" })
            Assert.Contains(operation, ir);
        Assert.Equal(2, ir.Split('\n').Count(line => line.Contains("cmpxchg ", StringComparison.Ordinal)
            && line.Contains("seq_cst seq_cst", StringComparison.Ordinal)));
        return ir;
    }

    private const string ThreadRunner = """
        #include <atomic>
        #include <thread>
        static_assert(sizeof(int) == 4 && sizeof(long long) == 8);
        using Worker = void (*)(int, int, int, int*, long long*, long long*);
        extern "C" int run_atomic_threads(Worker worker, int mode, int* narrow, long long* wide, long long* total) noexcept {
            constexpr int count = 4, iterations = 20000;
            alignas(8) long long results[count] = {};
            std::atomic<bool> start{false};std::thread workers[count];
            try {
                for (int index = 0; index < count; ++index) {
                    workers[index] = std::thread([&, index] {
                        while (!start.load(std::memory_order_acquire)) std::this_thread::yield();
                        worker(mode, index, iterations, narrow, wide, &results[index]);
                    });
                }
            } catch (...) {
                start.store(true, std::memory_order_release);
                for (auto& thread : workers) if (thread.joinable()) thread.join();
                return 1;
            }
            start.store(true, std::memory_order_release);
            for (auto& thread : workers) thread.join();
            long long sum = 0;for (long long result : results) sum += result;
            *total = sum;return 0;
        }
        """;
}
