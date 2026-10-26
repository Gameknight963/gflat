using std::IO;
using std::Memory;
using std::Binary;
using std::Collections;
using std;

Vector<byte> ToUtf16Le(ref immutable(Vector<byte>) input)
{
    if(input.IsEmpty)return new Vector<byte>();
    Span<readonly(byte)> bytes=input.AsImmutable();
    nuint units=0;
    if(!UnicodeTranscoding::TryGetUtf16Length((readonly(char)*)bytes.Data!,bytes.Length,ref units))
        throw new Exception("Input is not valid UTF-8");
    if(units>((nuint)0-(nuint)1)/2)throw new Exception("Output length overflow");
    Vector<ushort> wide=new(); wide.Reserve(units);
    wide.Resize(units,(ushort)0);
    nuint written=0;
    UnicodeTranscoding::TryUtf8ToUtf16((readonly(char)*)bytes.Data!,bytes.Length,wide.AsSpan().Data!,units,ref written);
    Vector<byte> result=new(); result.Reserve(units*2);
    result.Resize(units*2,(byte)0);
    Span<byte> output=result.AsSpan();
    for(nuint i=0;i<units;i++)
        BinaryPrimitives::TryWriteUInt16LittleEndian(output.Data!+i*2,2,wide[i]);
    return move result;
}

Vector<byte> FromUtf16Le(ref immutable(Vector<byte>) input)
{
    if(input.IsEmpty)return new Vector<byte>();
    if(input.Count%2!=0)throw new Exception("UTF-16LE input has an odd byte count");
    Span<readonly(byte)> bytes=input.AsImmutable();
    nuint units=bytes.Length/2;
    Vector<ushort> wide=new(); wide.Reserve(units);
    for(nuint i=0;i<units;i++) {
        ushort value=0;
        BinaryPrimitives::TryReadUInt16LittleEndian(bytes.Data!+i*2,2,ref value);
        wide.Add(value);
    }
    nuint required=0;
    if(!UnicodeTranscoding::TryGetUtf8Length(wide.AsImmutable().Data!,units,ref required))
        throw new Exception("Input is not valid UTF-16LE");
    Vector<byte> result=new(); result.Reserve(required);
    result.Resize(required,(byte)0);
    nuint written=0;
    UnicodeTranscoding::TryUtf16ToUtf8(wide.AsImmutable().Data!,units,(char*)result.AsSpan().Data!,required,ref written);
    return move result;
}

int main(int argc,char** argv)
{
    if(argc!=4||(!CString::Equals(argv[1],"utf8-to-utf16le")&&!CString::Equals(argv[1],"utf16le-to-utf8"))) {
        Console::Error.WriteLine("Usage: transcode <utf8-to-utf16le|utf16le-to-utf8> <input> <output>");
        return 2;
    }
    try {
        Vector<byte> input=FileBytes::ReadAll(argv[2]);
        Vector<byte> output;
        if(CString::Equals(argv[1],"utf8-to-utf16le"))output=ToUtf16Le(ref input);
        else output=FromUtf16Le(ref input);
        FileBytes::WriteAll(argv[3],ref output);
    }
    catch(ref Exception error) { Console::Error.WriteLine(error.GetMessage()); return 1; }
    return 0;
}
