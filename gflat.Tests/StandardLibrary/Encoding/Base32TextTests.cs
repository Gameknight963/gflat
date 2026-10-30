using System.Text;

namespace gflat.Tests.Std.Codecs;

public sealed class Base32TextTests
{
    private const string Allocator = """
        using std; using std::Text; using std::Encoding;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int allocations=0; int releases=0; int large=0; bool deny=false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                if(bytes>1024)large++;
                if(deny)return null;
                void*? result=malloc(bytes);if(result!=null)allocations++;return result;
            }
            public replace void Free(void* storage) nothrow { releases++;free(storage); }
        }
        """;

    // Independent bit-index oracle: no product chunking or accumulator recurrence.
    private static string Encode(byte[] bytes)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var encoded=new StringBuilder();
        for(int bit=0;bit<bytes.Length*8;bit+=5) {
            int digit=0;
            for(int offset=0;offset<5;offset++) {
                int index=bit+offset;digit<<=1;
                if(index<bytes.Length*8)digit|=(bytes[index/8]>>(7-index%8))&1;
            }
            encoded.Append(alphabet[digit]);
        }
        while(encoded.Length%8!=0)encoded.Append('=');
        return encoded.ToString();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RfcBitOracleAllBytesAndChunkBoundariesProduceIndependentCountedOwnersWithOneReservation(int optimization)
    {
        string[] plain=["","f","fo","foo","foob","fooba","foobar"];
        string[] rfc=["","MY======","MZXQ====","MZXW6===","MZXW6YQ=","MZXW6YTB","MZXW6YTBOI======"];
        var cases=plain.Select(Encoding.ASCII.GetBytes).ToList();
        for(int index=0;index<plain.Length;index++)Assert.Equal(rfc[index],Encode(cases[index]));
        cases.Add(Enumerable.Range(0,256).Select(value=>(byte)value).ToArray());
        var random=new Random(689513);
        foreach(int length in Enumerable.Range(0,26).Concat([31,32,33,63,64,65,127,128,129,255,257,511])) {
            byte[] bytes=new byte[length];random.NextBytes(bytes);cases.Add(bytes);
        }
        string data=string.Concat(cases.Select(Convert.ToHexString));
        string expected=string.Concat(cases.Select(Encode));
        Run(Allocator+$$"""
            int Digit(char value) nothrow { if(value<='9')return value-'0';return value-'A'+10; }
            int main() {
                int[{{cases.Count}}] lengths=[{{string.Join(',',cases.Select(value=>value.Length))}}];
                readonly(char)* data="{{data}}";readonly(char)* expected="{{expected}}";
                nuint rawOffset=0;nuint textOffset=0;char[511] raw;
                for(nuint row=0;row<{{cases.Count}};row++) {
                    nuint length=(nuint)lengths[row];nuint textLength=((length+4)/5)*8;
                    for(nuint index=0;index<length;index++)raw[index]=(char)((Digit(data[rawOffset+index*2])<<4)|Digit(data[rawOffset+index*2+1]));
                    {
                        int added=0;if(length!=0)added=1;
                        int before=allocations;String encoded=Base32Text::Encode(raw,length);
                        if(allocations!=before+added||encoded.Length!=textLength||encoded.Data[textLength]!='\0')return 1;
                        if(encoded.Capacity!=textLength)return 2;
                        for(nuint index=0;index<textLength;index++)if(encoded[index]!=expected[textOffset+index])return 3;
                        if(length!=0) {
                            char saved=raw[0];raw[0]='!';
                            for(nuint index=0;index<textLength;index++)if(encoded[index]!=expected[textOffset+index])return 4;
                            raw[0]=saved;
                        }
                        before=allocations;String decoded=Base32Text::Decode(encoded.Data,encoded.Length);
                        if(allocations!=before+added||decoded.Length!=length||decoded.Capacity!=length||decoded.Data[length]!='\0')return 5;
                        encoded.Clear();
                        for(nuint index=0;index<length;index++)if(decoded[index]!=raw[index])return 6;
                        String copy=decoded;String moved=move copy;copy=new();decoded.Clear();
                        if(moved.Length!=length||moved.Data[length]!='\0')return 7;
                        for(nuint index=0;index<length;index++)if(moved[index]!=raw[index])return 8;
                    }
                    if(allocations!=releases)return 9;
                    for(nuint index=0;index<length;index++)if((byte)raw[index]!=(byte)((Digit(data[rawOffset+index*2])<<4)|Digit(data[rawOffset+index*2+1])))return 10;
                    rawOffset+=length*2;textOffset+=textLength;
                }
                if(rawOffset!={{data.Length}}||textOffset!={{expected.Length}})return 11;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidCanonicalShapesAndLateSuffixesValidateBeforeResultAllocationWhileEmptyNeverAllocates(int optimization)
    {
        string[] invalid=["A","MY=====","my======","M0======","MZ======","MZXR====","MZXW7===","MZXW6YR=",
            "========","AAAAAA==","MY======MZXW6YTB","MY======MY======","MZXW6YTBMY=====!","MY=====\0"];
        string checks=string.Join('\n',invalid.Select(value=>$$"""
            deny=true;
            try { output=Base32Text::Decode({{Literal(value)}},{{value.Length}});return 1; }
            catch(ref Exception error) { if(!CString::Equals(error.GetMessage(),"Invalid Base32"))return 2;caught++; }
            deny=false;
            """));
        Run(Allocator+$$"""
            int main() {
                {
                    String output=s"unchanged";readonly(char)* original=output.Data;nuint capacity=output.Capacity;int caught=0;
                    {{checks}}
                    deny=true;
                    try { output=Base32Text::Encode(((nuint)1).reinterpret<readonly(char)*>(),(nuint)0-1);return 3; }
                    catch(ref Exception error) { if(!CString::Equals(error.GetMessage(),"Base32 length overflow"))return 4;caught++; }
                    deny=false;
                    if(caught!={{invalid.Length+1}}||output.Data!=original||output.Capacity!=capacity||!output.StartsWith("unchanged"))return 5;
                    char[4096] invalid;for(nuint index=0;index<4096;index++)invalid[index]='A';invalid[4095]='!';
                    int before=large;
                    deny=true;
                    try { output=Base32Text::Decode(invalid,4096);return 6; }
                    catch(ref Exception error) { if(!CString::Equals(error.GetMessage(),"Invalid Base32"))return 7; }
                    deny=false;
                    if(large!=before||output.Data!=original)return 8;
                    for(nuint index=0;index<4095;index++)if(invalid[index]!='A')return 9;
                    if(invalid[4095]!='!')return 10;
                    before=allocations;deny=true;
                    String encoded=Base32Text::Encode(((nuint)1).reinterpret<readonly(char)*>(),0);
                    String decoded=Base32Text::Decode(((nuint)1).reinterpret<readonly(char)*>(),0);deny=false;
                    if(!encoded.IsEmpty||!decoded.IsEmpty||encoded.Data[0]!='\0'||decoded.Data[0]!='\0'||allocations!=before)return 11;
                }
                if(allocations!=releases)return 12;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailuresPreserveDestinationAndInputAndRetryKeepsEmbeddedNullBytes(int optimization)
        =>Run(Allocator+"""
            int main() {
                char[3] raw=['A','\0','B'];char[8] text=['I','E','A','E','E','=','=', '='];
                {
                    String output=s"unchanged";readonly(char)* original=output.Data;nuint capacity=output.Capacity;
                    for(int mode=0;mode<2;mode++) {
                        int balance=allocations-releases;deny=true;
                        try {
                            if(mode==0)output=Base32Text::Encode(raw,3);else output=Base32Text::Decode(text,8);
                            return 1;
                        } catch(ref BadAllocException error) {}
                        deny=false;
                        if(allocations-releases!=balance||output.Data!=original||output.Capacity!=capacity||!output.StartsWith("unchanged"))return 2;
                        if(raw[0]!='A'||raw[1]!='\0'||raw[2]!='B')return 3;
                        for(nuint index=0;index<8;index++)if(text[index]!="IEAEE==="[index])return 4;
                        {
                            int before=allocations;String retry=new();
                            if(mode==0)retry=Base32Text::Encode(raw,3);else retry=Base32Text::Decode(text,8);
                            if(allocations!=before+1)return 5;
                            if(mode==0) { if(retry.Length!=8||!retry.StartsWith("IEAEE==="))return 6; }
                            else if(retry.Length!=3||retry[0]!='A'||retry[1]!='\0'||retry[2]!='B'||retry.Data[3]!='\0')return 7;
                        }
                        if(allocations-releases!=balance)return 8;
                    }
                    // The existing destination can also supply the borrowed input for a successful replacement.
                    output=Base32Text::Encode(output.Data,output.Length);
                    output=Base32Text::Decode(output.Data,output.Length);
                    if(output.Length!=9||!output.StartsWith("unchanged"))return 9;
                }
                if(allocations!=releases)return 10;return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\0","\\0")+"\"";
    private static void Run(string program,int optimization)
    {
        string[] names=["Encoding/Base32.gf","Encoding/Base32Text.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf",
            "IComparable.gf","Hashing/primitive_hashing.gf","Text/String.gf","Text/ByteSearch.gf","primitive_format.gf","Text/CString.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("base32-text.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Base32Text exit {result.ExitCode}: {result.StandardError}");
    }
}
