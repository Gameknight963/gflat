namespace gflat.Tests;

public sealed class PlatformClockTests
{
    [Fact]
    public void OtherPlatformProviderAndItsTimerSpecializationStillTypeCheck()
    {
        bool windows = OperatingSystem.IsWindows();
        string folder = windows ? "linux" : "windows";
        string filename = windows ? "MonotonicClock.gf" : "PerformanceCounter.gf";
        string provider = windows ? "Linux::MonotonicClock" : "Windows::PerformanceCounter";
        string symbol = windows ? "clock_gettime" : "QueryPerformanceCounter";
        string path = Path.Combine(AppContext.BaseDirectory, "Platform", folder, filename);
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file)))
            .Append(new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("other-platform-clock.gf", $$"""
                alias Timer=std::Time::Stopwatch<{{provider}}>;
                int main(){Timer value=Timer::StartNew();value.Stop();std::Time::TimeSpan elapsed=value.Elapsed;return 0;}
                """));
        // Emit only: declarations and generic specialization are checked without
        // linking or executing the other operating system's native functions.
        Assert.Contains("@" + symbol + "(", Compiler.Emit(sources));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostProviderIsMonotonicAndIntegratesWithStopwatchState(int optimization)
    {
        bool windows=OperatingSystem.IsWindows();
        Assert.True(windows||OperatingSystem.IsLinux(),"Platform clock providers support Windows x64 and Linux LP64 only.");
        string folder=windows?"windows":"linux";
        string filename=windows?"PerformanceCounter.gf":"MonotonicClock.gf";
        string provider=windows?"Windows::PerformanceCounter":"Linux::MonotonicClock";
        string path=Path.Combine(AppContext.BaseDirectory,"Platform",folder,filename);
        string program=$$"""
            using std; using std::Time;
            alias Clock={{provider}};
            alias Timer=std::Time::Stopwatch<Clock>;
            int main(){
                if(sizeof(long)!=8||sizeof(nint)!=8)return 1;
                long frequency=Clock::GetFrequency();if(frequency<=0l)return 2;
                for(int i=0;i<100;i++)if(Clock::GetFrequency()!=frequency)return 3;

                Timer watch=new();
                if(watch.IsRunning||watch.ElapsedTicks!=0l||watch.ElapsedSeconds!=0.0)return 4;
                TimeSpan initial=watch.Elapsed;if(initial.Ticks!=0l)return 5;
                watch.Start();if(!watch.IsRunning)return 6;
                long first=Clock::GetTimestamp();long previous=first;bool advanced=false;
                for(int i=0;i<100000;i++){
                    long next=Clock::GetTimestamp();if(next<previous)return 7;
                    if(next>first)advanced=true;previous=next;
                    if(advanced&&i>=100)break;
                }
                if(!advanced)return 8;
                if(watch.ElapsedTicks<0l||watch.ElapsedSeconds<0.0)return 9;
                TimeSpan running=watch.Elapsed;if(running.Ticks<0l)return 10;
                watch.Stop();if(watch.IsRunning)return 11;
                long frozen=watch.ElapsedTicks;double frozenSeconds=watch.ElapsedSeconds;TimeSpan frozenSpan=watch.Elapsed;
                if(frozen<0l||frozenSeconds<0.0||frozenSpan.Ticks<0l)return 12;
                for(int i=0;i<100;i++){
                    long unused=Clock::GetTimestamp();
                    if(watch.ElapsedTicks!=frozen||watch.ElapsedSeconds!=frozenSeconds)return 13;
                    TimeSpan stopped=watch.Elapsed;if(stopped.Ticks!=frozenSpan.Ticks)return 14;
                }
                watch.Reset();if(watch.IsRunning||watch.ElapsedTicks!=0l||watch.ElapsedSeconds!=0.0)return 15;
                TimeSpan reset=watch.Elapsed;if(reset.Ticks!=0l)return 16;
                watch.Restart();if(!watch.IsRunning||watch.ElapsedTicks<0l)return 17;
                watch.Stop();if(watch.IsRunning||watch.ElapsedTicks<0l)return 18;
                Timer fresh=Timer::StartNew();if(!fresh.IsRunning)return 19;
                fresh.Stop();if(fresh.ElapsedTicks<0l||fresh.ElapsedSeconds<0.0)return 20;
                return 0;
            }
            """;
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(file=>new SourceFile(file,File.ReadAllText(file)))
            .Append(new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("platform-clock.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"{provider} exit {result.ExitCode}: {result.StandardError}");
        Assert.Equal("",result.StandardOutput);
        Assert.Equal("",result.StandardError);
    }
}
