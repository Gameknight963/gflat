namespace gflat.Tests;

public sealed class NativeRuntimeDiscoveryTests
{
    [Theory]
    [InlineData("14.9.99999", "14.44.35207")]
    [InlineData("14.44.9", "14.44.35207")]
    public void RuntimeHeadersAndLibrariesUseTheNewestNumericToolsetAcrossEditions(string older, string newer)
    {
        WithFixture(root =>
        {
            string enterprise = Path.Combine(root, "Enterprise");
            string community = Path.Combine(root, "Community");
            AddToolset(enterprise, older);
            string expected = AddToolset(community, newer);
            string libraryOnly = AddToolset(enterprise, "99.0.0", headers: false);
            AddToolset(enterprise, "not-a-toolset-version");
            var arguments = NativeRuntime.HostArguments([enterprise, community], Path.Combine(root, "missing-sdk"));
            Assert.Equal(MsvcArguments(expected), arguments);
            Assert.Equal(arguments, NativeRuntime.HostArguments([community, enterprise], Path.Combine(root, "missing-sdk")));
            // Bare native linking still needs only its existing runtime-library sentinel.
            Assert.Equal(Path.Combine(libraryOnly, "lib", "x64"), NativeToolchain.FindMsvcLibraryDirectory([community, enterprise]));
        });
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("new")]
    public void RuntimeSkipsAYoungerToolsetMissingARequiredCppHeader(string missing)
    {
        WithFixture(root =>
        {
            string expected = AddToolset(root, "14.44.35207");
            string incomplete = AddToolset(root, "15.0.0");
            File.Delete(Path.Combine(incomplete, "include", missing));
            Assert.Equal(MsvcArguments(expected), NativeRuntime.HostArguments([root], Path.Combine(root, "missing-sdk")));
        });
    }

    [Theory]
    [InlineData("10.0.9.0", "10.0.26100.0")]
    [InlineData("10.0.26100.9", "10.0.26100.99")]
    public void RuntimeUsesOneCompleteNumericallyNewestWindowsSdk(string older, string newer)
    {
        WithFixture(root =>
        {
            string kits = Path.Combine(root, "Windows Kits", "10");
            AddSdk(kits, older);
            AddSdk(kits, newer);
            AddSdk(kits, "not-an-sdk-version");
            Assert.Equal(SdkArguments(kits, newer), NativeRuntime.HostArguments([], kits));
        });
    }

    [Theory]
    [InlineData("Include", "ucrt", "stdio.h")]
    [InlineData("Include", "shared", "sdkddkver.h")]
    [InlineData("Include", "um", "windows.h")]
    [InlineData("Lib", "ucrt", "ucrt.lib")]
    [InlineData("Lib", "um", "kernel32.lib")]
    public void RuntimeSkipsSdkWithMissingHeadersOrMatchingX64Libraries(string kind, string part, string file)
    {
        WithFixture(root =>
        {
            string kits = Path.Combine(root, "Windows Kits", "10");
            const string expected = "10.0.26100.0", incomplete = "10.0.99999.0";
            AddSdk(kits, expected);
            AddSdk(kits, incomplete);
            string missing = Path.Combine(kits, kind, incomplete, part);
            if (kind == "Lib") missing = Path.Combine(missing, "x64");
            File.Delete(Path.Combine(missing, file));
            Assert.Equal(SdkArguments(kits, expected), NativeRuntime.HostArguments([], kits));
        });
    }

    [Fact]
    public void IncompleteOrAbsentRootsDoNotAddUnusableSearchPaths()
    {
        WithFixture(root =>
        {
            string installation = Path.Combine(root, "Community");
            string kits = Path.Combine(root, "Kits");
            AddToolset(installation, "14.44.35207", headers: false);
            Directory.CreateDirectory(Path.Combine(kits, "Include", "10.0.26100.0", "um"));
            Assert.Empty(NativeRuntime.HostArguments([installation, Path.Combine(root, "absent")], kits));
            Assert.Empty(NativeRuntime.HostArguments([], Path.Combine(root, "absent-sdk")));
        });
    }

