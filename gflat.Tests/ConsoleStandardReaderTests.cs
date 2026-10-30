namespace gflat.Tests;

public sealed class ConsoleStandardReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RedirectedInputSharesOneCursorWithConsoleOperationsAndCommonConsumers(int optimization)
    {
        string directory = TemporaryDirectory();
        bool passed = false;
        try
        {
            string executable = Compile(directory, """
                using Standard::IO;
                struct Sink:ITextWriter{
                    private char* data;public nuint Count;public nuint Calls;
                    public Sink(char* output){data=output;Count=0;Calls=0;}
                    public void Write(readonly(char)* input,nuint count){
                        if(count>2-Count)throw new IOException("Unexpected Console copy size");
                        Calls++;for(nuint i=0;i<count;i++)data[Count+i]=input[i];Count+=count;
                    }
                }
                int main(){
                    IByteReader* input=Console::In;if(input!=Console::In)return 1;
                    if(input.Read(((nuint)1).reinterpret<char*>(),0)!=0)return 2;Standard::IO::ReadExactly(input,((nuint)1).reinterpret<char*>(),0);
                    char[4] header=['?','?','?','?'];if(input.Read(&header[1],2)!=2||header[0]!='?'||header[1]!='A'||header[2]!='\0'||header[3]!='?')return 3;
                    if(Console::PeekByte()!=255||Console::PeekByte()!=255||Console::ReadByte()!=255)return 4;
                    if(Standard::IO::ReadUInt32BigEndian(input)!=305419896u)return 5;
                    if(Standard::IO::ReadVarUInt32(input)!=300u||Standard::IO::ReadZigZagInt64(input)!=-42l)return 6;
                    char value='\0';if(Console::Read(&value,1)!=1||value!='B')return 7;
                    char[4] next=['?','?','?','?'];Standard::IO::ReadExactly(input,&next[1],2);
                    if(next[0]!='?'||next[1]!='C'||next[2]!='\0'||next[3]!='?')return 8;
                    char[4] output=['?','?','?','?'];char[4] scratch=['!','!','!','!'];Sink sink=new(&output[1]);
                    if(Standard::IO::CopyTo(input,&sink,&scratch[1],2)!=2||sink.Count!=2||sink.Calls!=1)return 9;
                    if((byte)output[1]!=(byte)254||output[2]!='Z'||output[0]!='?'||output[3]!='?'||scratch[0]!='!'||scratch[3]!='!')return 10;
                    if(input.Read(&value,1)!=0||Console::ReadByte()!=-1||input!=Console::In)return 11;
                    if(attempts!=0)return 12;return 0;
                }
                """, optimization, denyAllocation: true);
            byte[] input = [65, 0, 255, 0x12, 0x34, 0x56, 0x78, 0xac, 2, 0x53, 66, 67, 0, 254, 90];
            await ConsoleLineWriterTests.Run(executable, input, []);
            passed = true;
        }
        catch (Exception error)
        {
            error.Data["ConsoleStandardReaderDirectory"] = directory;
            throw;
        }
        finally
        {
            if (passed) await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task StableReaderFollowsFreopenEofClearErrorRecoveryAndCurrentTextMode(int optimization)
    {
        string directory = TemporaryDirectory();
        bool passed = false;
        try
        {
            string original = Path.Combine(directory, "original.bin"), replacement = Path.Combine(directory, "replacement.bin");
            File.WriteAllBytes(original, [65, 0, 66, 13, 10, 26, 255, 90]);
            File.WriteAllBytes(replacement, [68, 69]);
            byte[] textExpected = OperatingSystem.IsWindows() ? [65, 0, 66, 10] : [65, 0, 66, 13, 10, 26, 255, 90, 67];
            string executable = Compile(directory, $$"""
                using Standard::IO;
                extern void* __gflat_stdio_stdin();
                extern void*? freopen(readonly(char)* path,readonly(char)* mode,void* stream);
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern int fclose(void* stream);
                extern int fputc(int value,void* stream);
                extern int feof(void* stream);extern int ferror(void* stream);
                int main(){
                    IByteReader* input=Console::In;void* stream=__gflat_stdio_stdin();
                    if(freopen({{Literal(original)}},"rb",stream)==null)return 21;
                    char[10] bytes;for(nuint i=0;i<10;i++)bytes[i]='?';Standard::IO::ReadExactly(input,&bytes[1],8);
                    byte[8] expected=[(byte)65,(byte)0,(byte)66,(byte)13,(byte)10,(byte)26,(byte)255,(byte)90];
                    for(nuint i=0;i<8;i++)if((byte)bytes[i+1]!=expected[i])return 22;
                    if(bytes[0]!='?'||bytes[9]!='?'||input.Read(&bytes[1],1)!=0||feof(stream)==0)return 23;
                    void*? append=fopen({{Literal(original)}},"ab");if(append==null)return 24;
                    if(fputc(67,append!)!=67||fclose(append!)!=0)return 25;
                    Console::ClearInputError();if(feof(stream)!=0||input.Read(&bytes[1],1)!=1||bytes[1]!='C')return 26;
                    if(input.Read(&bytes[1],1)!=0||input!=Console::In)return 27;
                    if(freopen({{Literal(replacement)}},"rb",stream)==null)return 28;
                    if(input!=Console::In||Console::PeekByte()!=68||input.Read(&bytes[1],1)!=1||bytes[1]!='D')return 29;
                    Standard::IO::ReadExactly(input,&bytes[1],1);if(bytes[1]!='E'||input.Read(&bytes[1],1)!=0)return 30;
                    if(freopen({{Literal(replacement)}},"wb",stream)==null)return 31;
                    for(int attempt=0;attempt<2;attempt++){
                        bool caught=false;
                        try{input.Read(&bytes[1],1);}catch(ref EndOfStreamException error){return 32;}catch(ref IOException error){caught=true;}
                        if(!caught||ferror(stream)==0||input.Read(((nuint)1).reinterpret<char*>(),0)!=0||ferror(stream)==0)return 33;
                        Console::ClearInputError();if(ferror(stream)!=0||feof(stream)!=0)return 34;
                    }
                    if(freopen({{Literal(original)}},"r",stream)==null)return 35;
                    byte[{{textExpected.Length}}] textExpected=[{{string.Join(",", textExpected.Select(value => "(byte)" + value))}}];
                    char[18] text;for(nuint i=0;i<18;i++)text[i]='?';
                    if(input.Read(&text[1],16)!={{textExpected.Length}}||input!=Console::In)return 36;
                    for(nuint i=0;i<{{textExpected.Length}};i++)if((byte)text[i+1]!=textExpected[i])return 37;
                    if(text[0]!='?'||text[{{textExpected.Length + 1}}]!='?'||ferror(stream)!=0)return 38;
                    return 0;
                }
                """, optimization);
            ProcessResult result = NativeToolchain.Run(executable, [], 30000);
            Assert.True(result.ExitCode == 0, $"Console::In exit {result.ExitCode}: {result.StandardError}\nArtifacts: {directory}");
            Assert.Empty(result.StandardOutput);
            Assert.Empty(result.StandardError);
            passed = true;
        }
        catch (Exception error)
        {
            error.Data["ConsoleStandardReaderDirectory"] = directory;
            throw;
        }
        finally
        {
            if (passed) await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    private static string TemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-console-standard-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Compile(string directory, string program, int optimization, bool denyAllocation = false)
    {
        string[] names = ["core/Exception.gf", "core/ITextWriter.gf", "core/IO/IByteReader.gf", "core/IO/IOException.gf",
            "core/IO/EndOfStreamException.gf", "core/Text/BufferTooSmallException.gf", "core/IO/byte_reader.gf",
            "core/Binary/BinaryPrimitives.gf", "core/Binary/VarInt.gf", "core/IO/byte_reader_binary.gf",
            "core/IO/byte_reader_var_int.gf", "libc/Console/Console.gf", "libc/Console/standard_readers.gf", "libc/Memory/allocation.gf"];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("console-standard-reader-tests.gf", program)).ToList();
        if (denyAllocation) sources.Add(new SourceFile("allocation-hooks.gf", """
            int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){attempts++;return null;}
                public replace void Free(void* address)nothrow{}
            }
            """));
        string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "main.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true }));
        ProcessResult result = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(result.ExitCode == 0, $"Console reader native compile failed: {result.StandardError}\nArtifacts: {directory}");
        return executable;
    }
}
