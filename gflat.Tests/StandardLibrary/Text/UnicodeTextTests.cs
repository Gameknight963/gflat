using System.Buffers.Binary;
using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class UnicodeTextTests
{
    private const string Prelude="""
        using Standard; using Standard::Text; using Standard::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocations=0;int releases=0;int largeAllocations=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){if(count>1024)largeAllocations++;if(deny)return null;allocations++;return malloc(count);}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrictOwningConversionsMatchDotNetAndAllocateExactlyOnce(int optimization)
    {
        var utf8=new UTF8Encoding(false,true);var utf16=new UnicodeEncoding(false,false,true);
        List<string> values=["","\0","\ufeff","a\0b",new string(Enumerable.Range(0,128).Select(i=>(char)i).ToArray()),
            "\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\ud800\udc00\udbff\udfff","\ud83d\ude00\ud83d\ude00\0\ufeff"];
        var random=new Random(99234);
        for(int i=0;i<24;i++){
            var value=new StringBuilder();for(int j=0;j<random.Next(1,13);j++){
                int scalar=random.Next(0x110000);if(scalar is >=0xd800 and <=0xdfff)scalar=0;
                value.Append(char.ConvertFromUtf32(scalar));
            }values.Add(value.ToString());
        }
        var code=new StringBuilder(Prelude+"int main(){char[256] bytes;ushort[256] units;\n");
        foreach(string value in values)
        {
            byte[] bytes=utf8.GetBytes(value),encodedUnits=utf16.GetBytes(value);
            ushort[] units=Enumerable.Range(0,encodedUnits.Length/2).Select(i=>BinaryPrimitives.ReadUInt16LittleEndian(encodedUnits.AsSpan(i*2,2))).ToArray();
            Assert.Equal(value,utf8.GetString(bytes));Assert.Equal(value,utf16.GetString(encodedUnits));
            Assign(code,"bytes",bytes.Select(v=>(int)v));Assign(code,"units",units.Select(v=>(int)v));
            code.AppendLine($$"""
                {int before=allocations;
                 {String text=UnicodeText::FromUtf16(units,{{units.Length}});
                  if(allocations!=before+{{(bytes.Length==0?0:1)}}||text.Length!={{bytes.Length}}||text.Data[text.Length]!='\0')return 1;
                  for(nuint i=0;i<text.Length;i++)if(text.Data[i]!=bytes[i])return 2;}
                 before=allocations;
                 {Vector<ushort> text=UnicodeText::ToUtf16(bytes,{{bytes.Length}});
                  if(allocations!=before+{{(units.Length==0?0:1)}}||text.Count!={{units.Length}}||text.Capacity!={{units.Length}})return 3;
                  for(nuint i=0;i<text.Count;i++)if(text[i]!=units[i])return 4;
                  if(text.Count!=0)text[0]=(ushort)7;}
                }
                """);
            // Original counted inputs must remain unchanged in both directions.
            for(int i=0;i<bytes.Length;i++)code.AppendLine($"if((byte)bytes[{i}]!=(byte){bytes[i]})return 6;");
            for(int i=0;i<units.Length;i++)code.AppendLine($"if(units[{i}]!=(ushort){units[i]})return 7;");
        }
        code.AppendLine("""
            {int before=allocations;String empty=UnicodeText::FromUtf16(((nuint)1).reinterpret<readonly(ushort)*>(),0);
             Vector<ushort> zero=UnicodeText::ToUtf16(((nuint)1).reinterpret<readonly(char)*>(),0);
             if(!empty.IsEmpty||!zero.IsEmpty||allocations!=before)return 8;}
            if(allocations!=releases)return 9;return 0;}
            """);
        Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidEncodingsAndAllocationFailuresDoNotChangeExistingOwners(int optimization)
    {
        var code=new StringBuilder(Prelude+"int main(){char[5000] bytes;ushort[5000] units;\n");
        ushort[][] badUnits=[[0xd800],[0xdc00],[0xd800,65],[65,0xdfff],[0xdbff,0xdbff],[0xdc00,0xd800]];
        byte[][] badBytes=[[0x80],[0xc0,0x80],[0xc2],[0xe0,0x9f,0xbf],[0xed,0xa0,0x80],[0xf0,0x8f,0xbf,0xbf],[0xf4,0x90,0x80,0x80],[0xf5,0x80,0x80,0x80],[0xff]];
        var strict8=new UTF8Encoding(false,true);var strict16=new UnicodeEncoding(false,false,true);
        foreach(ushort[] units in badUnits){
            byte[] binary=units.SelectMany(v=>new[]{(byte)v,(byte)(v>>8)}).ToArray();Assert.Throws<DecoderFallbackException>(()=>strict16.GetString(binary));
            Assign(code,"units",units.Select(v=>(int)v));
            code.AppendLine($"try{{UnicodeText::FromUtf16(units,{units.Length});return 1;}}catch(ref Exception error){{}}");
        }
        foreach(byte[] bytes in badBytes){
            Assert.Throws<DecoderFallbackException>(()=>strict8.GetString(bytes));Assign(code,"bytes",bytes.Select(v=>(int)v));
            code.AppendLine($"try{{UnicodeText::ToUtf16(bytes,{bytes.Length});return 2;}}catch(ref Exception error){{}}");
        }
        code.AppendLine("""
            for(nuint i=0;i<5000;i++){bytes[i]='a';units[i]=(ushort)'a';}
            units[4999]=(ushort)0xd800;bytes[4999]=(char)255;int before=largeAllocations;
            try{UnicodeText::FromUtf16(units,5000);return 3;}catch(ref Exception error){}
            try{UnicodeText::ToUtf16(bytes,5000);return 4;}catch(ref Exception error){}
            if(largeAllocations!=before||units[4999]!=(ushort)0xd800||(byte)bytes[4999]!=(byte)255)return 5;
            {String destination=s"unchanged";Vector<ushort> vector=new();vector.Add((ushort)42);
             readonly(char)* original=destination.Data;ushort* originalVector=vector.AsSpan().Data!;
             units[0]=(ushort)'x';bytes[0]='x';deny=true;
             try{destination=UnicodeText::FromUtf16(units,1);return 6;}catch(ref BadAllocException error){}
             try{vector=UnicodeText::ToUtf16(bytes,1);return 7;}catch(ref BadAllocException error){}
             deny=false;
             if(destination.Data!=original||!CString::Equals(destination.Data,"unchanged"))return 8;
             if(vector.Count!=1||vector[0]!=(ushort)42||vector.AsSpan().Data!=originalVector)return 9;}
            if(allocations!=releases)return 10;return 0;}
            """);
        Run(code.ToString(),optimization);
    }

    private static void Assign(StringBuilder code,string name,IEnumerable<int> values)
    {
        int index=0;string type=name=="bytes"?"char":"ushort";
        foreach(int value in values)code.AppendLine($"{name}[{index++}]=({type}){value};");
    }
    private static void Run(string code,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