    [Fact]
    public void SharedBoundedEnumerationIncludesProgramFilesAndOlderX86Installations()
    {
        WithFixture(root =>
        {
            string programFiles = Path.Combine(root, "Program Files");
            string programFilesX86 = Path.Combine(root, "Program Files (x86)");
            string modern = Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community");
            string older = Path.Combine(programFilesX86, "Microsoft Visual Studio", "2019", "BuildTools");
            AddToolset(modern, "14.2.0");
            string expected = AddToolset(older, "14.29.30133");
            // A nested installation-like subtree must not trigger an unbounded recursive scan.
            Directory.CreateDirectory(Path.Combine(modern, "nested", "another", "edition"));
            string[] installations = NativeToolchain.EnumerateVisualStudioInstallations([programFiles, programFilesX86, programFiles]).ToArray();
            Assert.Equal(2, installations.Length);
            Assert.Contains(modern, installations);
            Assert.Contains(older, installations);
            Assert.Equal(MsvcArguments(expected), NativeRuntime.HostArguments(installations, Path.Combine(root, "missing-sdk")));
        });
    }

    [Fact]
    public void StdioOnlyCanUseCHeadersWhileMixedProgramsKeepTheMatchedCppToolset()
    {
        WithFixture(root => {
            string cpp = AddToolset(root, "14.44.1");
            AddFile(Path.Combine(cpp, "include", "vcruntime.h"));
            AddFile(Path.Combine(cpp, "include", "vadefs.h"));
            string c = AddToolset(root, "15.0.1", headers: false);
            AddFile(Path.Combine(c, "include", "vcruntime.h"));
            AddFile(Path.Combine(c, "include", "vadefs.h"));
            AddToolset(root, "99.0.0", headers: false);
            string kits = Path.Combine(root, "missing-sdk");
            Assert.Equal(MsvcArguments(c), NativeRuntime.HostArguments([root], kits, requireCppHeaders: false));
            Assert.Equal(MsvcArguments(cpp), NativeRuntime.HostArguments([root], kits, requireCppHeaders: true));
            File.Delete(Path.Combine(c, "include", "vadefs.h"));
            Assert.Equal(MsvcArguments(cpp), NativeRuntime.HostArguments([root], kits, requireCppHeaders: false));
        });
    }

    private static string[] MsvcArguments(string toolset)
        => ["-isystem", Path.Combine(toolset, "include"), "-Xlinker", "/libpath:" + Path.Combine(toolset, "lib", "x64")];

    private static string[] SdkArguments(string kits, string version)
        => ["-isystem", Path.Combine(kits, "Include", version, "ucrt"),
            "-isystem", Path.Combine(kits, "Include", version, "shared"),
            "-isystem", Path.Combine(kits, "Include", version, "um"),
            "-Xlinker", "/libpath:" + Path.Combine(kits, "Lib", version, "ucrt", "x64"),
            "-Xlinker", "/libpath:" + Path.Combine(kits, "Lib", version, "um", "x64")];

    private static string AddToolset(string installation, string version, bool headers = true)
    {
        string directory = Path.Combine(installation, "VC", "Tools", "MSVC", version);
        AddFile(Path.Combine(directory, "lib", "x64", "libcmt.lib"));
        if (headers)
        {
            AddFile(Path.Combine(directory, "include", "exception"));
            AddFile(Path.Combine(directory, "include", "new"));
        }
        return directory;
    }

    private static void AddSdk(string kits, string version)
    {
        AddFile(Path.Combine(kits, "Include", version, "ucrt", "stdio.h"));
        AddFile(Path.Combine(kits, "Include", version, "shared", "sdkddkver.h"));
        AddFile(Path.Combine(kits, "Include", version, "um", "windows.h"));
        AddFile(Path.Combine(kits, "Lib", version, "ucrt", "x64", "ucrt.lib"));
        AddFile(Path.Combine(kits, "Lib", version, "um", "x64", "kernel32.lib"));
    }

    private static void AddFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "discovery fixture only");
    }

    private static void WithFixture(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-runtime-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        catch (Exception error)
        {
            error.Data["NativeRuntimeDiscoveryDirectory"] = root;
            throw;
        }
        Directory.Delete(root, recursive: true);
    }
}