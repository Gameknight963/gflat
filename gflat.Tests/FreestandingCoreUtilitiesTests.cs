using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace gflat.Tests;

public class FreestandingCoreUtilitiesTests
{
    private const string ProgramSource = """
        extern ulong formatter_input() nothrow;
        extern void byte_search_input(byte* data) nothrow;
        int main() {
            if (!std::Ascii::IsHexDigit('f')) return 1;
            if (std::Ascii::IsAscii((char)255)) return 2;
            char[64] input; char[64] decoded; char[32] encoded;
            nuint length = 0; nuint consumed = 0; uint scalar = 0u;
            if (!std::Utf8::TryEncode(0x1f600u, input, 64, &length)) return 3;
            if (!std::Utf8::TryDecode(input, length, &scalar, &consumed)) return 4;
            if (scalar != 0x1f600u || consumed != length) return 5;
            nuint encodedLength = 0; nuint decodedLength = 0;
            if (!std::Base64::TryEncode(input, length, encoded, 32, ref encodedLength)) return 6;
            if (!std::Base64::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength)) return 7;
            if (decodedLength != length) return 8;
            for (nuint i = 0; i < length; i++) if (input[i] != decoded[i]) return 9;
            char[2] urlBinary=[(char)((formatter_input()>>63)+250ul),(char)255]; char[3] urlText; char[2] urlBytes;
            nuint urlWritten=0;
            if(!std::Base64Url::TryEncode(urlBinary,2,urlText,3,ref urlWritten)||urlWritten!=3
                ||urlText[0]!='-'||urlText[1]!='_'||urlText[2]!='8')return 162;
            if(!std::Base64Url::TryDecode(urlText,3,urlBytes,2,ref urlWritten)||urlWritten!=2
                ||urlBytes[0]!=urlBinary[0]||urlBytes[1]!=urlBinary[1])return 163;
            if (!std::Hex::TryEncode(decoded, decodedLength, encoded, 32, ref encodedLength)) return 10;
            if (encodedLength != (nuint)8) return 11;
            readonly(char)* expected = "F09F9880";
            for (nuint i = 0; i < encodedLength; i++) if (encoded[i] != expected[i]) return 12;
            if (std::Ascii::ToLower(encoded[0]) != 'f') return 13;
            if (!std::Hex::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength)) return 14;
            if (!std::Utf8::IsValid(decoded, decodedLength)) return 15;
            if (std::BitOperations::PopCount(0xffu) != 8) return 16;
            if (std::BitOperations::RotateRight(1ul, 64) != 1ul) return 17;
            if (std::Hashing::Bytes("hello", 5) != (nuint)1335831723u) return 18;
            if (!std::PercentEncoding::TryEncode("a +", 3, encoded, 32, ref encodedLength) || encodedLength != (nuint)7) return 19;
            if (!std::PercentEncoding::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength) || decodedLength != (nuint)3) return 20;
            if (decoded[0] != 'a' || decoded[1] != ' ' || decoded[2] != '+') return 21;
            byte[9] binary; ulong integer = 0ul;
            if (!std::BinaryPrimitives::TryWriteUInt64BigEndian(&binary[1], 8, 0xfedcba9876543210ul)) return 22;
            if (!std::BinaryPrimitives::TryReadUInt64BigEndian(&binary[1], 8, ref integer) || integer != 0xfedcba9876543210ul) return 23;
            std::CRC32 crc = new(); crc.Append("123456789", 9);
            if (crc.GetValue() != 0xcbf43926u) return 24;
            std::Adler32 adler = new(); adler.Append("Wikipedia", 9);
            if (adler.GetValue() != 0x11e60398u) return 25;
            std::Utf8Decoder decoder = new();
            if (decoder.Read(input, 2, false, &scalar, &consumed) != std::Utf8Decoder::Status::NeedMore || consumed != 2) return 26;
            if (decoder.Read(&input[2], 2, true, &scalar, &consumed) != std::Utf8Decoder::Status::Scalar || scalar != 0x1f600u) return 27;
            if (decoder.Read(input, 0, true, &scalar, &consumed) != std::Utf8Decoder::Status::End) return 28;
            byte[10] packet; packet[0]=(byte)165; packet[9]=(byte)90;
            std::BinaryBufferWriter writer = new(&packet[1],8);
            std::BinaryBufferWriter payload = new();
            if (!writer.TryWriteUInt16BigEndian((ushort)4) || !writer.TryWriteSlice(4,ref payload)) return 29;
            if (!payload.TryWriteInt32LittleEndian(-42) || payload.TryWriteByte((byte)0)) return 30;
            if (!writer.TryWriteUInt16BigEndian((ushort)48879) || writer.Position!=8 || writer.Remaining!=0) return 31;
            if (packet[0]!=(byte)165 || packet[9]!=(byte)90 || packet[1]!=(byte)0 || packet[2]!=(byte)4 || packet[3]!=(byte)214 || packet[4]!=(byte)255 || packet[7]!=(byte)190 || packet[8]!=(byte)239) return 32;
            std::BinaryBufferReader reader = new(&packet[1],8);
            std::BinaryBufferReader part = new(); ushort field=0; int signedValue=0; byte extra=(byte)99;
            if (!reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)4 || !reader.TryReadSlice((nuint)field,ref part)) return 33;
            if (!part.TryReadInt32LittleEndian(ref signedValue) || signedValue!=-42 || part.TryReadByte(ref extra) || extra!=(byte)0) return 34;
            if (!reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)48879 || reader.Position!=8 || reader.Remaining!=0) return 35;
            if (reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)0 || reader.Position!=8) return 36;
            if (packet[0]!=(byte)165 || packet[9]!=(byte)90) return 37;
            byte[4] bulk=[(byte)11,(byte)22,(byte)33,(byte)44];
            std::BinaryBufferWriter bulkWriter=new(bulk,4);
            if(!bulkWriter.TrySkip(1)||!bulkWriter.TryWriteBytes(bulk,3)||bulk[2]!=(byte)22||bulk[3]!=(byte)33)return 160;
            std::BinaryBufferReader bulkReader=new(bulk,4);
            if(!bulkReader.TryReadBytes(&bulk[1],3)||bulk[2]!=(byte)11||bulk[3]!=(byte)22)return 161;
            ushort[7] unicode = [(ushort)65, (ushort)0, (ushort)0x20ac, (ushort)0xd83d, (ushort)0xde00, (ushort)0xfeff, (ushort)0xffff];
            nuint unicodeBytes = 0; nuint unicodeUnits = 0; nuint unicodeScalars = 0;
            if (!std::Utf16::TryCount(unicode, 7, &unicodeScalars) || unicodeScalars != 6) return 38;
            if (!std::UnicodeTranscoding::TryGetUtf8Length(unicode, 7, ref unicodeBytes) || unicodeBytes != 15) return 39;
            if (!std::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, input, 64, ref length) || length != 15) return 40;
            if (input[0] != 'A' || input[1] != '\0' || (byte)input[2] != (byte)0xe2 || (byte)input[5] != (byte)0xf0) return 41;
            if (!std::UnicodeTranscoding::TryGetUtf16Length(input, length, ref unicodeUnits) || unicodeUnits != 7) return 43;
            ushort[9] converted;
            for (nuint i = 0; i < 9; i++) converted[i] = (ushort)123;
            if (!std::UnicodeTranscoding::TryUtf8ToUtf16(input, length, &converted[1], 7, ref unicodeUnits) || unicodeUnits != 7) return 44;
            if (converted[0] != (ushort)123 || converted[8] != (ushort)123) return 45;
            for (nuint i = 0; i < 7; i++) if (unicode[i] != converted[i + 1]) return 46;
            for (nuint i = 0; i < 64; i++) decoded[i] = 'Z';
            if (std::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, decoded, 14, ref decodedLength) || decodedLength != 0) return 47;
            for (nuint i = 0; i < 64; i++) if (decoded[i] != 'Z') return 48;
            unicode[6] = (ushort)0xd800;
            if (std::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, decoded, 64, ref decodedLength) || decodedLength != 0) return 49;
            for (nuint i = 0; i < 64; i++) if (decoded[i] != 'Z') return 50;
            for (nuint i = 0; i < 9; i++) converted[i] = (ushort)123;
            if (std::UnicodeTranscoding::TryUtf8ToUtf16(input, length, &converted[1], 6, ref unicodeUnits) || unicodeUnits != 0) return 51;
            for (nuint i = 0; i < 9; i++) if (converted[i] != (ushort)123) return 52;
            input[length] = (char)255;
            if (std::UnicodeTranscoding::TryUtf8ToUtf16(input, length + 1, &converted[1], 7, ref unicodeUnits) || unicodeUnits != 0) return 53;
            for (nuint i = 0; i < 9; i++) if (converted[i] != (ushort)123) return 54;
            char[22] decimalText;
            for (nuint i = 0; i < 22; i++) decimalText[i] = 'Z';
            ulong runtimeInput = formatter_input();
            if (!std::IntegerFormatting::TryFormat((long)runtimeInput, &decimalText[1], 20, ref length) || length != 20) return 55;
            expected = "-9223372036854775808";
            for (nuint i = 0; i < length; i++) if (decimalText[i + 1] != expected[i]) return 56;
            if (decimalText[0] != 'Z' || decimalText[21] != 'Z') return 57;
            ulong unsignedMaximum = runtimeInput + runtimeInput - 1ul;
            if (!std::IntegerFormatting::TryFormat(unsignedMaximum, &decimalText[1], 20, ref length) || length != 20) return 58;
            expected = "18446744073709551615";
            for (nuint i = 0; i < length; i++) if (decimalText[i + 1] != expected[i]) return 59;
            if (decimalText[0] != 'Z' || decimalText[21] != 'Z') return 60;
            if (std::IntegerFormatting::TryFormat(unsignedMaximum, &decimalText[1], 19, ref length) || length != 0) return 61;
            for (nuint i = 0; i < 20; i++) if (decimalText[i + 1] != expected[i]) return 62;
            byte[64] searchInput; byte[5] searchPattern;
            byte_search_input((byte*)searchInput);
            searchPattern[0]=(byte)1;searchPattern[1]=(byte)0;searchPattern[2]=(byte)1;
            searchPattern[3]=(byte)0;searchPattern[4]=(byte)1;
            if(std::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=0) return 63;
            if(std::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=58) return 64;
            searchPattern[2]=(byte)2;
            if(std::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=64) return 65;
            if(std::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=64) return 66;
            if(std::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,0)!=0) return 67;
            if(std::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,0)!=64) return 68;
            char* cText=(char*)searchInput;
            if(std::CString::Length(cText)!=1) return 69;
            if(std::CString::Compare(cText,&cText[2])!=0 || !std::CString::Equals(cText,&cText[2])) return 70;
            if(!std::CString::StartsWith(cText,&cText[2]) || !std::CString::EndsWith(cText,&cText[2])) return 71;
            length=99;
            if(std::CString::TryGetLength(cText,1,ref length) || length!=0) return 72;
            if(!std::CString::TryGetLength(cText,2,ref length) || length!=1) return 73;
            searchInput[1]=(byte)2;searchInput[2]=(byte)0;
            if(std::CString::Length(cText)!=2 || std::CString::Compare(cText,&cText[2])!=1) return 74;
            if(std::CString::Equals(cText,&cText[2]) || std::CString::EndsWith(cText,"x")) return 75;
            if(!std::CString::EndsWith(cText,&cText[1]) || !std::CString::StartsWith(cText,&cText[2])) return 76;
            byte[66] memoryOutput;
            nuint moveCount=(nuint)(runtimeInput>>63)+62;
            Memory::Fill((void*)&memoryOutput[0],(byte)165,66);
            Memory::Copy((void*)&memoryOutput[1],(readonly(void)*)&searchInput[0],64);
            if(memoryOutput[0]!=(byte)165 || memoryOutput[65]!=(byte)165) return 77;
            if(Memory::Compare((readonly(void)*)&memoryOutput[1],(readonly(void)*)&searchInput[0],64)!=0) return 78;
            if(!Memory::Equal((readonly(void)*)&memoryOutput[1],(readonly(void)*)&searchInput[0],64)) return 79;
            // Higher destination requires backwards copying; lower requires forwards.
            Memory::Move((void*)&memoryOutput[2],(readonly(void)*)&memoryOutput[1],moveCount);
            if(!Memory::Equal((readonly(void)*)&memoryOutput[2],(readonly(void)*)&searchInput[0],moveCount)) return 80;
            Memory::Move((void*)&memoryOutput[1],(readonly(void)*)&memoryOutput[2],moveCount);
            if(!Memory::Equal((readonly(void)*)&memoryOutput[1],(readonly(void)*)&searchInput[0],moveCount)) return 81;
            Memory::Move((void*)&memoryOutput[1],(readonly(void)*)&memoryOutput[1],moveCount);
            if(memoryOutput[0]!=(byte)165 || memoryOutput[65]!=(byte)165) return 82;
            memoryOutput[1]=(byte)255;
            if(Memory::Compare((readonly(void)*)&memoryOutput[1],(readonly(void)*)&searchInput[0],1)!=255-(int)searchInput[0]) return 83;
            if(Memory::Compare((readonly(void)*)&searchInput[0],(readonly(void)*)&memoryOutput[1],1)!=(int)searchInput[0]-255) return 84;
            if(Memory::Equal((readonly(void)*)&memoryOutput[1],(readonly(void)*)&searchInput[0],moveCount)) return 85;
            Memory::Copy((void*)1,(readonly(void)*)1,0);
            Memory::Move((void*)1,(readonly(void)*)1,0);
            Memory::Fill((void*)1,(byte)0,0);
            if(Memory::Compare((readonly(void)*)1,(readonly(void)*)1,0)!=0 || !Memory::Equal((readonly(void)*)1,(readonly(void)*)1,0)) return 86;
            byte_search_input((byte*)searchInput);
            char[388] jsonOutput;
            for(nuint i=0;i<388;i++)jsonOutput[i]='Z';
            nuint jsonNeeded=0; nuint jsonWritten=0;
            if(!std::JsonString::TryGetQuotedLength((char*)searchInput,64,ref jsonNeeded)||jsonNeeded!=386) return 87;
            if(!std::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],jsonNeeded,ref jsonWritten)||jsonWritten!=386) return 88;
            if(jsonOutput[0]!='Z'||jsonOutput[387]!='Z'||jsonOutput[1]!='"'||jsonOutput[386]!='"') return 89;
            for(nuint i=0;i<64;i++) {
                nuint at=2+i*6;
                if(jsonOutput[at]!='\\'||jsonOutput[at+1]!='u'||jsonOutput[at+2]!='0'||jsonOutput[at+3]!='0'||jsonOutput[at+4]!='0') return 90;
                char digit='0';if(i%2==0)digit='1';
                if(jsonOutput[at+5]!=digit) return 91;
            }
            searchInput[0]=(byte)'"';searchInput[1]=(byte)'\\';searchInput[2]=(byte)'\n';
            if(!std::JsonString::TryGetQuotedLength((char*)searchInput,64,ref jsonNeeded)||jsonNeeded!=374) return 92;
            if(!std::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],jsonNeeded,ref jsonWritten)||jsonWritten!=374) return 93;
            readonly(char)* jsonPrefix="\\\"\\\\\\n\\u0000";
            for(nuint i=0;i<12;i++)if(jsonOutput[i+2]!=jsonPrefix[i]) return 94;
            for(nuint i=0;i<388;i++)jsonOutput[i]='Z';
            if(std::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],jsonNeeded-1,ref jsonWritten)||jsonWritten!=0) return 95;
            for(nuint i=0;i<388;i++)if(jsonOutput[i]!='Z') return 96;
            searchInput[63]=(byte)255;
            if(std::JsonString::TryGetQuotedLength((char*)searchInput,64,ref jsonNeeded)||jsonNeeded!=0) return 97;
            if(std::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],386,ref jsonWritten)||jsonWritten!=0) return 98;
            for(nuint i=0;i<388;i++)if(jsonOutput[i]!='Z') return 99;
            byte[12] variable;
            for(nuint i=0;i<12;i++)variable[i]=(byte)90;
            nuint variableLength=0; nuint variableConsumed=0;
            if(std::VarInt::GetUInt64Length(unsignedMaximum)!=10) return 100;
            if(std::VarInt::TryWriteUInt64(&variable[1],9,unsignedMaximum,ref variableLength)||variableLength!=0) return 101;
            for(nuint i=0;i<12;i++)if(variable[i]!=(byte)90) return 102;
            if(!std::VarInt::TryWriteUInt64(&variable[1],10,unsignedMaximum,ref variableLength)||variableLength!=10) return 103;
            if(variable[0]!=(byte)90||variable[11]!=(byte)90||variable[10]!=(byte)1) return 104;
            for(nuint i=1;i<10;i++)if(variable[i]!=(byte)255) return 105;
            ulong variableValue=42ul;
            if(std::VarInt::ReadUInt64(&variable[1],9,ref variableValue,ref variableConsumed)!=std::VarInt::Status::NeedMore||variableValue!=42ul||variableConsumed!=0) return 106;
            if(std::VarInt::ReadUInt64(&variable[1],11,ref variableValue,ref variableConsumed)!=std::VarInt::Status::Done||variableValue!=unsignedMaximum||variableConsumed!=10) return 107;
            variable[10]=(byte)2;
            if(std::VarInt::ReadUInt64(&variable[1],10,ref variableValue,ref variableConsumed)!=std::VarInt::Status::Invalid||variableValue!=unsignedMaximum||variableConsumed!=0) return 108;
            uint narrowVariable=(uint)(runtimeInput>>32)|300u; uint narrowDecoded=0u;
            if(!std::VarInt::TryWriteUInt32(&variable[1],10,narrowVariable,ref variableLength)||variableLength!=std::VarInt::GetUInt32Length(narrowVariable)) return 109;
            if(std::VarInt::ReadUInt32(&variable[1],variableLength,ref narrowDecoded,ref variableConsumed)!=std::VarInt::Status::Done||narrowDecoded!=narrowVariable||variableConsumed!=variableLength) return 110;
            uint floatBits=(uint)(runtimeInput>>32)|0x7f800001u;
            ulong doubleBits=runtimeInput|0x7ff0000000000001ul;
            float floatingValue=*((float*)&floatBits); double doubleValue=*((double*)&doubleBits);
            byte[14] ieee; ieee[0]=(byte)90; ieee[13]=(byte)165;
            std::BinaryBufferWriter ieeeWriter=new(&ieee[1],12);
            if(!ieeeWriter.TryWriteFloatLittleEndian(floatingValue)||!ieeeWriter.TryWriteDoubleBigEndian(doubleValue)||ieeeWriter.Position!=12) return 111;
            if(ieee[0]!=(byte)90||ieee[13]!=(byte)165||ieee[1]!=(byte)1||ieee[4]!=(byte)255||ieee[5]!=(byte)255||ieee[6]!=(byte)240||ieee[12]!=(byte)1) return 112;
            std::BinaryBufferReader ieeeReader=new(&ieee[1],12);
            float decodedFloat=(float)0; double decodedDouble=(double)0;
            if(!ieeeReader.TryReadFloatLittleEndian(ref decodedFloat)||!ieeeReader.TryReadDoubleBigEndian(ref decodedDouble)||ieeeReader.Position!=12) return 113;
            if(*((uint*)&decodedFloat)!=floatBits||*((ulong*)&decodedDouble)!=doubleBits) return 114;
            for(nuint i=0;i<22;i++)decimalText[i]='Z';
            if(!std::IntegerFormatting::TryFormatHex(unsignedMaximum,&decimalText[1],20,ref length,20,false)||length!=20) return 115;
            for(nuint i=1;i<5;i++)if(decimalText[i]!='0') return 116;
            for(nuint i=5;i<21;i++)if(decimalText[i]!='f') return 117;
            if(decimalText[0]!='Z'||decimalText[21]!='Z') return 118;
            if(std::IntegerFormatting::TryFormatHex(unsignedMaximum,&decimalText[1],19,ref length,20,false)||length!=0) return 119;
            for(nuint i=1;i<5;i++)if(decimalText[i]!='0') return 120;
            for(nuint i=5;i<21;i++)if(decimalText[i]!='f') return 121;
            ulong parsedHex=0ul;
            if(!std::TryParseHexULong(&decimalText[1],20,&parsedHex)||parsedHex!=unsignedMaximum) return 122;
            decimalText[21]='\0';
            if(!std::TryParseHexULong(&decimalText[1],&parsedHex)||parsedHex!=unsignedMaximum) return 123;
            decimalText[20]='g';
            if(std::TryParseHexULong(&decimalText[1],20,&parsedHex)||parsedHex!=0ul) return 124;
            decimalText[20]='f';
            uint hexadecimalWord=(uint)(runtimeInput>>32)|0x12345678u;
            if(!std::IntegerFormatting::TryFormatHex(hexadecimalWord,&decimalText[1],20,ref length,8,true)||length!=8) return 125;
            uint parsedWord=0u;
            if(!std::TryParseHexUInt(&decimalText[1],length,&parsedWord)||parsedWord!=hexadecimalWord) return 126;
            byte parsedByte=(byte)7;
            if(std::TryParseHexByte(&decimalText[1],length,&parsedByte)||parsedByte!=(byte)0) return 127;
            if(!std::TryParseHexByte("0xFf",&parsedByte)||parsedByte!=(byte)255) return 128;
            ushort parsedShort=(ushort)0;
            if(!std::TryParseHexUShort("0Xffff",6,&parsedShort)||parsedShort!=(ushort)65535) return 129;
            nuint nativeHex=(nuint)runtimeInput|(nuint)255;
            if(!std::IntegerFormatting::TryFormatHex(nativeHex,&decimalText[1],20,ref length)) return 130;
            nuint parsedNative=0;
            if(!std::TryParseHexNUInt(&decimalText[1],length,&parsedNative)||parsedNative!=nativeHex) return 131;
            char[6] booleanText=['T','r','U','e','\0','Z'];
            booleanText[0]=(char)((runtimeInput>>63)+(ulong)'S');
            bool parsedBoolean=false;
            if(!std::TryParseBool(booleanText,4,&parsedBoolean)||!parsedBoolean) return 132;
            if(!std::TryParseBool(booleanText,&parsedBoolean)||!parsedBoolean) return 133;
            booleanText[0]=(char)((runtimeInput>>63)+(ulong)'e');
            booleanText[1]='A';booleanText[2]='l';booleanText[3]='S';booleanText[4]='e';booleanText[5]='\0';
            if(!std::TryParseBool(booleanText,5,&parsedBoolean)||parsedBoolean) return 134;
            booleanText[4]=(char)(runtimeInput>>56);
            if(std::TryParseBool(booleanText,5,&parsedBoolean)||parsedBoolean) return 135;
            parsedBoolean=true;
            if(std::TryParseBool((readonly(char)*)1,0,&parsedBoolean)||parsedBoolean) return 136;
            char[3] copySource=[(char)((runtimeInput>>56)|1ul),'A','\0'];
            cText=copySource;
            char[7] copied;
            for(nuint i=0;i<7;i++) copied[i]='Z';
            if(std::CString::TryCopy(cText,&copied[1],2)) return 137;
            for(nuint i=0;i<7;i++) if(copied[i]!='Z') return 138;
            if(!std::CString::TryCopy(cText,&copied[1],3)||!std::CString::Equals(cText,&copied[1])) return 139;
            if(!std::CString::TryCopy(&copied[1],&copied[2],3)||!std::CString::Equals(cText,&copied[2])) return 140;
            if(!std::CString::TryCopy(&copied[2],&copied[1],3)||!std::CString::Equals(cText,&copied[1])) return 141;
            if(copied[0]!='Z'||copied[5]!='Z'||copied[6]!='Z') return 142;
            if(std::CString::TryCopy((readonly(char)*)1,(char*)1,0)) return 143;
            if(std::VarInt::ZigZagEncode((long)runtimeInput)!=unsignedMaximum ||
                std::VarInt::ZigZagDecode(unsignedMaximum)!=(long)runtimeInput) return 144;
            if(std::VarInt::ZigZagEncode((int)(runtimeInput>>32))!=(uint)unsignedMaximum ||
                std::VarInt::ZigZagDecode((uint)unsignedMaximum)!=(int)(runtimeInput>>32)) return 145;
            extralong wideBits=(extralong)runtimeInput<<64;
            char[38] wideHex;
            for(nuint i=0;i<38;i++)wideHex[i]='Z';
            if(!std::IntegerFormatting::TryFormatHex(wideBits,&wideHex[1],36,ref length,36,false)||length!=36) return 146;
            if(wideHex[0]!='Z'||wideHex[37]!='Z'||wideHex[5]!='8') return 147;
            for(nuint i=1;i<5;i++)if(wideHex[i]!='0') return 148;
            for(nuint i=6;i<37;i++)if(wideHex[i]!='0') return 149;
            if(std::IntegerFormatting::TryFormatHex(wideBits,&wideHex[1],35,ref length,36,false)||length!=0) return 150;
            if(wideHex[0]!='Z'||wideHex[37]!='Z'||wideHex[5]!='8') return 151;
            for(nuint i=1;i<5;i++)if(wideHex[i]!='0') return 152;
            for(nuint i=6;i<37;i++)if(wideHex[i]!='0') return 153;
            return 42;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StackOnlyCodecsRunWithoutLibcOrCppRuntime(int optimization)
    {
        // The test bootstrap below implements the x64 Windows/Linux process ABI.
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())) return;

        var sources = new[] { "Ascii.gf", "Utf8.gf", "Utf16.gf", "UnicodeTranscoding.gf", "Hex.gf", "Base64.gf", "Base64Url.gf", "BitOperations.gf", "Hashing.gf",
            "PercentEncoding.gf", "BinaryPrimitives.gf", "Checksums/CRC32.gf", "Checksums/Adler32.gf", "Utf8Decoder.gf",
            "BinaryBufferReader.gf", "BinaryBufferWriter.gf", "IntegerFormatting.gf", "HexIntegerParsing.gf", "BooleanParsing.gf", "ByteSearch.gf", "CString.gf", "Memory/Bytes.gf", "JsonString.gf", "VarInt.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", ProgramSource));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "%Exception", "@__gflat_eh_", "personality", "gflat$Memory$Allocate", "gflat$Memory$Free", "@malloc", "@free", "@__gflat_stdio_" })
            Assert.DoesNotContain(forbidden, ir);

        string directory = Path.Combine(Path.GetTempPath(), "gflat-core-bare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "core.ll");
            string output = Path.Combine(directory, "core.exe");
            var arguments = new List<string> { input, "-O" + optimization, "-nostdlib", "-fno-stack-protector", "-o", output };
            // Keep actual formatter inputs opaque to LLVM. A separate assembly
            // object prevents constant propagation from erasing the digit/copy loops.
            string formatterInput = Path.Combine(directory, "formatter-input.S");
            File.WriteAllText(formatterInput, """
                .text
                .global formatter_input
                formatter_input:
                    movabs $0x8000000000000000, %rax
                    ret
                """);
            // Populate search bytes outside LLVM's module, retaining real search
            // loops at O2 instead of merely checking constant-folded answers.
            string inputRegister = OperatingSystem.IsWindows() ? "%rcx" : "%rdi";
            File.AppendAllText(formatterInput, $$"""

