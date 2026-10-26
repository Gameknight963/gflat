using System.Text;

namespace gflat.Tests;

public class StandardUtf8DecoderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryUnicodeScalarDecodesAtEverySplitWithoutRuntimeDependencies(int optimization)
    {
        Run("""
            int main() {
                std::Utf8Decoder decoder = new();
                char[4] bytes; uint scalar = 0u; nuint consumed = 0; nuint length = 0;
                for (uint value = 0u; value <= 0x10ffffu; value++) {
                    if (value >= 0xd800u && value <= 0xdfffu) continue;
                    if (!std::Utf8::TryEncode(value, bytes, 4, &length)) return 1;
                    for (nuint split = 0; split <= length; split++) {
                        decoder.Reset();
                        std::Utf8Decoder::Status status = decoder.Read(bytes, split, false, &scalar, &consumed);
                        if (consumed != split) return 2;
                        if (split < length) {
                            if (status != std::Utf8Decoder::Status::NeedMore || scalar != 0u) return 3;
                            // Empty chunks preserve a partially decoded scalar.
                            if (decoder.Read(bytes, 0, false, &scalar, &consumed) != std::Utf8Decoder::Status::NeedMore || consumed != 0 || scalar != 0u) return 4;
                            status = decoder.Read(&bytes[split], length - split, true, &scalar, &consumed);
                            if (consumed != length - split) return 5;
                        }
                        if (status != std::Utf8Decoder::Status::Scalar || scalar != value) return 6;
                        if (decoder.Read(bytes, 0, true, &scalar, &consumed) != std::Utf8Decoder::Status::End || scalar != 0u || consumed != 0) return 7;
                        // The completed decoder consumes no bytes until explicitly reset.
                        if (decoder.Read(bytes, length, false, &scalar, &consumed) != std::Utf8Decoder::Status::End || consumed != 0) return 8;
                    }
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedStreamMatchesDotNetRuneAtEveryChunkSizeAndConsumesOneScalarPerRead(int optimization)
    {
        const string text = "\0A\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\U00010000\U0001f600\U0010ffffZ\0";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        uint[] scalars = text.EnumerateRunes().Select(r => (uint)r.Value).ToArray();
        string input = string.Join(",", bytes.Select(b => $"(char){b}"));
        string expected = string.Join(",", scalars.Select(v => $"{v}u"));
        Run($$"""
            int main() {
                char[{{bytes.Length}}] bytes = [{{input}}];
                uint[{{scalars.Length}}] expected = [{{expected}}];
                std::Utf8Decoder decoder = new(); uint scalar = 0u; nuint consumed = 0;
                for (nuint chunk = 1; chunk <= (nuint){{bytes.Length}}; chunk++) {
                    decoder.Reset(); nuint offset = 0; nuint index = 0;
                    while (offset < (nuint){{bytes.Length}}) {
                        nuint end = offset + chunk;
                        if (end > (nuint){{bytes.Length}}) end = {{bytes.Length}};
                        while (offset < end) {
                            std::Utf8Decoder::Status status = decoder.Read(&bytes[offset], end-offset, end == (nuint){{bytes.Length}}, &scalar, &consumed);
                            if (consumed == 0 || consumed > end-offset) return 1;
                            offset += consumed;
                            if (status == std::Utf8Decoder::Status::Scalar) {
                                if (index >= (nuint){{scalars.Length}} || scalar != expected[index]) return 2;
                                index++;
                            } else if (status != std::Utf8Decoder::Status::NeedMore || offset != end || scalar != 0u) return 3;
                        }
                    }
                    if (index != (nuint){{scalars.Length}}) return 4;
                    if (decoder.Read(bytes, 0, true, &scalar, &consumed) != std::Utf8Decoder::Status::End) return 5;
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidPrefixesAreRejectedImmediatelyAcrossEverySplitAndRemainInvalid(int optimization)
    {
        // Each entry ends at the first offending byte, including invalid ranges
        // in the second byte that would otherwise require future input to reject.
        string[] malformed = ["80", "BF", "C0", "C1", "F5", "FF", "C241", "C2C2", "E080", "EDA0", "F080", "F490", "E18041", "F1808041", "F180FF"];
        string cases = string.Join("\n", malformed.Select(hex =>
        {
            byte[] bytes = Convert.FromHexString(hex);
            string data = string.Join(",", bytes.Select(b => $"(char){b}"));
            return $$"""
                {
                    char[{{bytes.Length}}] bytes = [{{data}}];
                    for (nuint split = 0; split <= (nuint){{bytes.Length}}; split++) {
                        decoder.Reset();
                        std::Utf8Decoder::Status status = decoder.Read(bytes, split, false, &scalar, &consumed);
                        if (consumed != split || scalar != 0u) return 1;
                        if (split < (nuint){{bytes.Length}}) {
                            if (status != std::Utf8Decoder::Status::NeedMore) return 2;
                            status = decoder.Read(&bytes[split], (nuint){{bytes.Length}}-split, false, &scalar, &consumed);
                            if (consumed != (nuint){{bytes.Length}}-split || scalar != 0u) return 3;
                        }
                        if (status != std::Utf8Decoder::Status::Invalid) return 4;
                        if (decoder.Read("ok", 2, true, &scalar, &consumed) != std::Utf8Decoder::Status::Invalid || scalar != 0u || consumed != 0) return 5;
                    }
                }
                """;
        }));
        Run($$"""
            int main() {
                std::Utf8Decoder decoder = new(); uint scalar = 42u; nuint consumed = 42;
                {{cases}}
                decoder.Reset();
                if (decoder.Read("ok", 2, true, &scalar, &consumed) != std::Utf8Decoder::Status::Scalar || scalar != 111u || consumed != 1) return 6;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FinalTruncationAndCopiedPartialStatesHaveExplicitIndependentLifetimes(int optimization)
    {
        Run("""
            int main() {
                std::Utf8Decoder decoder = new(); uint scalar = 42u; nuint consumed = 42;
                char[4] bytes = [(char)0xf0,(char)0x9f,(char)0x98,(char)0x80];
                for (nuint length = 1; length < (nuint)4; length++) {
                    decoder.Reset();
                    if (decoder.Read(bytes, length, true, &scalar, &consumed) != std::Utf8Decoder::Status::Invalid || consumed != length || scalar != 0u) return 1;
                    decoder.Reset();
                    if (decoder.Read(bytes, length, false, &scalar, &consumed) != std::Utf8Decoder::Status::NeedMore) return 2;
                    if (decoder.Read(bytes, 0, true, &scalar, &consumed) != std::Utf8Decoder::Status::Invalid || consumed != 0 || scalar != 0u) return 3;
                }
                char[3] shorter = [(char)0xc2,(char)0xe0,(char)0xa0];
                decoder.Reset();
                if (decoder.Read(shorter, 1, true, &scalar, &consumed) != std::Utf8Decoder::Status::Invalid || consumed != 1) return 11;
                decoder.Reset();
                if (decoder.Read(&shorter[1], 1, false, &scalar, &consumed) != std::Utf8Decoder::Status::NeedMore) return 12;
                if (decoder.Read(&shorter[2], 1, true, &scalar, &consumed) != std::Utf8Decoder::Status::Invalid || consumed != 1) return 13;
                decoder.Reset();
                if (decoder.Read(bytes, 3, false, &scalar, &consumed) != std::Utf8Decoder::Status::NeedMore) return 4;
                std::Utf8Decoder copy = decoder;
                char[1] other = [(char)0x81];
                if (decoder.Read(&bytes[3], 1, true, &scalar, &consumed) != std::Utf8Decoder::Status::Scalar || scalar != 0x1f600u || consumed != 1) return 5;
                decoder.Reset();
                if (copy.Read(other, 1, true, &scalar, &consumed) != std::Utf8Decoder::Status::Scalar || scalar != 0x1f601u || consumed != 1) return 6;
                if (decoder.Read("A", 1, true, &scalar, &consumed) != std::Utf8Decoder::Status::Scalar || scalar != 65u || consumed != 1) return 7;
                if (decoder.Read(bytes, 0, false, &scalar, &consumed) != std::Utf8Decoder::Status::NeedMore || scalar != 0u || consumed != 0) return 8;
                if (decoder.Read(bytes, 0, true, &scalar, &consumed) != std::Utf8Decoder::Status::End || scalar != 0u || consumed != 0) return 9;
                // A malformed byte in a larger chunk is consumed, its suffix is not.
                decoder.Reset(); char[3] bad = [(char)0xe0,(char)0x80,'X'];
                if (decoder.Read(bad, 3, true, &scalar, &consumed) != std::Utf8Decoder::Status::Invalid || consumed != 2 || scalar != 0u) return 10;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string code, int optimization)
    {
        var sources = new[] { "Text/utf8.gf", "Text/Utf8Decoder.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free" }) Assert.DoesNotContain(forbidden, ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-utf8-decoder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "decoder.ll"), output = Path.Combine(directory, "decoder.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(output, []).ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}
