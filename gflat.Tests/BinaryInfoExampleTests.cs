using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace gflat.Tests;

public sealed class BinaryInfoExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolInspectsSyntheticFormatsAndItsOwnHostImageWithBoundsFailures(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-binary-info-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("binary-info.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/binary-info.gf"))));
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "binary-info.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);

            var valid = new List<(string Name, byte[] Bytes, string Expected)>();
            byte[] pe = Pe();
            valid.Add(("pe", pe, PeOutput(pe.Length, false, "KERNEL32.dll", "sample.dll")));
            byte[] peNone = Pe(imports: false);
            valid.Add(("pe-none", peNone, PeOutput(peNone.Length, false)));
            byte[] peClr = Pe(clr: true);
            valid.Add(("pe-clr", peClr, PeOutput(peClr.Length, true, "KERNEL32.dll", "sample.dll")));
            byte[] peHeader = Pe(); U32(peHeader, 0x200 + 12, 0x1f0); Put(peHeader, 0x1f0, "header.dll\0"u8);
            valid.Add(("pe-header-name", peHeader, PeOutput(peHeader.Length, false, "header.dll", "sample.dll")));
            byte[] peHeaderDirectory = Pe(); peHeaderDirectory.AsSpan(0x200, 60).CopyTo(peHeaderDirectory.AsSpan(0x1b0)); U32(peHeaderDirectory, 0x98 + 120, 0x1b0);
            valid.Add(("pe-header-directory", peHeaderDirectory, PeOutput(peHeaderDirectory.Length, false, "KERNEL32.dll", "sample.dll")));
            byte[] peEscaped = Pe(); byte[] oddName = [65, 34, 92, 10, 255]; Put(peEscaped, 0x300, [.. oddName, 0]);
            valid.Add(("pe-escaped", peEscaped, PeBytesOutput(peEscaped.Length, false, oddName, "sample.dll"u8.ToArray())));
            // Disjoint adjacent ranges and an empty virtual range are permitted.
            byte[] peAdjacent = Pe(); U16(peAdjacent, 0x86, 2); PeSection(peAdjacent, 0x1b0, 0x1400, 0x500, 0x10);
            valid.Add(("pe-adjacent", peAdjacent, PeOutput(peAdjacent.Length, false, "KERNEL32.dll", "sample.dll")));
            byte[] peEmpty = Pe(); U16(peEmpty, 0x86, 2); PeSection(peEmpty, 0x1b0, 0x1100, 0, 0);
            valid.Add(("pe-empty-range", peEmpty, PeOutput(peEmpty.Length, false, "KERNEL32.dll", "sample.dll")));
            byte[] elf = Elf();
            valid.Add(("elf", elf, ElfOutput(elf.Length, "/lib64/ld-linux-x86-64.so.2", "libc.so.6", "libsample.so")));
            byte[] elfStatic = Elf(dynamic: false, interpreter: false);
            valid.Add(("elf-static-shaped", elfStatic, ElfOutput(elfStatic.Length, null)));
            byte[] elfShared = Elf(interpreter: false); U16(elfShared, 16, 3);
            valid.Add(("elf-shared", elfShared, ElfOutput(elfShared.Length, null, "libc.so.6", "libsample.so")));
            byte[] elfEscaped = Elf(); Put(elfEscaped, 0x301, [.. oddName, 0]);
            valid.Add(("elf-escaped", elfEscaped, ElfBytesOutput(elfEscaped.Length, "/lib64/ld-linux-x86-64.so.2", oddName, "libsample.so"u8.ToArray())));
            byte[] elfNoNeeded = Elf(); U64(elfNoNeeded, 0x260, 0);
            valid.Add(("elf-no-needed", elfNoNeeded, ElfOutput(elfNoNeeded.Length, "/lib64/ld-linux-x86-64.so.2")));
            byte[] elfAdjacent = Elf(); U16(elfAdjacent, 56, 4); ElfSegment(elfAdjacent, 64 + 3 * 56, 1, 0x500, 0x400600, 0x10);
            valid.Add(("elf-adjacent", elfAdjacent, ElfOutput(elfAdjacent.Length, "/lib64/ld-linux-x86-64.so.2", "libc.so.6", "libsample.so")));

            byte[] elfEmpty = Elf(); U16(elfEmpty, 56, 4); ElfSegment(elfEmpty, 64 + 3 * 56, 1, 0, 0x400310, 0);
            valid.Add(("elf-empty-range", elfEmpty, ElfOutput(elfEmpty.Length, "/lib64/ld-linux-x86-64.so.2", "libc.so.6", "libsample.so")));

            foreach (var fixture in valid)
                CheckFile(executable, directory, fixture.Name, fixture.Bytes, fixture.Expected);

            var invalid = new List<(string Name, byte[] Bytes)> {
                ("empty", []), ("zip", [80, 75, 3, 4]), ("mz-short", [77, 90, 0, 0])
            };
            void BadPe(string name, Action<byte[]> change) { byte[] image = Pe(); change(image); invalid.Add((name, image)); }
            void BadElf(string name, Action<byte[]> change) { byte[] image = Elf(); change(image); invalid.Add((name, image)); }
            BadPe("pe-header-offset", image => U32(image, 60, uint.MaxValue));
            BadPe("pe32", image => U16(image, 0x98, 0x10b));
            BadPe("pe-directories", image => U32(image, 0x98 + 108, 17));
            BadPe("pe-sections", image => U16(image, 0x86, 97));
            BadPe("pe-small-headers", image => U32(image, 0x98 + 60, 0x180));
            BadPe("pe-raw-offset", image => U32(image, 0x188 + 20, uint.MaxValue));
            BadPe("pe-import-pair", image => U32(image, 0x98 + 120, 0));
            BadPe("pe-import-terminator", image => U32(image, 0x98 + 124, 40));
            BadPe("pe-zero-fill-name", image => { U32(image, 0x188 + 8, 0x800); U32(image, 0x200 + 12, 0x1500); });
            BadPe("pe-name-terminator", image => image.AsSpan(0x300).Fill(255));
            BadPe("pe-partial-mapping-overlap", image => { U16(image, 0x86, 2); PeSection(image, 0x1b0, 0x1020, 0x500, 0x20); });
            BadPe("pe-name-tail-overlap", image => { U16(image, 0x86, 2); PeSection(image, 0x1b0, 0x1105, 0x500, 0x20); });
            BadPe("pe-header-mapping-overlap", image => { U16(image, 0x86, 2); PeSection(image, 0x1b0, 0x100, 0x500, 0x20); });
            BadPe("pe-clr-pair", image => U32(image, 0x98 + 224, 0x1180));
            BadPe("pe-clr-map", image => { U32(image, 0x98 + 224, 0x1500); U32(image, 0x98 + 228, 72); });
            BadElf("elf32", image => image[4] = 1);
            BadElf("elf-big-endian", image => image[5] = 2);
            BadElf("elf-version", image => U32(image, 20, 2));
            BadElf("elf-relocatable", image => U16(image, 16, 1));
            BadElf("elf-extended", image => U16(image, 56, ushort.MaxValue));
            BadElf("elf-count-limit", image => U16(image, 56, 257));
            BadElf("elf-table-offset", image => U64(image, 32, ulong.MaxValue));
            BadElf("elf-entry-size", image => U16(image, 54, 55));
            BadElf("elf-file-range", image => U64(image, 64 + 8, ulong.MaxValue));
            BadElf("elf-load-wrap", image => U64(image, 64 + 16, ulong.MaxValue));
            BadElf("elf-load-memory", image => U64(image, 64 + 40, 1));
            BadElf("elf-interpreter-terminator", image => image.AsSpan(0x200, 28).Fill(65));
            BadElf("elf-dynamic-size", image => U64(image, 64 + 2 * 56 + 32, 79));
            BadElf("elf-dynamic-terminator", image => U64(image, 0x280, 1));
            BadElf("elf-missing-string-address", image => U64(image, 0x240, 99));
            BadElf("elf-string-offset", image => U64(image, 0x268, 96));
            BadElf("elf-zero-fill-strings", image => { U64(image, 64 + 40, 0x1000); U64(image, 0x248, 0x400700); });
            BadElf("elf-name-terminator", image => image.AsSpan(0x301, 95).Fill(255));
            BadElf("elf-partial-mapping-overlap", image => { U16(image, 56, 4); ElfSegment(image, 64 + 3 * 56, 1, 0x500, 0x400320, 16); });
            BadElf("elf-duplicate-interpreter", image => { U16(image, 56, 4); ElfSegment(image, 64 + 3 * 56, 3, 0x200, 0, 27); });
            BadElf("elf-duplicate-dynamic", image => { U16(image, 56, 4); ElfSegment(image, 64 + 3 * 56, 2, 0x240, 0, 80); });
            foreach (var fixture in invalid) CheckFile(executable, directory, fixture.Name, fixture.Bytes, null);
            foreach (int length in new[] { 1, 15, 63, 127, 255, 511 })
                CheckFile(executable, directory, "pe-truncated-" + length, pe[..length], null);

            foreach (string[] arguments in new string[][] { [], ["one", "two"] })
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: binary-info <file>\n", Normalize(usage.StandardError));
            }
            var missing = NativeToolchain.Run(executable, [Path.Combine(directory, "missing.bin")], 30000);
            Assert.Equal(1, missing.ExitCode); Assert.StartsWith("binary-info: ", missing.StandardError);

            // Real host image, inspected as bytes; never execute any synthetic data file.
            byte[] hostImage = File.ReadAllBytes(executable);
            string expectedHost = OperatingSystem.IsWindows() ? HostPeOutput(hostImage) : HostElfOutput(hostImage);
            var own = NativeToolchain.Run(executable, [executable], 30000);
            Assert.True(own.ExitCode == 0, own.StandardError); Assert.Empty(own.StandardError);
            Assert.Equal(expectedHost, Normalize(own.StandardOutput));
            Assert.Equal(hostImage, File.ReadAllBytes(executable));
        }
        catch (Exception error) { error.Data["BinaryInfoExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ExistingOutputFailureControlChecksBufferedFlushAndReadOnlyStreamFailures(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-binary-info-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.elf"); File.WriteAllBytes(input, Elf());
            ExampleOutputFailure.AssertReportedWithArguments("binary-info", optimization,
                "binary-info: Console flush failed", "binary-info: Console write failed", input);
        }
        catch (Exception error) { error.Data["BinaryInfoOutputDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static void CheckFile(string executable, string directory, string name, byte[] bytes, string? expected)
    {
        string path = Path.Combine(directory, name + ".bin"); File.WriteAllBytes(path, bytes);
        var result = NativeToolchain.Run(executable, [path], 30000);
        if (expected is null) { Assert.Equal(1, result.ExitCode); Assert.StartsWith("binary-info: ", result.StandardError); }
        else { Assert.True(result.ExitCode == 0, result.StandardError); Assert.Empty(result.StandardError); Assert.Equal(expected, Normalize(result.StandardOutput)); }
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static byte[] Pe(bool imports = true, bool clr = false)
    {
        byte[] image = new byte[0x600]; Put(image, 0, "MZ"u8); U32(image, 60, 0x80); Put(image, 0x80, "PE\0\0"u8);
        U16(image, 0x84, 0x8664); U16(image, 0x86, 1); U16(image, 0x94, 240);
        U16(image, 0x98, 0x20b); U32(image, 0x98 + 60, 0x200); U32(image, 0x98 + 108, 16);
        PeSection(image, 0x188, 0x1000, 0x200, 0x400);
        if (imports)
        {
            U32(image, 0x98 + 120, 0x1000); U32(image, 0x98 + 124, 60);
            U32(image, 0x200 + 12, 0x1100); U32(image, 0x214 + 12, 0x1120);
            Put(image, 0x300, "KERNEL32.dll\0"u8); Put(image, 0x320, "sample.dll\0"u8);
        }
        if (clr) { U32(image, 0x98 + 224, 0x1180); U32(image, 0x98 + 228, 72); }
        return image;
    }

    private static void PeSection(byte[] image, int at, uint address, uint offset, uint size)
    { U32(image, at + 8, size); U32(image, at + 12, address); U32(image, at + 16, size); U32(image, at + 20, offset); }

    private static byte[] Elf(bool dynamic = true, bool interpreter = true)
    {
        byte[] image = new byte[0x600]; Put(image, 0, [127, 69, 76, 70, 2, 1, 1]); U16(image, 16, 2); U16(image, 18, 62);
        U32(image, 20, 1); U64(image, 32, 64); U16(image, 52, 64); U16(image, 54, 56);
        U16(image, 56, (ushort)(1 + (dynamic ? 1 : 0) + (interpreter ? 1 : 0)));
        ElfSegment(image, 64, 1, 0, 0x400000, (ulong)image.Length);
        int next = 120;
        if (interpreter) { Put(image, 0x200, "/lib64/ld-linux-x86-64.so.2\0"u8); ElfSegment(image, next, 3, 0x200, 0x400200, 28); next += 56; }
        if (dynamic)
        {
            ElfSegment(image, next, 2, 0x240, 0x400240, 80);
            U64(image, 0x240, 5); U64(image, 0x248, 0x400300); U64(image, 0x250, 10); U64(image, 0x258, 96);
            U64(image, 0x260, 1); U64(image, 0x268, 1); U64(image, 0x270, 1); U64(image, 0x278, 32);
            Put(image, 0x301, "libc.so.6\0"u8); Put(image, 0x320, "libsample.so\0"u8);
        }
        return image;
    }

    private static void ElfSegment(byte[] image, int at, uint type, ulong offset, ulong address, ulong size)
    { U32(image, at, type); U64(image, at + 8, offset); U64(image, at + 16, address); U64(image, at + 32, size); U64(image, at + 40, size); }
    private static void Put(byte[] image, int at, ReadOnlySpan<byte> bytes) => bytes.CopyTo(image.AsSpan(at));
    private static void U16(byte[] image, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(at), value);
    private static void U32(byte[] image, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at), value);
    private static void U64(byte[] image, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at), value);
    private static string Normalize(string text) => text.Replace("\r\n", "\n");
    private static string Quote(byte[] bytes) => "\"" + string.Concat(bytes.Select(value => value >= 32 && value < 127 && value is not (34 or 92) ? ((char)value).ToString() : "\\x" + value.ToString("x2"))) + "\"";
    private static string PeOutput(int length, bool clr, params string[] names) => PeBytesOutput(length, clr, names.Select(Encoding.ASCII.GetBytes).ToArray());
    private static string PeBytesOutput(int length, bool clr, params byte[][] names)
        => $"format: PE32+\nbytes: {length}\nmachine: 0x8664\nclr-directory: {(clr ? "present" : "absent")}\n" + (names.Length == 0 ? "import: none\n" : string.Concat(names.Select(name => "import: " + Quote(name) + "\n")));
    private static string ElfOutput(int length, string? interpreter, params string[] names) => ElfBytesOutput(length, interpreter, names.Select(Encoding.ASCII.GetBytes).ToArray());
    private static string ElfBytesOutput(int length, string? interpreter, params byte[][] names)
        => $"format: ELF64LE\nbytes: {length}\nmachine: 0x003e\ninterpreter: {(interpreter is null ? "absent" : Quote(Encoding.ASCII.GetBytes(interpreter)))}\n" + (names.Length == 0 ? "needed: none\n" : string.Concat(names.Select(name => "needed: " + Quote(name) + "\n")));

    private static string HostPeOutput(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var header = pe.PEHeaders.PEHeader!;
        var names = new List<byte[]>();
        int Offset(int rva)
        {
            if (rva < header.SizeOfHeaders) return rva;
            var section = Assert.Single(pe.PEHeaders.SectionHeaders, section => rva >= section.VirtualAddress && rva - section.VirtualAddress < section.SizeOfRawData);
            return checked(section.PointerToRawData + rva - section.VirtualAddress);
        }
        int table = header.ImportTableDirectory.RelativeVirtualAddress;
        if (table != 0)
            for (int at = Offset(table); ; at += 20)
            {
                if (image.AsSpan(at, 20).IndexOfAnyExcept((byte)0) < 0) break;
                int rva = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 12));
                int start = Offset(rva), end = Array.IndexOf(image, (byte)0, start); Assert.True(end >= start);
                names.Add(image[start..end]);
            }
        string expected = PeBytesOutput(image.Length, pe.PEHeaders.CorHeader is not null, names.ToArray());
        return expected.Replace("machine: 0x8664", "machine: 0x" + ((ushort)pe.PEHeaders.CoffHeader.Machine).ToString("x4"));
    }

    private static string HostElfOutput(byte[] image)
    {
        // This path runs only on a real non-Windows host; it is not a claim of local Linux execution.
        Assert.Equal(new byte[] { 127, 69, 76, 70, 2, 1 }, image[..6]);
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(18));
        int table = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(32)));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(56));
        var loads = new List<(ulong Address, ulong Count, int Offset)>();
        int dynamic = 0, dynamicSize = 0; string? interpreter = null;
        ulong Q(int at) => BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at));
        for (int i = 0; i < count; i++)
        {
            int entry = table + 56 * i; uint type = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(entry));
            int offset = checked((int)Q(entry + 8)), size = checked((int)Q(entry + 32));
            if (type == 1) loads.Add((Q(entry + 16), (ulong)size, offset));
            if (type == 2) { dynamic = offset; dynamicSize = size; }
            if (type == 3) { int end = Array.IndexOf(image, (byte)0, offset, size); Assert.True(end >= offset); interpreter = Encoding.ASCII.GetString(image, offset, end - offset); }
        }
        ulong strings = 0, stringSize = 0; var offsets = new List<ulong>();
        for (int at = dynamic; dynamic != 0 && at < dynamic + dynamicSize; at += 16)
        {
            ulong tag = Q(at), value = Q(at + 8); if (tag == 0) break;
            if (tag == 1) offsets.Add(value); if (tag == 5) strings = value; if (tag == 10) stringSize = value;
        }
        var names = new List<byte[]>();
        if (offsets.Count != 0)
        {
            var load = Assert.Single(loads, load => strings >= load.Address && strings - load.Address < load.Count);
            int start = checked(load.Offset + (int)(strings - load.Address));
            foreach (ulong offset in offsets) { int at = checked(start + (int)offset), end = Array.IndexOf(image, (byte)0, at, checked((int)(stringSize - offset))); Assert.True(end >= at); names.Add(image[at..end]); }
        }
        return ElfBytesOutput(image.Length, interpreter, names.ToArray()).Replace("machine: 0x003e", "machine: 0x" + machine.ToString("x4"));
    }
}
