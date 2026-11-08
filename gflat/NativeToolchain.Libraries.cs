using System.Text.RegularExpressions;

namespace gflat;

public static partial class NativeToolchain
{
    private static IEnumerable<string> ExportedSymbols(string ir)
    {
        foreach (Match match in Regex.Matches(ir, @"(?m)^define\s+(?!internal\b|private\b)[^@\r\n]*@(?<name>[A-Za-z_.$][A-Za-z0-9_.$]*)\("))
            yield return match.Groups["name"].Value;
    }

    public static ProcessResult CompileStaticLibrary(string irPath, string outputPath, string? clang = null,
        int optimizationLevel = 0, IReadOnlyList<ForeignTranslationUnit>? foreignUnits = null,
        int exceptionReserveBytes = gflat.Projects.ExceptionReserve.DefaultBytes)
    {
        gflat.Projects.OptimizationLevel.Parse(optimizationLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string compiler = FindClang(clang);
        string ir = File.ReadAllText(irPath);
        if (foreignUnits == null && File.Exists(irPath + ".foreign.json"))
            foreignUnits = System.Text.Json.JsonSerializer.Deserialize<ForeignTranslationUnit[]>(File.ReadAllText(irPath + ".foreign.json"));
        string scratch = Path.Combine(Path.GetTempPath(), "gflat_archive_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var objects = new List<string>();
            string program = Path.Combine(scratch, "program.o");
            var arguments = new List<string> { "-c", irPath, "-O" + optimizationLevel, "-o", program };
            if (OperatingSystem.IsLinux()) arguments.Add("-fPIC");
            ProcessResult result = Run(compiler, arguments, 60000);
            if (result.ExitCode != 0) return result;
            objects.Add(program);
            var usedBridges = LlvmForeignSymbols(ir);
            for (int index = 0; index < (foreignUnits?.Count ?? 0); index++)
            {
                var unit = foreignUnits![index];
                string source = Path.Combine(scratch, "foreign" + index + (unit.Language == "cpp" ? ".cpp" : ".c"));
                string obj = Path.Combine(scratch, "foreign" + index + ".o");
                File.WriteAllText(source, unit.Text + "\n" + string.Concat(unit.Bridges
                    .Where(bridge => usedBridges.Contains(bridge.Key)).Select(bridge => bridge.Value)));
                arguments = new(unit.Arguments);
                arguments.AddRange(NativeRuntime.HostCompilerArguments(unit.Language == "cpp"));
                arguments.AddRange(["-x", unit.Language == "cpp" ? "c++" : "c", "-c", source, "-O" + optimizationLevel,
                    "-ffunction-sections", "-fdata-sections", "-o", obj]);
                if (OperatingSystem.IsLinux()) arguments.Add("-fPIC");
                result = Run(compiler, arguments, 60000);
                if (result.ExitCode != 0) return result;
                objects.Add(obj);
            }
            var runtime = RequiredNativeRuntimes(ir);
            if (runtime.Exceptions) objects.Add(NativeRuntime.GetObject(compiler, exceptionReserveBytes));
            if (runtime.Stdio) objects.Add(NativeRuntime.GetStdioObject(compiler, cppHost: runtime.Exceptions));
            string archiverName = OperatingSystem.IsWindows() ? "llvm-ar.exe" : "llvm-ar";
            string archiver = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(compiler))!, archiverName);
            if (!File.Exists(archiver)) archiver = FindOnPath(archiverName) ??
                (OperatingSystem.IsLinux() ? FindOnPath("ar") : null) ?? throw new FileNotFoundException("A native archiver was not found beside Clang or on PATH.");
            string archive = Path.Combine(scratch, "library.a");
            result = Run(archiver, new[] { "rcs", archive }.Concat(objects), 60000);
            if (result.ExitCode == 0) File.Copy(archive, outputPath, overwrite: true);
            return result;
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
