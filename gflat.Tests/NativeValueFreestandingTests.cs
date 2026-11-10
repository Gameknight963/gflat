using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace gflat.Tests;

public sealed class NativeValueFreestandingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeValueLifetimesLinkWithoutCOrCppRuntimeLibraries(int optimization)
    {
        const string code = """
            #lang cpp
            int live = 0;
            class Cell {
                int value;
                const int* self;
            public:
                explicit Cell(int input) noexcept : value(input), self(&value) { ++live; }
                Cell(Cell&& source) noexcept : value(source.value), self(&value) { ++live; }
                ~Cell() noexcept { --live; }
                int Read() const noexcept { return self == &value ? value : -999; }
            };
            int Live() noexcept { return live; }
            #lang gflat
            int main() {
                {
                    Cell first = new Cell(42);
                    Cell second = move first;
                    if (second.Read() != 42 || Live() != 1) return 1;
                }
                if (Live() != 0) return 2;
                return 42;
            }
            """;
        var options = new CompilationOptions {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true,
            ForeignCompilerResolver = () => NativeToolchain.FindClang(),
            ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
                .Concat(new[] { "-ffreestanding", "-fno-exceptions", "-fno-rtti" }).ToArray()
        };
        var program = Compiler.EmitProgram([new SourceFile("native-freestanding.gf", code)], options: options);
        Assert.DoesNotContain("__gflat_eh_", program.Ir);
        Assert.DoesNotContain("__gflat_stdio_", program.Ir);
        string directory = Directory.CreateTempSubdirectory("gflat-native-bare-").FullName;
        try
        {
            string ir = Path.Combine(directory, "program.ll"), archive = Path.Combine(directory, "program.a");
            string output = Path.Combine(directory, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.CompileStaticLibrary(ir, archive, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var arguments = new List<string> { "-nostdlib", "-fno-stack-protector", "-o", output };
            if (OperatingSystem.IsWindows())
            {
                string startup = Path.Combine(directory, "start.ll");
                File.WriteAllText(startup, """
                    declare i32 @main()
                    declare void @gflat_test_fault_initialize()
                    declare i1 @__gflat_test_fault_handling_ready()
                    declare void @ExitProcess(i32) noreturn
                    define void @bare_entry() {
                        call void @gflat_test_fault_initialize()
                        %ready = call i1 @__gflat_test_fault_handling_ready()
                        br i1 %ready, label %run, label %unavailable
                    run:
                        %status = call i32 @main()
                        call void @ExitProcess(i32 %status)
                        unreachable
                    unavailable:
                        call void @ExitProcess(i32 99)
                        unreachable
                    }
                    """);
                arguments.Add(startup);
                arguments.AddRange(NativeTestFaultHandling.LinkInputs.Libraries.Select(library => library.Value));
                arguments.AddRange([archive, "-Xlinker", "/NODEFAULTLIB", "-Xlinker", "/ENTRY:bare_entry",
                    "-Xlinker", "/SUBSYSTEM:CONSOLE", "-lkernel32"]);
                arguments.AddRange(NativeRuntime.HostArguments());
            }
            else
            {
                string startup = Path.Combine(directory, "start.S");
                File.WriteAllText(startup, """
                    .text
                    .global bare_entry
                    bare_entry:
                        xor %ebp, %ebp
                        and $-16, %rsp
                        call main
                        mov %eax, %edi
                        mov $60, %eax
                        syscall
                    .section .note.GNU-stack,"",@progbits
                    """);
                arguments.AddRange([startup, archive, "-static", "-no-pie", "-Wl,-e,bare_entry", "-Wl,--gc-sections"]);
            }
            var linked = NativeToolchain.Run(NativeToolchain.FindClang(), arguments, 60000);
            Assert.True(linked.ExitCode == 0, linked.StandardError);
            Assert.Equal(42, NativeToolchain.Run(output, []).ExitCode);
            byte[] binary = File.ReadAllBytes(output);
            if (OperatingSystem.IsWindows())
            {
                using var pe = new PEReader(new MemoryStream(binary, writable: false));
                Assert.Null(pe.PEHeaders.CorHeader);
                int Offset(int rva) {
                    var section = Assert.Single(pe.PEHeaders.SectionHeaders.Where(section => rva >= section.VirtualAddress &&
                        rva - section.VirtualAddress < Math.Max(section.VirtualSize, section.SizeOfRawData)));
                    return section.PointerToRawData + rva - section.VirtualAddress;
                }
                int descriptor = Offset(pe.PEHeaders.PEHeader!.ImportTableDirectory.RelativeVirtualAddress);
                for (; BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(descriptor + 12, 4)) is var name && name != 0; descriptor += 20)
                {
                    int start = Offset((int)name), end = Array.IndexOf(binary, (byte)0, start);
                    Assert.True(end > start);
                    Assert.Equal("KERNEL32.dll", Encoding.ASCII.GetString(binary, start, end - start), ignoreCase: true);
                }
            }
            else
            {
                int offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(32, 8)));
                int size = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(54, 2));
                int count = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(56, 2));
                for (int index = 0; index < count; index++)
                    Assert.DoesNotContain(BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(offset + size * index, 4)), new uint[] { 2, 3 });
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
