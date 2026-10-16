using gflat.Projects;

namespace gflat;

public static class ProjectSdkInstallation
{
    public static int Run(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("gflat sdk install [--sdk-directory path]\nInstall Gflat.Sdk into the active .NET SDK's Sdks directory, or an explicit MSBuild SDK directory. This setup action may require administrator permissions.");
            return 0;
        }
        if (args.Length == 0 || args[0] != "install") throw new ArgumentException("Use gflat sdk install [--sdk-directory path].");
        string? sdkDirectory = null;
        if (args.Length == 3 && args[1] == "--sdk-directory") sdkDirectory = args[2];
        else if (args.Length != 1) throw new ArgumentException("Use gflat sdk install [--sdk-directory path].");
        if (sdkDirectory == null)
        {
            var version = NativeToolchain.Run("dotnet", ["--version"]);
            var installed = NativeToolchain.Run("dotnet", ["--list-sdks"]);
            if (version.ExitCode != 0 || installed.ExitCode != 0) throw new ArgumentException("Cannot locate the active .NET SDK. Pass --sdk-directory with the MSBuild host's Sdks directory.");
            string active = version.StandardOutput.Trim();
            string? line = installed.StandardOutput.Split('\n').FirstOrDefault(l => l.StartsWith(active + " [", StringComparison.Ordinal));
            if (line == null) throw new ArgumentException("Cannot locate the active .NET SDK. Pass --sdk-directory explicitly.");
            string root = line[(line.IndexOf('[') + 1)..line.LastIndexOf(']')];
            sdkDirectory = Path.Combine(root, active, "Sdks");
        }
        string destination = ProjectSdk.Install(ProjectCreation.FindToolchainRoot(), sdkDirectory);
        Console.WriteLine("Installed Gflat.Sdk: " + destination);
        Console.WriteLine("The SDK references this toolchain installation. Reinstall after moving it or adding another MSBuild/.NET SDK host.");
        return 0;
    }
}
