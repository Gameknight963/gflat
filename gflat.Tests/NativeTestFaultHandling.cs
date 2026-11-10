using System.Runtime.InteropServices;
using gflat.Projects;

namespace gflat.Tests;

internal static class NativeTestFaultHandling
{
    private const uint QuietErrorMode = 3;
    private const uint AlwaysShowReportingUi = 16;
    private const uint NoReportingUi = 32;
    private const int MissingReportingPolicy = unchecked((int)0x80070490);
    private static readonly Lazy<NativeLinkInputs> inputs = new(CreateInputs);
    internal static NativeLinkInputs LinkInputs => inputs.Value;

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();
    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")]
    private static extern int WerGetFlags(nint process, out uint flags);
    [DllImport("kernel32.dll")]
    private static extern int WerSetFlags(uint flags);

    internal static void InitializeHost()
    {
        if (!OperatingSystem.IsWindows()) return;
        SetErrorMode(GetErrorMode() | QuietErrorMode);
        int status = WerGetFlags(GetCurrentProcess(), out uint flags);
        // A process without an existing WER policy reports ERROR_NOT_FOUND.
        if (status == MissingReportingPolicy) flags = 0;
        else Marshal.ThrowExceptionForHR(status);
        Marshal.ThrowExceptionForHR(WerSetFlags((flags & ~AlwaysShowReportingUi) | NoReportingUi));
    }

    private static NativeLinkInputs CreateInputs()
    {
        if (!OperatingSystem.IsWindows()) return NativeLinkInputs.Empty;
        string directory = Directory.CreateTempSubdirectory("gflat-test-fault-handling-").FullName;
        string source = Path.Combine(directory, "fault-handling.ll");
        string output = Path.Combine(directory, "fault-handling.obj");
        File.WriteAllText(source, WindowsInitializer);
        var result = NativeToolchain.Run(NativeToolchain.FindClang(), ["-c", source, "-o", output]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Cannot build noninteractive test fault handling: {result.StandardError}");
        return new NativeLinkInputs([new("File", output)], []);
    }

    // Install in the child before ordinary global constructors. Inheritance of
    // the test host's error mode alone does not establish the child's WER policy.
    // Unhandled hardware faults retain their Windows status as the exit code.
    private const string WindowsInitializer = """
        target triple = "x86_64-pc-windows-msvc"
        declare dllimport i32 @GetErrorMode()
        declare dllimport i32 @SetErrorMode(i32)
        declare dllimport i8* @GetCurrentProcess()
        declare dllimport i32 @WerGetFlags(i8*, i32*)
        declare dllimport i32 @WerSetFlags(i32)
        declare dllimport i8* @SetUnhandledExceptionFilter(i8*)
        declare dllimport void @ExitProcess(i32) noreturn

        @llvm.global_ctors = appending global [1 x { i32, void ()*, i8* }]
            [{ i32, void ()*, i8* } { i32 65534, void ()* @gflat_test_fault_initialize, i8* null }]

        define internal i32 @gflat_test_fault_exit(i8* %information) {
            %record_address = bitcast i8* %information to i8**
            %record = load i8*, i8** %record_address
            %code_address = bitcast i8* %record to i32*
            %code = load i32, i32* %code_address
            call void @ExitProcess(i32 %code)
            unreachable
        }

        define void @gflat_test_fault_initialize() {
            %mode = call i32 @GetErrorMode()
            %quiet = or i32 %mode, 3
            %old_mode = call i32 @SetErrorMode(i32 %quiet)
            %process = call i8* @GetCurrentProcess()
            %flags = alloca i32
            store i32 0, i32* %flags
            %get = call i32 @WerGetFlags(i8* %process, i32* %flags)
            %get_failed = icmp slt i32 %get, 0
            %not_found = icmp eq i32 %get, -2147023728
            %other_error = xor i1 %not_found, true
            %cannot_configure = and i1 %get_failed, %other_error
            br i1 %cannot_configure, label %get_failed_exit, label %configure
        configure:
            %stored = load i32, i32* %flags
            %current = select i1 %not_found, i32 0, i32 %stored
            %without_ui = and i32 %current, -17
            %no_ui = or i32 %without_ui, 32
            %set = call i32 @WerSetFlags(i32 %no_ui)
            %set_failed = icmp slt i32 %set, 0
            br i1 %set_failed, label %set_failed_exit, label %handler
        handler:
            %previous = call i8* @SetUnhandledExceptionFilter(i8* bitcast (i32 (i8*)* @gflat_test_fault_exit to i8*))
            ret void
        get_failed_exit:
            call void @ExitProcess(i32 %get)
            unreachable
        set_failed_exit:
            call void @ExitProcess(i32 %set)
            unreachable
        }

        define i1 @__gflat_test_fault_handling_ready() {
            %mode = call i32 @GetErrorMode()
            %bits = and i32 %mode, 3
            %mode_ready = icmp eq i32 %bits, 3
            %process = call i8* @GetCurrentProcess()
            %flags = alloca i32
            store i32 0, i32* %flags
            %get = call i32 @WerGetFlags(i8* %process, i32* %flags)
            %get_ok = icmp sge i32 %get, 0
            %current = load i32, i32* %flags
            %ui = and i32 %current, 48
            %wer_ready = icmp eq i32 %ui, 32
            %filter = call i8* @SetUnhandledExceptionFilter(i8* null)
            %ignored = call i8* @SetUnhandledExceptionFilter(i8* %filter)
            %filter_ready = icmp eq i8* %filter, bitcast (i32 (i8*)* @gflat_test_fault_exit to i8*)
            %a = and i1 %mode_ready, %get_ok
            %b = and i1 %wer_ready, %filter_ready
            %ready = and i1 %a, %b
            ret i1 %ready
        }
        """;
}
