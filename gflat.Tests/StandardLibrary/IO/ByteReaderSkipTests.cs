namespace gflat.Tests.Std.IO;

public sealed class ByteReaderSkipTests
{
    private const string Source = """
        using Standard::IO;
        struct Source : IByteReader {
            public nuint Length; public nuint Boundary; public nuint Position;
            public nuint Calls; public nuint ZeroCalls; public nuint Maximum; public nuint EofReads;
            public bool Bad; private nuint chunk;
            public Source(nuint length, nuint block, nuint boundary) {
                Length=length; Boundary=boundary; Position=0; Calls=0; ZeroCalls=0; Maximum=0; EofReads=0; Bad=false; chunk=block;
            }
            public nuint Read(char* output, nuint count) {
                if(count==0){ZeroCalls++;return 0;}
                Calls++;if(count>Maximum)Maximum=count;
                if(count>4096||count>Boundary-Position){Bad=true;return 0;}
                nuint take=Length-Position;if(take>count)take=count;if(take>chunk)take=chunk;
                for(nuint index=0;index<take;index++)output[index]=(char)((Position+index)*73+19);
                Position+=take;if(take==0)EofReads++;
                return take;
            }
        }
        """;

    private const string NoBufferAllocations = """
        int allocationAttempts=0;
        namespace Memory {
            public replace void*? Allocate(nuint count){allocationAttempts++;return null;}
            public replace void Free(void* storage)nothrow{}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChunkedReadsStopAtIndependentFrameBoundaryWithoutAllocatingABuffer(int optimization)
    {
        var cases = new List<ulong>();
        foreach (ulong count in new ulong[] { 0, 1, 2, 31, 4095, 4096, 4097, 8192, 8193 })
        foreach (ulong chunk in new ulong[] { 1, 3, 257, 4096, 10000 })
        {
            cases.AddRange([count, chunk, unchecked((byte)(count * 73 + 19))]);
        }
        string values = string.Join(",", cases);
        Run(Source + $$"""
            int main(){
                nuint[{{cases.Count}}] cases=[{{values}}];
                for(nuint row=0;row<{{cases.Count}};row+=3){
                    nuint count=cases[row];Source source=new(count+7,cases[row+1],count);
                    IByteReader* input=&source;
                    if(row%2==0)input.SkipExactly(count);else SkipExactly(input,count);
                    nuint zero=0;if(count==0)zero=1;
                    if(source.Position!=count||source.ZeroCalls!=zero||source.EofReads!=0||source.Bad)return 1;
                    if(count==0&&source.Calls!=0)return 3;
                    source.Boundary=source.Length;char marker='?';
                    if(input.Read(&marker,1)!=1||marker!=(char)cases[row+2]||source.Position!=count+1||source.Bad)return 2;
                }
                return allocationAttempts;
            }
            """ + NoBufferAllocations, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EofSourceErrorsInvalidCountsAndZeroValidationKeepTheirConsumptionContracts(int optimization)
        => Run(Source + """
            class SourceFailure : Exception {
                public SourceFailure() : base("Source failure",79) {}
            }
            struct FaultSource : IByteReader {
                public int Mode; public nuint Calls; public nuint ZeroCalls; public nuint Position;
                public FaultSource(int mode){Mode=mode;Calls=0;ZeroCalls=0;Position=0;}
                public nuint Read(char* output,nuint count){
                    if(count==0){ZeroCalls++;if(Mode==4)throw new SourceFailure();if(Mode==5)return 1;return 0;}
                    Calls++;
                    if(Calls!=1)return 0;
                    output[0]='p';Position++;
                    if(Mode==1)throw new SourceFailure();
                    if(Mode==2)return count+1;
                    return (nuint)0-(nuint)1;
                }
            }
            int main(){
                nuint[5] lengths=[0,1,4095,4096,4097];
                for(nuint row=0;row<5;row++){
                    Source source=new(lengths[row],257,lengths[row]+1);bool caught=false;
                    try{IByteReader* input=&source;input.SkipExactly(lengths[row]+1);}
                    catch(ref EndOfStreamException error){caught=true;}
                    if(!caught||source.Position!=lengths[row]||source.EofReads!=1||source.ZeroCalls!=0||source.Bad)return 10;
                }
                Source empty=new(0,1,(nuint)0-(nuint)1);bool caught=false;
                try{SkipExactly(&empty,(nuint)0-(nuint)1);}catch(ref EndOfStreamException error){caught=true;}
                if(!caught||empty.Calls!=1||empty.Maximum==0||empty.Maximum>4096||empty.Position!=0)return 11;
                FaultSource failed=new(1);caught=false;
                try{SkipExactly(&failed,3);}catch(ref SourceFailure error){if(error.GetCode()!=79)return 12;caught=true;}
                if(!caught||failed.Calls!=1||failed.Position!=1)return 13;
                for(int mode=2;mode<=3;mode++){
                    FaultSource bad=new(mode);caught=false;
                    try{SkipExactly(&bad,3);}
                    catch(ref EndOfStreamException error){return 14;}
                    catch(ref IOException error){caught=true;}
                    if(!caught||bad.Calls!=1||bad.Position!=1)return 15;
                }
                FaultSource closed=new(4);caught=false;
                try{SkipExactly(&closed,0);}catch(ref SourceFailure error){if(error.GetCode()!=79)return 16;caught=true;}
                if(!caught||closed.Calls!=0||closed.ZeroCalls!=1||closed.Position!=0)return 17;
                FaultSource invalidZero=new(5);caught=false;
                try{SkipExactly(&invalidZero,0);}catch(ref IOException error){caught=true;}
                if(!caught||invalidZero.Calls!=0||invalidZero.ZeroCalls!=1||invalidZero.Position!=0)return 18;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RealBorrowedBinaryCursorsShareTheirPositionAndCopiesStayIndependent(int optimization)
        => Run("""
            using Standard::IO;using Standard::Binary;
            int main(){
                byte[8206] bytes;for(nuint index=0;index<8206;index++)bytes[index]=(byte)(index*73+19);
                BinaryBufferReader source=new(bytes,8206);IByteReader* input=&source;
                if(!source.TrySeek(5))return 20;input.SkipExactly(4097);
                if(source.Position!=4102)return 21;
                byte next=0;if(!source.TryReadByte(ref next)||next!=(byte)201||source.Position!=4103)return 22;
                BinaryBufferReader second=source;IByteReader* other=&second;other.SkipExactly(4097);
                if(second.Position!=8200||source.Position!=4103)return 23;
                if(!second.TryReadByte(ref next)||next!=(byte)91)return 24;
                if(!source.TrySeek(8203)||source.TrySkip(4)||source.Position!=8203)return 25;
                SkipExactly(input,3);if(source.Position!=source.Length)return 26;
                input.SkipExactly(0);if(source.Position!=source.Length||allocationAttempts!=0)return 27;
                return 0;
            }
            """ + NoBufferAllocations, optimization, binary: true);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LimitedPayloadKeepsFollowingFieldsOnTheSameBufferedCursor(int optimization)
    {
        const string program = """
            using Standard::IO;
            using Standard::Binary;

            extern void*? malloc(nuint bytes);
            extern void free(void* data);
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    attempts++;
                    void*? result = malloc(bytes);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* data) nothrow {
                    released++;
                    free(data);
                }
            }

            int main() {
                {
                    byte[12] packet = [
                        (byte)97, (byte)98, (byte)0, (byte)255, (byte)101, (byte)102,
                        (byte)77, (byte)0, (byte)255, (byte)82, (byte)83, (byte)84];
                    BinaryBufferReader raw = new(packet, 12);
                    BufferedReader buffered = new(&raw, 8);
                    IByteReader* cursor = &buffered;
                    int before = attempts;
                    {
                        LimitedReader payload = new(cursor, 6);
                        IByteReader* input = &payload;
                        char first = '?';
                        if (input.Read(&first, 1) != 1 || first != 'a' || payload.Remaining != 5)
                            return 30;
                        payload.SkipExactly(payload.Remaining);
                        if (payload.Remaining != 0) return 31;
                        char untouched = '?';
                        if (input.Read(&untouched, 1) != 0 || untouched != '?') return 32;
                    }

                    // The raw cursor may have read ahead. Continue through the same adapter.
                    char marker = '?';
                    if (cursor.Read(&marker, 1) != 1 || marker != 'M') return 33;
                    char[2] tail;
                    cursor.ReadExactly(tail, 2);
                    if (tail[0] != '\0' || tail[1] != (char)255) return 34;
                    cursor.SkipExactly(3);
                    char untouched = '?';
                    if (cursor.Read(&untouched, 1) != 0 || untouched != '?') return 35;
                    if (attempts != before) return 36;
                }
                if (allocated != released) return 37;

                {
                    byte[3] packet = [(byte)120, (byte)121, (byte)122];
                    BinaryBufferReader raw = new(packet, 3);
                    BufferedReader buffered = new(&raw, 2);
                    IByteReader* cursor = &buffered;
                    {
                        LimitedReader payload = new(cursor, 5);
                        bool caught = false;
                        try { Standard::IO::SkipExactly(&payload, 5); }
                        catch (ref EndOfStreamException error) { caught = true; }
                        if (!caught || payload.Remaining != 2) return 38;
                    }
                    char untouched = '?';
                    if (cursor.Read(&untouched, 1) != 0 || untouched != '?') return 39;
                }
                if (allocated != released) return 40;
                return 0;
            }
            """;
        Run(program, optimization, allStandard: true);
    }

    private static void Run(string program, int optimization, bool binary = false, bool allStandard = false)
    {
        IEnumerable<SourceFile> library;
        if (allStandard)
        {
            library = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)));
        }
        else
        {
            var names = new List<string>
            {
                "core/Exception.gf", "core/ITextWriter.gf", "core/IO/IByteReader.gf",
                "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
                "core/IO/byte_reader.gf", "core/IO/byte_reader_skip.gf",
                "libc/Memory/allocation.gf", "libc/Exceptions/unhandled.gf"
            };
            if (binary) names.AddRange(["core/Binary/BinaryPrimitives.gf", "core/Binary/BinaryBufferReader.gf", "core/Memory/bytes.gf"]);
            library = names.Select(name => new SourceFile(name,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))));
        }
        var sources = library.Append(new SourceFile("byte-reader-skip-tests.gf", program));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
