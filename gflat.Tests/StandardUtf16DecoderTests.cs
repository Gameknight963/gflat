using System.Text;

namespace gflat.Tests;

public sealed class StandardUtf16DecoderTests
{
    private static void Run(string program, int optimization)
    {
        var sources = new[] { "Text/utf16.gf", "Text/Utf16Decoder.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "__gflat_eh", "@malloc", "@free", "gflat$Memory$", "std$Utf8" })
            Assert.DoesNotContain(forbidden, ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-utf16-decoder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "program.ll"), output = Path.Combine(directory, "program.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, [], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryScalarDecodesAtEveryCodeUnitSplit(int optimization)
    {
        Run("""
            int main() nothrow {
                std::Utf16Decoder decoder = new(); ushort[2] units;
                uint scalar = 99u; nuint consumed = 99; nuint length = 0;
                for (uint value = 0u; value <= 0x10ffffu; value++) {
                    if (value >= 0xd800u && value <= 0xdfffu) continue;
                    if (!std::Utf16::TryEncode(value, units, 2, &length)) return 1;
                    for (nuint split = 0; split <= length; split++) {
                        decoder.Reset();
                        std::Utf16Decoder::Status status = decoder.Read(units, split, false, &scalar, &consumed);
                        if (consumed != split) return 2;
                        if (split < length) {
                            if (status != std::Utf16Decoder::Status::NeedMore || scalar != 0u) return 3;
                            if (decoder.Read(units, 0, false, &scalar, &consumed) != std::Utf16Decoder::Status::NeedMore || scalar != 0u || consumed != 0) return 4;
                            status = decoder.Read(&units[split], length - split, true, &scalar, &consumed);
                            if (consumed != length - split) return 5;
                        }
                        if (status != std::Utf16Decoder::Status::Scalar || scalar != value) return 6;
                        if (decoder.Read(units, 0, true, &scalar, &consumed) != std::Utf16Decoder::Status::End || scalar != 0u || consumed != 0) return 7;
                        if (decoder.Read(units, length, false, &scalar, &consumed) != std::Utf16Decoder::Status::End || scalar != 0u || consumed != 0) return 8;
                    }
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedStreamMatchesDotNetRunesAtEveryChunkSize(int optimization)
    {
        const string text = "\0A\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\ufeff\U00010000\U0001f600\U0010ffffZ\0";
        uint[] expected = text.EnumerateRunes().Select(r => (uint)r.Value).ToArray();
        Run($$"""
            int main() nothrow {
                ushort[{{text.Length}}] units = [{{string.Join(",", text.Select(c => $"(ushort){(int)c}"))}}];
                uint[{{expected.Length}}] expected = [{{string.Join(",", expected.Select(s => $"{s}u"))}}];
                std::Utf16Decoder decoder = new(); uint scalar = 0u; nuint consumed = 0;
                for (nuint chunk = 1; chunk <= (nuint){{text.Length}}; chunk++) {
                    decoder.Reset(); nuint offset = 0; nuint index = 0;
                    while (offset < (nuint){{text.Length}}) {
                        nuint end = offset + chunk; if (end > (nuint){{text.Length}}) end = {{text.Length}};
                        while (offset < end) {
                            std::Utf16Decoder::Status status = decoder.Read(&units[offset], end - offset, end == (nuint){{text.Length}}, &scalar, &consumed);
                            if (consumed == 0 || consumed > end - offset) return 1;
                            offset += consumed;
                            if (status == std::Utf16Decoder::Status::Scalar) {
                                if (index >= (nuint){{expected.Length}} || scalar != expected[index]) return 2;
                                index++;
                            } else if (status != std::Utf16Decoder::Status::NeedMore || scalar != 0u || offset != end) return 3;
                        }
                    }
                    if (index != (nuint){{expected.Length}}) return 4;
                    if (decoder.Read(units, 0, true, &scalar, &consumed) != std::Utf16Decoder::Status::End) return 5;
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedPrefixesConsumeOffendingUnitThenRemainInvalid(int optimization)
    {
        ushort[][] cases = [[0xdc00], [0xdfff], [0xd800, 65], [0xdbff, 0xd800], [0xd800, 0xffff], [0xdbff, 0xdbff]];
        string checks = string.Join("\n", cases.Select(input => $$"""
            {
                ushort[{{input.Length + 1}}] units = [{{string.Join(",", input.Append((ushort)90).Select(u => $"(ushort){u}"))}}];
                for (nuint split = 0; split <= (nuint){{input.Length}}; split++) {
                    decoder.Reset();
                    std::Utf16Decoder::Status status = decoder.Read(units, split, false, &scalar, &consumed);
                    if (consumed != split || scalar != 0u) return 1;
                    if (split < (nuint){{input.Length}}) {
                        if (status != std::Utf16Decoder::Status::NeedMore) return 2;
                        status = decoder.Read(&units[split], (nuint){{input.Length + 1}} - split, true, &scalar, &consumed);
                        if (consumed != (nuint){{input.Length}} - split || scalar != 0u) return 3;
                    }
                    if (status != std::Utf16Decoder::Status::Invalid) return 4;
                    if (decoder.Read(units, {{input.Length + 1}}, true, &scalar, &consumed) != std::Utf16Decoder::Status::Invalid || scalar != 0u || consumed != 0) return 5;
                }
            }
            """));
        Run($$"""
            int main() nothrow {
                std::Utf16Decoder decoder = new(); uint scalar = 99u; nuint consumed = 99;
                {{checks}}
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FinalTruncationCopiedPartialStateResetAndEmptyChunksAreIndependent(int optimization)
    {
        Run("""
            int main() nothrow {
                ushort[3] units = [(ushort)0xd83d, (ushort)0xde00, (ushort)65];
                uint scalar = 99u; nuint consumed = 99; std::Utf16Decoder decoder = new();
                if (decoder.Read(units, 1, true, &scalar, &consumed) != std::Utf16Decoder::Status::Invalid || scalar != 0u || consumed != 1) return 1;
                decoder.Reset();
                if (decoder.Read(units, 1, false, &scalar, &consumed) != std::Utf16Decoder::Status::NeedMore || scalar != 0u || consumed != 1) return 2;
                std::Utf16Decoder copy = decoder;
                if (decoder.Read(units, 0, true, &scalar, &consumed) != std::Utf16Decoder::Status::Invalid || scalar != 0u || consumed != 0) return 3;
                if (copy.Read(&units[1], 1, true, &scalar, &consumed) != std::Utf16Decoder::Status::Scalar || scalar != 0x1f600u || consumed != 1) return 4;
                decoder.Reset(); units[1] = (ushort)0xde01;
                if (decoder.Read(units, 1, false, &scalar, &consumed) != std::Utf16Decoder::Status::NeedMore) return 5;
                std::Utf16Decoder other = decoder;
                if (decoder.Read(&units[1], 1, true, &scalar, &consumed) != std::Utf16Decoder::Status::Scalar || scalar != 0x1f601u) return 6;
                other.Reset();
                if (other.Read(&units[2], 1, true, &scalar, &consumed) != std::Utf16Decoder::Status::Scalar || scalar != 65u || consumed != 1) return 7;
                if (other.Read(units, 0, false, &scalar, &consumed) != std::Utf16Decoder::Status::NeedMore || scalar != 0u || consumed != 0) return 8;
                readonly(ushort)*? absent = null;
                if (other.Read(absent!, 0, true, &scalar, &consumed) != std::Utf16Decoder::Status::End || consumed != 0) return 9;
                if (other.Read(units, 3, false, &scalar, &consumed) != std::Utf16Decoder::Status::End || scalar != 0u || consumed != 0) return 10;
                return 0;
            }
            """, optimization);
    }

    [Fact]
    public void DecoderSourceDoesNotRequireStaticCodecsOrAnyOtherLibrarySource()
    {
        Compiler.Emit([new SourceFile("Utf16Decoder.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/Utf16Decoder.gf"))),
            new SourceFile("main.gf", "int main() nothrow { std::Utf16Decoder decoder = new(); return 0; }")], options: new() { NoStdLib = true });
    }
}
