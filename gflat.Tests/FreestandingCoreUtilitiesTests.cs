using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace gflat.Tests;

public class FreestandingCoreUtilitiesTests
{
    private const string ProgramSource = """
        int main() {
            if (!std::Ascii::IsHexDigit('f')) return 1;
            if (std::Ascii::IsAscii((char)255)) return 2;
            char[64] input; char[64] decoded; char[32] encoded;
            nuint length = 0; nuint consumed = 0; uint scalar = 0u;
            if (!std::Utf8::TryEncode(0x1f600u, input, 64, &length)) return 3;
            if (!std::Utf8::TryDecode(input, length, &scalar, &consumed)) return 4;
            if (scalar != 0x1f600u || consumed != length) return 5;
            nuint encodedLength = 0; nuint decodedLength = 0;
            if (!std::Base64::TryEncode(input, length, encoded, 32, ref encodedLength)) return 6;
            if (!std::Base64::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength)) return 7;
            if (decodedLength != length) return 8;
            for (nuint i = 0; i < length; i++) if (input[i] != decoded[i]) return 9;
            if (!std::Hex::TryEncode(decoded, decodedLength, encoded, 32, ref encodedLength)) return 10;
            if (encodedLength != (nuint)8) return 11;
            readonly(char)* expected = "F09F9880";
            for (nuint i = 0; i < encodedLength; i++) if (encoded[i] != expected[i]) return 12;
            if (std::Ascii::ToLower(encoded[0]) != 'f') return 13;
            if (!std::Hex::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength)) return 14;
            if (!std::Utf8::IsValid(decoded, decodedLength)) return 15;
            if (std::BitOperations::PopCount(0xffu) != 8) return 16;
            if (std::BitOperations::RotateRight(1ul, 64) != 1ul) return 17;
            if (std::Hashing::Bytes("hello", 5) != (nuint)1335831723u) return 18;
            if (!std::PercentEncoding::TryEncode("a +", 3, encoded, 32, ref encodedLength) || encodedLength != (nuint)7) return 19;
            if (!std::PercentEncoding::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength) || decodedLength != (nuint)3) return 20;
            if (decoded[0] != 'a' || decoded[1] != ' ' || decoded[2] != '+') return 21;
            byte[9] binary; ulong integer = 0ul;
            if (!std::BinaryPrimitives::TryWriteUInt64BigEndian(&binary[1], 8, 0xfedcba9876543210ul)) return 22;
            if (!std::BinaryPrimitives::TryReadUInt64BigEndian(&binary[1], 8, ref integer) || integer != 0xfedcba9876543210ul) return 23;
            std::CRC32 crc = new(); crc.Append("123456789", 9);
            if (crc.GetValue() != 0xcbf43926u) return 24;
            std::Adler32 adler = new(); adler.Append("Wikipedia", 9);
            if (adler.GetValue() != 0x11e60398u) return 25;
            std::Utf8Decoder decoder = new();
            if (decoder.Read(input, 2, false, &scalar, &consumed) != std::Utf8Decoder::Status::NeedMore || consumed != 2) return 26;
            if (decoder.Read(&input[2], 2, true, &scalar, &consumed) != std::Utf8Decoder::Status::Scalar || scalar != 0x1f600u) return 27;
            if (decoder.Read(input, 0, true, &scalar, &consumed) != std::Utf8Decoder::Status::End) return 28;
            return 42;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StackOnlyCodecsRunWithoutLibcOrCppRuntime(int optimization)
    {
        // The test bootstrap below implements the x64 Windows/Linux process ABI.
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())) return;

        var sources = new[] { "Ascii.gf", "Utf8.gf", "Hex.gf", "Base64.gf", "BitOperations.gf", "Hashing.gf",
            "PercentEncoding.gf", "BinaryPrimitives.gf", "Checksums/CRC32.gf", "Checksums/Adler32.gf", "Utf8Decoder.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", ProgramSource));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "%Exception", "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free", "@__gflat_stdio_" })
            Assert.DoesNotContain(forbidden, ir);

        string directory = Path.Combine(Path.GetTempPath(), "gflat-core-bare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "core.ll");
            string output = Path.Combine(directory, "core.exe");
            var arguments = new List<string> { input, "-O" + optimization, "-nostdlib", "-fno-stack-protector", "-o", output };
            if (OperatingSystem.IsWindows())
            {
                ir += """

                    declare void @ExitProcess(i32) noreturn
                    define void @bare_entry() {
                        %status = call i32 @main()
                        call void @ExitProcess(i32 %status)
                        unreachable
                    }
                    """;
                arguments.AddRange(["-Xlinker", "/NODEFAULTLIB", "-Xlinker", "/ENTRY:bare_entry", "-Xlinker", "/SUBSYSTEM:CONSOLE", "-lkernel32"]);
                arguments.AddRange(NativeRuntime.HostArguments());
            }
            else
            {
                // Align the kernel-supplied stack before making an ordinary ABI call.
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
                arguments.AddRange([startup, "-static", "-no-pie", "-Wl,-e,bare_entry", "-Wl,--gc-sections"]);
            }
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Run(NativeToolchain.FindClang(), arguments, 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(output, []).ExitCode);
            byte[] binary = File.ReadAllBytes(output);
            if (OperatingSystem.IsWindows()) AssertOnlyWindowsExitImport(binary);
            else AssertNoElfDynamicLoader(binary);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void AssertOnlyWindowsExitImport(byte[] binary)
    {
        using var stream = new MemoryStream(binary, writable: false);
        using var pe = new PEReader(stream);
        Assert.Null(pe.PEHeaders.CorHeader);
        var import = pe.PEHeaders.PEHeader!.ImportTableDirectory;
        Assert.NotEqual(0, import.RelativeVirtualAddress);
        int Offset(int rva)
        {
            var section = Assert.Single(pe.PEHeaders.SectionHeaders.Where(s => rva >= s.VirtualAddress && rva - s.VirtualAddress < Math.Max(s.VirtualSize, s.SizeOfRawData)));
            return section.PointerToRawData + rva - section.VirtualAddress;
        }
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(offset, 4));
        string CString(int rva)
        {
            int offset = Offset(rva), end = Array.IndexOf(binary, (byte)0, offset);
            Assert.True(end >= offset);
            return Encoding.ASCII.GetString(binary, offset, end - offset);
        }
        int descriptor = Offset(import.RelativeVirtualAddress);
        Assert.Equal("KERNEL32.dll", CString((int)U32(descriptor + 12)), ignoreCase: true);
        Assert.Equal(0u, U32(descriptor + 20 + 12)); // No second imported DLL.
        int lookupRva = (int)U32(descriptor);
        if (lookupRva == 0) lookupRva = (int)U32(descriptor + 16);
        int lookup = Offset(lookupRva);
        ulong symbol = BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(lookup, 8));
        Assert.Equal(0ul, symbol & (1ul << 63)); // Named import, not an ordinal.
        Assert.Equal("ExitProcess", CString(checked((int)symbol + 2)));
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(lookup + 8, 8)));
    }

    private static void AssertNoElfDynamicLoader(byte[] binary)
    {
        Assert.Equal(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1 }, binary[..6]);
        ulong headers = BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(32, 8));
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(54, 2));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(56, 2));
        for (int i = 0; i < count; i++)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(checked((int)headers + i * size), 4));
            Assert.NotEqual(2u, type); // PT_DYNAMIC
            Assert.NotEqual(3u, type); // PT_INTERP
        }
    }
}
