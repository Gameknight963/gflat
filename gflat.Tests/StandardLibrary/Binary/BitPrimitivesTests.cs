namespace gflat.Tests.Std.Binary;

public sealed class BitPrimitivesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryBitOffsetAndWidthMatchesIndependentLogicalBitStreams(int optimization)
    {
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, System.Text.Encoding.UTF8, true))
        for (int order = 0; order < 2; order++)
        for (int offset = 0; offset < 8; offset++)
        for (int width = 0; width <= 64; width++)
        for (int mode = 0; mode < 3; mode++)
        {
            byte[] input = Enumerable.Range(0, 9).Select(index => mode == 0 ? (byte)0 :
                mode == 1 ? (byte)255 : (byte)(index * 73 + 0x96)).ToArray();
            ulong mask = width == 64 ? ulong.MaxValue : (1ul << width) - 1;
            ulong value = mode == 0 ? 0 : mode == 1 ? mask : 0xD6A35F0E19C27B84ul & mask;
            writer.Write(ReadBits(input, offset, width, order != 0));
            writer.Write(value);
            writer.Write(WriteBits(input, offset, width, order != 0, value));
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            byte ReadByte(readonly(char)* data,ref nuint at)nothrow{
                byte value=(byte)((Digit(data[at])<<4)|Digit(data[at+1]));at+=2;return value;
            }
            ulong ReadULong(readonly(char)* data,ref nuint at)nothrow{
                ulong value=0ul;for(int index=0;index<8;index++)value=value|((ulong)ReadByte(data,ref at)<<(index*8));return value;
            }
            int main()nothrow{
                readonly(char)* records="{{hex}}";nuint at=0;
                for(int orderIndex=0;orderIndex<2;orderIndex++)
                for(nuint offset=0;offset<8;offset++)
                for(int width=0;width<=64;width++)
                for(int mode=0;mode<3;mode++){
                    ulong expected=ReadULong(records,ref at);ulong value=ReadULong(records,ref at);
                    byte[11] data;byte[11] saved;data[0]=(byte)165;data[10]=(byte)90;
                    for(nuint index=0;index<9;index++){
                        byte initial=0;if(mode==1)initial=(byte)255;else if(mode==2)initial=(byte)((int)index*73+150);
                        data[index+1]=initial;
                    }
                    for(nuint index=0;index<11;index++)saved[index]=data[index];
                    BitOrder order=(BitOrder)orderIndex;ulong result=73ul;
                    if(!BitPrimitives::TryReadUInt64(&data[1],9,offset,width,order,ref result)||result!=expected)return 1;
                    for(nuint index=0;index<11;index++)if(data[index]!=saved[index])return 2;
                    if(!BitPrimitives::TryWriteUInt64(&data[1],9,offset,width,order,value))return 3;
                    for(nuint index=0;index<9;index++)if(data[index+1]!=ReadByte(records,ref at))return 4;
                    if(data[0]!=saved[0]||data[10]!=saved[10])return 5;
                    result=73ul;if(!BitPrimitives::TryReadUInt64(&data[1],9,offset,width,order,ref result)||result!=value)return 6;
                }
                if(at!={{hex.Length}})return 7;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidArgumentsAreAtomicAndZeroWidthRequiresAValidEndpointAndZeroValue(int optimization)
        => Run("""
            int main()nothrow{
                byte[11] data;byte[11] saved;
                for(nuint index=0;index<11;index++){data[index]=(byte)((int)index*29+7);saved[index]=data[index];}
                int[4] invalidWidths=[-1,65,-2147483648,2147483647];
                for(int orderIndex=0;orderIndex<2;orderIndex++){
                    BitOrder order=(BitOrder)orderIndex;
                    for(nuint row=0;row<4;row++){
                        ulong result=99ul;
                        if(BitPrimitives::TryReadUInt64(&data[1],9,0,invalidWidths[row],order,ref result)||result!=0ul)return 1;
                        if(BitPrimitives::TryWriteUInt64(&data[1],9,0,invalidWidths[row],order,0ul))return 2;
                    }
                    for(int width=0;width<64;width++){
                        if(BitPrimitives::TryWriteUInt64(&data[1],9,0,width,order,1ul<<width))return 3;
                    }
                    nuint[10] counts=[0,1,8,9,0,1,9,0,1,9];
                    nuint[10] offsets=[0,8,1,9,0,8,72,1,9,73];
                    int[10] widths=[1,1,64,64,0,0,0,0,0,0];
                    bool[10] fits=[false,false,false,false,true,true,true,false,false,false];
                    for(nuint row=0;row<10;row++){
                        ulong result=99ul;
                        if(BitPrimitives::TryReadUInt64(&data[1],counts[row],offsets[row],widths[row],order,ref result)!=fits[row]||result!=0ul)return 4;
                        if(BitPrimitives::TryWriteUInt64(&data[1],counts[row],offsets[row],widths[row],order,0ul)!=fits[row])return 5;
                    }
                    nuint maximum=(nuint)0-1;nuint[2] huge=[maximum-7,maximum];
                    for(nuint row=0;row<2;row++)for(int width=0;width<=64;width+=32){
                        ulong result=99ul;
                        if(BitPrimitives::TryReadUInt64(&data[1],9,huge[row],width,order,ref result)||result!=0ul)return 6;
                        if(BitPrimitives::TryWriteUInt64(&data[1],9,huge[row],width,order,0ul))return 7;
                    }
                    ulong zero=99ul;
                    if(!BitPrimitives::TryReadUInt64(((nuint)1).reinterpret<readonly(byte)*>(),0,0,0,order,ref zero)||zero!=0ul)return 8;
                    if(!BitPrimitives::TryWriteUInt64(((nuint)1).reinterpret<byte*>(),0,0,0,order,0ul))return 9;
                    if(BitPrimitives::TryWriteUInt64(((nuint)1).reinterpret<byte*>(),0,0,0,order,1ul))return 10;
                }
                int[2] invalidOrders=[-1,2];
                for(nuint row=0;row<2;row++)for(int width=0;width<2;width++){
                    ulong result=99ul;
                    if(BitPrimitives::TryReadUInt64(&data[1],9,0,width,(BitOrder)invalidOrders[row],ref result)||result!=0ul)return 11;
                    if(BitPrimitives::TryWriteUInt64(&data[1],9,0,width,(BitOrder)invalidOrders[row],0ul))return 12;
                }
                for(nuint index=0;index<11;index++)if(data[index]!=saved[index])return 13;
                return 0;
            }
            """, optimization);

    // Build the entire logical stream, then slice/replace and convert by positional digits.
    private static bool[] LogicalBits(byte[] bytes, bool mostFirst)
        => bytes.SelectMany(value => {
            var digits = new List<bool>();int remaining = value;
            for (int bit = 0; bit < 8; bit++) { digits.Add(remaining % 2 != 0);remaining /= 2; }
            return mostFirst ? digits.AsEnumerable().Reverse() : digits;
        }).ToArray();

    private static ulong Accumulate(IEnumerable<bool> digits)
        => digits.Aggregate(0ul, (value, bit) => checked(value * 2 + (bit ? 1ul : 0ul)));

    private static ulong ReadBits(byte[] bytes, int offset, int width, bool mostFirst)
    {
        IEnumerable<bool> digits = LogicalBits(bytes, mostFirst).Skip(offset).Take(width);
        return Accumulate(mostFirst ? digits : digits.Reverse());
    }

    private static byte[] WriteBits(byte[] bytes, int offset, int width, bool mostFirst, ulong value)
    {
        bool[] stream = LogicalBits(bytes, mostFirst);
        var digits = new List<bool>();
        for (int bit = 0; bit < width; bit++) { digits.Add(value % 2 != 0);value /= 2; }
        if (mostFirst) digits.Reverse();
        digits.CopyTo(stream, offset);
        return stream.Chunk(8).Select(group => (byte)Accumulate(mostFirst ? group : group.Reverse())).ToArray();
    }

    private static void Run(string program, int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Standard/core/Binary/BitPrimitives.gf");
        string ir = Compiler.Emit([new SourceFile(path, File.ReadAllText(path)),
            new SourceFile("bit-primitives.gf", "using Standard::Binary;\n" + program)],
            options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "gflat$Memory$", "@malloc", "@free", "__gflat_gc" })
            Assert.DoesNotContain(forbidden, ir);
        foreach (string name in new[] { "TryReadUInt64", "TryWriteUInt64" })
        {
            string definition = Assert.Single(ir.Split('\n'), line => line.StartsWith("define ", StringComparison.Ordinal)
                && line.Contains("$BitPrimitives$" + name + "(", StringComparison.Ordinal));
            Assert.Contains("nounwind", definition);
        }
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