                .global byte_search_input
                byte_search_input:
                    mov {{inputRegister}}, %r10
                    xor %eax, %eax
                .Lsearch_fill:
                    movb $1, (%r10,%rax)
                    inc %rax
                    movb $0, (%r10,%rax)
                    inc %rax
                    cmp $64, %rax
                    jne .Lsearch_fill
                    ret
                """);
            if (OperatingSystem.IsLinux()) File.AppendAllText(formatterInput, "\n.section .note.GNU-stack,\"\",@progbits\n");
            arguments.Add(formatterInput);
            if (OperatingSystem.IsWindows())
            {
                // LLVM emits _fltused for Windows FP code even without CRT calls.
                // Standalone startup supplies the marker; no x87/printf setup here.
                // https://discourse.llvm.org/t/lld-link-fails-to-link-uefi-binary-because-of-undefined-symbol-fltused/52454
                ir += """

                    ; Windows x64 ABI marker, normally supplied by the CRT.
                    ; These bit-preserving codecs require no FP initialization.
                    @_fltused = global i32 0
                    declare void @ExitProcess(i32) noreturn
                    define void @bare_entry() {
                        %status = call i32 @main()
                        call void @ExitProcess(i32 %status)
                        unreachable
                    }
                    """;
                arguments.AddRange(["-Xlinker", "/NODEFAULTLIB", "-Xlinker", "/ENTRY:bare_entry", "-Xlinker", "/SUBSYSTEM:CONSOLE", "-lkernel32"]);
                arguments.AddRange(NativeRuntime.HostArguments());
            }
            else
            {
                // Align the kernel-supplied stack before making an ordinary ABI call.
                string startup = Path.Combine(directory, "start.S");
                File.WriteAllText(startup, """
                    .text
                    .global bare_entry
                    bare_entry:
                        xor %ebp, %ebp
                        and $-16, %rsp
                        call main
                        mov %eax, %edi
                        mov $60, %eax
                        syscall
                    .section .note.GNU-stack,"",@progbits
                    """);
                arguments.AddRange([startup, "-static", "-no-pie", "-Wl,-e,bare_entry", "-Wl,--gc-sections"]);
            }
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Run(NativeToolchain.FindClang(), arguments, 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(output, []).ExitCode);
            byte[] binary = File.ReadAllBytes(output);
            if (OperatingSystem.IsWindows()) AssertOnlyWindowsExitImport(binary);
            else AssertNoElfDynamicLoader(binary);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void AssertOnlyWindowsExitImport(byte[] binary)
    {
        using var stream = new MemoryStream(binary, writable: false);
        using var pe = new PEReader(stream);
        Assert.Null(pe.PEHeaders.CorHeader);
        var import = pe.PEHeaders.PEHeader!.ImportTableDirectory;
        Assert.NotEqual(0, import.RelativeVirtualAddress);
        int Offset(int rva)
        {
            var section = Assert.Single(pe.PEHeaders.SectionHeaders.Where(s => rva >= s.VirtualAddress && rva - s.VirtualAddress < Math.Max(s.VirtualSize, s.SizeOfRawData)));
            return section.PointerToRawData + rva - section.VirtualAddress;
        }
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(offset, 4));
        string CString(int rva)
        {
            int offset = Offset(rva), end = Array.IndexOf(binary, (byte)0, offset);
            Assert.True(end >= offset);
            return Encoding.ASCII.GetString(binary, offset, end - offset);
        }
        int descriptor = Offset(import.RelativeVirtualAddress);
        Assert.Equal("KERNEL32.dll", CString((int)U32(descriptor + 12)), ignoreCase: true);
        Assert.Equal(0u, U32(descriptor + 20 + 12)); // No second imported DLL.
        int lookupRva = (int)U32(descriptor);
        if (lookupRva == 0) lookupRva = (int)U32(descriptor + 16);
        int lookup = Offset(lookupRva);
        ulong symbol = BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(lookup, 8));
        Assert.Equal(0ul, symbol & (1ul << 63)); // Named import, not an ordinal.
        Assert.Equal("ExitProcess", CString(checked((int)symbol + 2)));
        Assert.Equal(0ul, BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(lookup + 8, 8)));
    }

    private static void AssertNoElfDynamicLoader(byte[] binary)
    {
        Assert.Equal(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1 }, binary[..6]);
        ulong headers = BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(32, 8));
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(54, 2));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(56, 2));
        for (int i = 0; i < count; i++)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(checked((int)headers + i * size), 4));
            Assert.NotEqual(2u, type); // PT_DYNAMIC
            Assert.NotEqual(3u, type); // PT_INTERP
        }
    }
}

