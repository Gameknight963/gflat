using System.Buffers.Binary;
using System.Text;

namespace gflat.Tests;

public class StandardBinaryPrimitivesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnalignedReadsAndWritesMatchDotNetAndRejectShortBuffers(int optimization)
    {
        var body=new StringBuilder("using std; int main(){ byte[10] data; ushort small=0; uint medium=0u; ulong large=0ul;");
        ulong[] values=[0ul,1ul,255ul,256ul,0x0123456789abcdeful,0x8000000000000000ul,ulong.MaxValue];
        foreach (int width in new[]{2,4,8})
        foreach (bool little in new[]{true,false})
        foreach (ulong value in values)
        {
            string suffix=$"UInt{width*8}{(little ? "Little" : "Big")}Endian";
            string type=width==2 ? "ushort" : width==4 ? "uint" : "ulong";
            string output=width==2 ? "small" : width==4 ? "medium" : "large";
            ulong truncated=width==2 ? (ushort)value : width==4 ? (uint)value : value;
            byte[] bytes=new byte[width];
            if(width==2) { if(little) BinaryPrimitives.WriteUInt16LittleEndian(bytes,(ushort)value); else BinaryPrimitives.WriteUInt16BigEndian(bytes,(ushort)value); }
            else if(width==4) { if(little) BinaryPrimitives.WriteUInt32LittleEndian(bytes,(uint)value); else BinaryPrimitives.WriteUInt32BigEndian(bytes,(uint)value); }
            else { if(little) BinaryPrimitives.WriteUInt64LittleEndian(bytes,value); else BinaryPrimitives.WriteUInt64BigEndian(bytes,value); }
            body.Append("for(int i=0;i<10;i++) data[i]=(byte)165;");
            body.Append($"if(!BinaryPrimitives::TryWrite{suffix}(&data[1],{width},({type}){value}ul)) return 1;");
            for(int i=0;i<width;i++) body.Append($"if(data[{i+1}]!=(byte){bytes[i]}) return 2;");
            body.Append($"if(data[0]!=(byte)165 || data[{width+1}]!=(byte)165) return 3;");
            body.Append($"if(!BinaryPrimitives::TryRead{suffix}(&data[1],9,ref {output}) || (ulong){output}!={truncated}ul) return 4;");
            body.Append($"if(BinaryPrimitives::TryWrite{suffix}(&data[1],{width-1},({type})0)) return 5;");
            for(int i=0;i<width;i++) body.Append($"if(data[{i+1}]!=(byte){bytes[i]}) return 6;");
            body.Append($"if(BinaryPrimitives::TryRead{suffix}(&data[1],{width-1},ref {output}) || {output}!=({type})0) return 7;");
        }
        body.Append("return 0;}");
        SourceFile[] sources=[new("BinaryPrimitives.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Binary/BinaryPrimitives.gf"))),new("main.gf",body.ToString())];
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Fact]
    public void ByteOrderHelpersDoNotIntroduceRuntimeServices()
    {
        SourceFile[] sources=[new("BinaryPrimitives.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Binary/BinaryPrimitives.gf"))),
            new("main.gf","using std; int main(){byte[4] data; uint value=0u; BinaryPrimitives::TryWriteUInt32LittleEndian(data,4,42u); BinaryPrimitives::TryReadUInt32LittleEndian(data,4,ref value); return (int)value;}")];
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,InternalizeFunctions=true});
        Assert.DoesNotContain("@__gflat_eh_",ir);
        Assert.DoesNotContain("personality",ir);
        Assert.DoesNotContain("@malloc",ir);
    }
}
