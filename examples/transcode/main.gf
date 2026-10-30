using Standard::Text;
using Standard::IO;
using Standard::Memory;
using Standard::Binary;
using Standard::Collections;
using Standard;

Vector<byte> ToUtf16Le(ref immutable(Vector<byte>) input)
{
    if(input.IsEmpty)return new Vector<byte>();
    Span<readonly(byte)> bytes=input.AsImmutable();
    nuint units=0;
    if(!UnicodeTranscoding::TryGetUtf16Length((bytes.Data!).reinterpret<readonly(char)*>(),bytes.Length,ref units))
        throw new Exception("Input is not valid UTF-8");
    if(units>((nuint)0-(nuint)1)/2)throw new Exception("Output length overflow");
    nuint required=units*2;
    Vector<byte> result=new(); result.Reserve(required); result.Resize(required,(byte)0);
    Span<byte> output=result.AsSpan();
    nuint position=0; nuint produced=0; ushort[2] wide;
    // Complete validation and sizing above prove these per-scalar operations fit.
    while(position<bytes.Length) {
        uint scalar=0u; nuint consumed=0; nuint encoded=0;
        Utf8::TryDecode((bytes.Data!).reinterpret<readonly(char)*>()+position,bytes.Length-position,&scalar,&consumed);
        Utf16::TryEncode(scalar,wide,2,&encoded);
        for(nuint i=0;i<encoded;i++) {
            BinaryPrimitives::TryWriteUInt16LittleEndian(output.Data!+produced,2,wide[i]);
            produced+=2;
        }
        position+=consumed;
    }
    return move result;
}

// Decode one serialized scalar with bytewise loads; no input alignment assumption.
bool TryDecodeUtf16Le(readonly(byte)* data,nuint count,uint* scalar,nuint* consumed) nothrow
{
    *scalar=0u; *consumed=0;
    ushort first=0;
    if(!BinaryPrimitives::TryReadUInt16LittleEndian(data,count,ref first))return false;
    ushort[2] wide; wide[0]=first; nuint available=1;
    if(first>=(ushort)0xd800 && first<=(ushort)0xdbff && count>=4) {
        ushort second=0;
        BinaryPrimitives::TryReadUInt16LittleEndian(data+2,count-2,ref second);
        wide[1]=second; available=2;
    }
    nuint units=0;
    if(!Utf16::TryDecode(wide,available,scalar,&units))return false;
    *consumed=units*2;
    return true;
}

Vector<byte> FromUtf16Le(ref immutable(Vector<byte>) input)
{
    if(input.IsEmpty)return new Vector<byte>();
    if(input.Count%2!=0)throw new Exception("UTF-16LE input has an odd byte count");
    Span<readonly(byte)> bytes=input.AsImmutable();
    nuint position=0; nuint required=0; nuint maximum=(nuint)0-(nuint)1;
    while(position<bytes.Length) {
        uint scalar=0u; nuint consumed=0;
        if(!TryDecodeUtf16Le(bytes.Data!+position,bytes.Length-position,&scalar,&consumed))
            throw new Exception("Input is not valid UTF-16LE");
        nuint encoded=Utf8::EncodedLength(scalar);
        if(encoded>maximum-required)throw new Exception("Output length overflow");
        required+=encoded; position+=consumed;
    }
    Vector<byte> result=new(); result.Reserve(required); result.Resize(required,(byte)0);
    Span<byte> output=result.AsSpan(); position=0; nuint produced=0;
    // Input remains unchanged; the validated length proves every output write fits.
    while(position<bytes.Length) {
        uint scalar=0u; nuint consumed=0; nuint encoded=0;
        TryDecodeUtf16Le(bytes.Data!+position,bytes.Length-position,&scalar,&consumed);
        Utf8::TryEncode(scalar,(output.Data!).reinterpret<char*>()+produced,required-produced,&encoded);
        produced+=encoded; position+=consumed;
    }
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
