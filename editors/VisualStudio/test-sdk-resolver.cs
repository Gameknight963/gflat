using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

internal static class Program
{
    private static string sdkPath = "";

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            string name = new AssemblyName(request.Name).Name + ".dll";
            string path = Path.Combine(args[0], name);
            if (!File.Exists(path)) path = Path.Combine(args[1], name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run(args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        try
        {
            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.GetFullPath(Path.Combine(args[0], "MSBuild.exe")));
            Console.WriteLine("Engine: " + typeof(ProjectCollection).Assembly.Location + "; bits=" + (IntPtr.Size * 8));
            string root = Path.Combine(args[2], "work", Guid.NewGuid().ToString("N"));
            sdkPath = Path.Combine(root, "private sdk", "Gflat.Sdk", "Sdk");
            Directory.CreateDirectory(sdkPath);
            string standardSdks = Path.Combine(root, "standard SDK directory");
            string otherSdk = Path.Combine(standardSdks, "Other.Sdk", "Sdk");
            Directory.CreateDirectory(otherSdk);
            File.WriteAllText(Path.Combine(otherSdk, "Sdk.props"), "<Project><PropertyGroup><OtherSdk>loaded</OtherSdk></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(otherSdk, "Sdk.targets"), "<Project />");
            Environment.SetEnvironmentVariable("MSBuildSDKsPath", standardSdks);
            File.WriteAllText(Path.Combine(sdkPath, "Sdk.props"), "<Project><PropertyGroup><ProbeProps>loaded</ProbeProps></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(sdkPath, "Sdk.targets"), "<Project><Target Name=\"Probe\"><Error Condition=\"'$(ProbeProps)' != 'loaded'\" Text=\"SDK props missing\" /></Target></Project>");
            string project = Path.Combine(root, "probe.gfproj");
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\" />");
            string other = Path.Combine(root, "other.proj");
            File.WriteAllText(other, "<Project Sdk=\"Unrelated.Missing.Sdk\" />");
            RequireFailure(project);
            Console.WriteLine("PASS: SDK unresolved before hook");
            using (IDisposable hook = Install(args[1]))
            {
                using (var projects = new ProjectCollection())
                {
                    Project loaded = projects.LoadProject(project);
                    if (loaded.GetPropertyValue("ProbeProps") != "loaded" || !loaded.Build("Probe")) throw new Exception("Private SDK evaluation/build failed");
                    projects.UnloadAllProjects();
                }
                Console.WriteLine("PASS: private SDK resolves and builds through real MSBuild");
                Parallel.For(0, 32, _ =>
                {
                    using (var projects = new ProjectCollection())
                    {
                        if (projects.LoadProject(project).GetPropertyValue("ProbeProps") != "loaded") throw new Exception("Concurrent evaluation failed");
                        projects.UnloadAllProjects();
                    }
                });
                Console.WriteLine("PASS: concurrent evaluations");
                string standardProject = Path.Combine(root, "standard.proj");
                File.WriteAllText(standardProject, "<Project Sdk=\"Other.Sdk\" />");
                using (var projects = new ProjectCollection())
                {
                    if (projects.LoadProject(standardProject).GetPropertyValue("OtherSdk") != "loaded") throw new Exception("Hook broke normal SDK resolution");
                }
                Console.WriteLine("PASS: unrelated installed SDK resolves through original resolver");
                string versioned = Path.Combine(root, "versioned.proj");
                File.WriteAllText(versioned, "<Project Sdk=\"Gflat.Sdk/99.0.0\" />");
                RequireFailure(versioned);
                Console.WriteLine("PASS: explicit SDK version is rejected");
                File.WriteAllText(Path.Combine(root, "childA.gfproj"), "<Project Sdk=\"Gflat.Sdk\" />");
                File.WriteAllText(Path.Combine(root, "childB.gfproj"), "<Project Sdk=\"Gflat.Sdk\" />");
                string nestedProject = Path.Combine(root, "nested.gfproj");
                File.WriteAllText(nestedProject, "<Project Sdk=\"Gflat.Sdk\"><Target Name=\"Nested\"><MSBuild Projects=\"childA.gfproj;childB.gfproj\" Targets=\"Probe\" BuildInParallel=\"true\" /></Target></Project>");
                using (var projects = new ProjectCollection())
                using (var builds = new BuildManager())
                {
                    var request = new BuildRequestData(projects.LoadProject(nestedProject).CreateProjectInstance(), new[] { "Nested" });
                    var options = new BuildParameters(projects)
                    {
                        DisableInProcNode = true, MaxNodeCount = 2, EnableNodeReuse = false,
                        Loggers = new[] { new ConsoleLogger(LoggerVerbosity.Minimal) }
                    };
                    BuildResult buildResult = builds.Build(options, request);
                    if (buildResult.OverallResult != BuildResultCode.Success) throw new Exception("Out-of-process project reference build failed", buildResult.Exception);
                    projects.UnloadAllProjects();
                }
                Console.WriteLine("PASS: out-of-process workers resolve child-project SDKs through host hook");
                RequireFailure(other);

                Console.WriteLine("PASS: unrelated SDK forwarded to original resolver");
            }
            RequireFailure(project);
            Console.WriteLine("PASS: disposing hook restores original resolution");

            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static IDisposable Install(string packageDirectory)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(packageDirectory, "gflat.VisualStudio.dll"));
        var resolver = assembly.GetType("gflat.VisualStudio.GflatSdkResolver", true);
        return (IDisposable)Activator.CreateInstance(resolver, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { sdkPath }, null);
    }

    private static void RequireFailure(string path)
    {
        using (var projects = new ProjectCollection())
        {
            try { projects.LoadProject(path); }
            catch (InvalidProjectFileException error) when (error.ErrorCode == "MSB4236") { return; }
            finally { projects.UnloadAllProjects(); }
        }
        throw new Exception("Expected unresolved SDK: " + path);
    }
}
