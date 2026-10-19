using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace gflat.Tests;

public class FreestandingCoreUtilitiesTests
{
    private const string ProgramSource = """
        extern ulong formatter_input() nothrow;
        extern void byte_search_input(byte* data) nothrow;
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
            byte[10] packet; packet[0]=(byte)165; packet[9]=(byte)90;
            std::BinaryBufferWriter writer = new(&packet[1],8);
            std::BinaryBufferWriter payload = new();
            if (!writer.TryWriteUInt16BigEndian((ushort)4) || !writer.TryWriteSlice(4,ref payload)) return 29;
            if (!payload.TryWriteInt32LittleEndian(-42) || payload.TryWriteByte((byte)0)) return 30;
            if (!writer.TryWriteUInt16BigEndian((ushort)48879) || writer.Position!=8 || writer.Remaining!=0) return 31;
            if (packet[0]!=(byte)165 || packet[9]!=(byte)90 || packet[1]!=(byte)0 || packet[2]!=(byte)4 || packet[3]!=(byte)214 || packet[4]!=(byte)255 || packet[7]!=(byte)190 || packet[8]!=(byte)239) return 32;
            std::BinaryBufferReader reader = new(&packet[1],8);
            std::BinaryBufferReader part = new(); ushort field=0; int signedValue=0; byte extra=(byte)99;
            if (!reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)4 || !reader.TryReadSlice((nuint)field,ref part)) return 33;
            if (!part.TryReadInt32LittleEndian(ref signedValue) || signedValue!=-42 || part.TryReadByte(ref extra) || extra!=(byte)0) return 34;
            if (!reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)48879 || reader.Position!=8 || reader.Remaining!=0) return 35;
            if (reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)0 || reader.Position!=8) return 36;
            if (packet[0]!=(byte)165 || packet[9]!=(byte)90) return 37;
            ushort[7] unicode = [(ushort)65, (ushort)0, (ushort)0x20ac, (ushort)0xd83d, (ushort)0xde00, (ushort)0xfeff, (ushort)0xffff];
            nuint unicodeBytes = 0; nuint unicodeUnits = 0; nuint unicodeScalars = 0;
            if (!std::Utf16::TryCount(unicode, 7, &unicodeScalars) || unicodeScalars != 6) return 38;
            if (!std::UnicodeTranscoding::TryGetUtf8Length(unicode, 7, ref unicodeBytes) || unicodeBytes != 15) return 39;
            if (!std::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, input, 64, ref length) || length != 15) return 40;
            if (input[0] != 'A' || input[1] != '\0' || (byte)input[2] != (byte)0xe2 || (byte)input[5] != (byte)0xf0) return 41;
            if (!std::UnicodeTranscoding::TryGetUtf16Length(input, length, ref unicodeUnits) || unicodeUnits != 7) return 43;
            ushort[9] converted;
            for (nuint i = 0; i < 9; i++) converted[i] = (ushort)123;
            if (!std::UnicodeTranscoding::TryUtf8ToUtf16(input, length, &converted[1], 7, ref unicodeUnits) || unicodeUnits != 7) return 44;
            if (converted[0] != (ushort)123 || converted[8] != (ushort)123) return 45;
            for (nuint i = 0; i < 7; i++) if (unicode[i] != converted[i + 1]) return 46;
            for (nuint i = 0; i < 64; i++) decoded[i] = 'Z';
            if (std::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, decoded, 14, ref decodedLength) || decodedLength != 0) return 47;
            for (nuint i = 0; i < 64; i++) if (decoded[i] != 'Z') return 48;
            unicode[6] = (ushort)0xd800;
            if (std::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, decoded, 64, ref decodedLength) || decodedLength != 0) return 49;
            for (nuint i = 0; i < 64; i++) if (decoded[i] != 'Z') return 50;
            for (nuint i = 0; i < 9; i++) converted[i] = (ushort)123;
            if (std::UnicodeTranscoding::TryUtf8ToUtf16(input, length, &converted[1], 6, ref unicodeUnits) || unicodeUnits != 0) return 51;
            for (nuint i = 0; i < 9; i++) if (converted[i] != (ushort)123) return 52;
            input[length] = (char)255;
            if (std::UnicodeTranscoding::TryUtf8ToUtf16(input, length + 1, &converted[1], 7, ref unicodeUnits) || unicodeUnits != 0) return 53;
            for (nuint i = 0; i < 9; i++) if (converted[i] != (ushort)123) return 54;
            char[22] decimalText;
            for (nuint i = 0; i < 22; i++) decimalText[i] = 'Z';
            ulong runtimeInput = formatter_input();
            if (!std::IntegerFormatting::TryFormat((long)runtimeInput, &decimalText[1], 20, ref length) || length != 20) return 55;
            expected = "-9223372036854775808";
            for (nuint i = 0; i < length; i++) if (decimalText[i + 1] != expected[i]) return 56;
            if (decimalText[0] != 'Z' || decimalText[21] != 'Z') return 57;
            ulong unsignedMaximum = runtimeInput + runtimeInput - 1ul;
            if (!std::IntegerFormatting::TryFormat(unsignedMaximum, &decimalText[1], 20, ref length) || length != 20) return 58;
            expected = "18446744073709551615";
            for (nuint i = 0; i < length; i++) if (decimalText[i + 1] != expected[i]) return 59;
            if (decimalText[0] != 'Z' || decimalText[21] != 'Z') return 60;
            if (std::IntegerFormatting::TryFormat(unsignedMaximum, &decimalText[1], 19, ref length) || length != 0) return 61;
            for (nuint i = 0; i < 20; i++) if (decimalText[i + 1] != expected[i]) return 62;
            byte[64] searchInput; byte[5] searchPattern;
            byte_search_input((byte*)searchInput);
            searchPattern[0]=(byte)1;searchPattern[1]=(byte)0;searchPattern[2]=(byte)1;
            searchPattern[3]=(byte)0;searchPattern[4]=(byte)1;
            if(std::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=0) return 63;
            if(std::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=58) return 64;
            searchPattern[2]=(byte)2;
            if(std::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=64) return 65;
            if(std::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=64) return 66;
            if(std::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,0)!=0) return 67;
            if(std::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,0)!=64) return 68;
            char* cText=(char*)searchInput;
            if(std::CString::Length(cText)!=1) return 69;
            if(std::CString::Compare(cText,&cText[2])!=0 || !std::CString::Equals(cText,&cText[2])) return 70;
            if(!std::CString::StartsWith(cText,&cText[2]) || !std::CString::EndsWith(cText,&cText[2])) return 71;
            length=99;
            if(std::CString::TryGetLength(cText,1,ref length) || length!=0) return 72;
            if(!std::CString::TryGetLength(cText,2,ref length) || length!=1) return 73;
            searchInput[1]=(byte)2;searchInput[2]=(byte)0;
            if(std::CString::Length(cText)!=2 || std::CString::Compare(cText,&cText[2])!=1) return 74;
            if(std::CString::Equals(cText,&cText[2]) || std::CString::EndsWith(cText,"x")) return 75;
            if(!std::CString::EndsWith(cText,&cText[1]) || !std::CString::StartsWith(cText,&cText[2])) return 76;
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

        var sources = new[] { "Ascii.gf", "Utf8.gf", "Utf16.gf", "UnicodeTranscoding.gf", "Hex.gf", "Base64.gf", "BitOperations.gf", "Hashing.gf",
            "PercentEncoding.gf", "BinaryPrimitives.gf", "Checksums/CRC32.gf", "Checksums/Adler32.gf", "Utf8Decoder.gf",
            "BinaryBufferReader.gf", "BinaryBufferWriter.gf", "IntegerFormatting.gf", "ByteSearch.gf", "CString.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", ProgramSource));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "%Exception", "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free", "@__gflat_stdio_" })
            Assert.DoesNotContain(forbidden, ir);

        string directory = Path.Combine(Path.GetTempPath(), "gflat-core-bare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "core.ll");
            string output = Path.Combine(directory, "core.exe");
            var arguments = new List<string> { input, "-O" + optimization, "-nostdlib", "-fno-stack-protector", "-o", output };
            // Keep actual formatter inputs opaque to LLVM. A separate assembly
            // object prevents constant propagation from erasing the digit/copy loops.
            string formatterInput = Path.Combine(directory, "formatter-input.S");
            File.WriteAllText(formatterInput, """
                .text
                .global formatter_input
                formatter_input:
                    movabs $0x8000000000000000, %rax
                    ret
                """);
            // Populate search bytes outside LLVM's module, retaining real search
            // loops at O2 instead of merely checking constant-folded answers.
            string inputRegister = OperatingSystem.IsWindows() ? "%rcx" : "%rdi";
            File.AppendAllText(formatterInput, $$"""

                .global byte_search_input
                byte_search_input:
                    mov {{inputRegister}}, %r10
                    xor %eax, %eax
                .Lsearch_fill:
                    movb $1, (%r10,%rax)
                    inc %rax
                    movb $0, (%r10,%rax)
                    inc %rax
                    cmp $64, %rax
                    jne .Lsearch_fill
                    ret
                """);
            if (OperatingSystem.IsLinux()) File.AppendAllText(formatterInput, "\n.section .note.GNU-stack,\"\",@progbits\n");
            arguments.Add(formatterInput);
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
