namespace gflat.Tests.Std.Codecs;

public sealed class StreamingCodecBytePointerTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("STREAMING_CODEC_BYTES.md", 0);

    [Theory]
    [InlineData("Base32", 0)]
    [InlineData("Base32", 2)]
    [InlineData("Base64", 0)]
    [InlineData("Base64", 2)]
    [InlineData("Hex", 0)]
    [InlineData("Hex", 2)]
    public void EveryShortChunkingMatchesOneShotAndByteWritesRespectCompletion(string codec, int optimization)
    {
        string setup = codec == "Hex" ? "ITextWriter* encoder = &encoded;" : $"{codec}Encoder encoder = new(&encoded);";
        string write = codec == "Hex" ? "encoder.WriteHex(&data[position], chunk);" : "encoder.Write(&data[position], chunk);";
        string complete = codec == "Hex" ? "" : "encoder.Complete();";
        string completedWrite = codec == "Hex" ? "" : "bool caughtWrite = false; try { encoder.Write(&data[0], 0); } catch (Exception* error) { caughtWrite = true; } if (!caughtWrite) return 5;";
        string[] files = codec == "Hex"
            ? ["core/Encoding/Hex.gf", "core/Encoding/HexText.gf", "core/Encoding/HexDecoder.gf", "core/Encoding/hex_writer.gf"]
            : [$"core/Encoding/{codec}.gf", $"core/Encoding/{codec}Text.gf", $"core/Encoding/{codec}Encoder.gf", $"core/Encoding/{codec}Decoder.gf"];
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(files))
            .Append(new SourceFile("streaming-codec-bytes.gf", $$"""
                using Standard;
                using Standard::Encoding;
                using Standard::Text;

                int main()
                {
                    byte[256] data;
                    for (nuint i = 0; i < 256; i++) data[i] = (byte)i;
                    String expected = {{codec}}Text::Encode(&data[0], 256);
                    for (nuint chunkSize = 1; chunkSize <= 19; chunkSize++)
                    {
                        String encoded = new();
                        {{setup}}
                        nuint position = 0;
                        while (position < 256)
                        {
                            nuint chunk = 256 - position;
                            if (chunk > chunkSize) chunk = chunkSize;
                            {{write}}
                            position += chunk;
                        }
                        {{complete}}
                        if (!encoded.Equals(&expected)) return 1;
                        {{completedWrite}}
                        byte[512] wire;
                        for (nuint i = 0; i < encoded.Length; i++) wire[i] = (byte)encoded.Data[i];
                        String decoded = new();
                        {{codec}}Decoder decoder = new(&decoded);
                        position = 0;
                        while (position < encoded.Length)
                        {
                            nuint chunk = encoded.Length - position;
                            if (chunk > chunkSize) chunk = chunkSize;
                            decoder.Write(&wire[position], chunk);
                            position += chunk;
                        }
                        decoder.Complete();
                        if (!decoded.Equals((&data[0]).reinterpret<readonly(char)*>(), 256)) return 2;
                        bool caught = false;
                        try { decoder.Write(&wire[0], 0); }
                        catch (Exception* error) { caught = true; }
                        if (!caught) return 3;
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
