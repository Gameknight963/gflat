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
        int Sha256Proof() nothrow {
            std::Hashing::SHA256 hash = new();
            char[3] text = ['a', 'b', (char)(98ul + (formatter_input() >> 63))];
            if (!hash.TryAppend(text, 3)) return 1;
            byte[32] actual = hash.GetHash();
            byte[32] expected = [(byte)0xba,(byte)0x78,(byte)0x16,(byte)0xbf,(byte)0x8f,(byte)0x01,(byte)0xcf,(byte)0xea,
                (byte)0x41,(byte)0x41,(byte)0x40,(byte)0xde,(byte)0x5d,(byte)0xae,(byte)0x22,(byte)0x23,
                (byte)0xb0,(byte)0x03,(byte)0x61,(byte)0xa3,(byte)0x96,(byte)0x17,(byte)0x7a,(byte)0x9c,
                (byte)0xb4,(byte)0x10,(byte)0xff,(byte)0x61,(byte)0xf2,(byte)0x00,(byte)0x15,(byte)0xad];
            for (nuint i = 0; i < 32; i++) if (actual[i] != expected[i]) return 2;
            return 0;
        }
        int main() {
            if (!std::Text::Ascii::IsHexDigit('f')) return 1;
            if (std::Text::Ascii::IsAscii((char)255)) return 2;
            char[64] input; char[64] decoded; char[32] encoded;
            nuint length = 0; nuint consumed = 0; uint scalar = 0u;
            if (!std::Text::Utf8::TryEncode(0x1f600u, input, 64, &length)) return 3;
            if (!std::Text::Utf8::TryDecode(input, length, &scalar, &consumed)) return 4;
            if (scalar != 0x1f600u || consumed != length) return 5;
            nuint encodedLength = 0; nuint decodedLength = 0;
            if (!std::Encoding::Base64::TryEncode(input, length, encoded, 32, ref encodedLength)) return 6;
            if (!std::Encoding::Base64::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength)) return 7;
            if (decodedLength != length) return 8;
            for (nuint i = 0; i < length; i++) if (input[i] != decoded[i]) return 9;
            char[2] urlBinary=[(char)((formatter_input()>>63)+250ul),(char)255]; char[3] urlText; char[2] urlBytes;
            nuint urlWritten=0;
            if(!std::Encoding::Base64Url::TryEncode(urlBinary,2,urlText,3,ref urlWritten)||urlWritten!=3
                ||urlText[0]!='-'||urlText[1]!='_'||urlText[2]!='8')return 162;
            if(!std::Encoding::Base64Url::TryDecode(urlText,3,urlBytes,2,ref urlWritten)||urlWritten!=2
                ||urlBytes[0]!=urlBinary[0]||urlBytes[1]!=urlBinary[1])return 163;
            if (!std::Encoding::Hex::TryEncode(decoded, decodedLength, encoded, 32, ref encodedLength)) return 10;
            if (encodedLength != (nuint)8) return 11;
            readonly(char)* expected = "F09F9880";
            for (nuint i = 0; i < encodedLength; i++) if (encoded[i] != expected[i]) return 12;
            if (std::Text::Ascii::ToLower(encoded[0]) != 'f') return 13;
            if (!std::Encoding::Hex::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength)) return 14;
            if (!std::Text::Utf8::IsValid(decoded, decodedLength)) return 15;
            if (std::Numerics::BitOperations::PopCount(0xffu) != 8) return 16;
            if (std::Numerics::BitOperations::RotateRight(1ul, 64) != 1ul) return 17;
            if (std::Hashing::Bytes("hello", 5) != (nuint)1335831723u) return 18;
            if (!std::Encoding::PercentEncoding::TryEncode("a +", 3, encoded, 32, ref encodedLength) || encodedLength != (nuint)7) return 19;
            if (!std::Encoding::PercentEncoding::TryDecode(encoded, encodedLength, decoded, 64, ref decodedLength) || decodedLength != (nuint)3) return 20;
            if (decoded[0] != 'a' || decoded[1] != ' ' || decoded[2] != '+') return 21;
            byte[9] binary; ulong integer = 0ul;
            if (!std::Binary::BinaryPrimitives::TryWriteUInt64BigEndian(&binary[1], 8, 0xfedcba9876543210ul)) return 22;
            if (!std::Binary::BinaryPrimitives::TryReadUInt64BigEndian(&binary[1], 8, ref integer) || integer != 0xfedcba9876543210ul) return 23;
            std::Hashing::CRC32 crc = new(); crc.Append("123456789", 9);
            if (crc.GetValue() != 0xcbf43926u) return 24;
            std::Hashing::Adler32 adler = new(); adler.Append("Wikipedia", 9);
            if (adler.GetValue() != 0x11e60398u) return 25;
            std::Text::Utf8Decoder decoder = new();
            if (decoder.Read(input, 2, false, &scalar, &consumed) != std::Text::Utf8Decoder::Status::NeedMore || consumed != 2) return 26;
            if (decoder.Read(&input[2], 2, true, &scalar, &consumed) != std::Text::Utf8Decoder::Status::Scalar || scalar != 0x1f600u) return 27;
            if (decoder.Read(input, 0, true, &scalar, &consumed) != std::Text::Utf8Decoder::Status::End) return 28;
            byte[10] packet; packet[0]=(byte)165; packet[9]=(byte)90;
            std::Binary::BinaryBufferWriter writer = new(&packet[1],8);
            std::Binary::BinaryBufferWriter payload = new();
            if (!writer.TryWriteUInt16BigEndian((ushort)4) || !writer.TryWriteSlice(4,ref payload)) return 29;
            if (!payload.TryWriteInt32LittleEndian(-42) || payload.TryWriteByte((byte)0)) return 30;
            if (!writer.TryWriteUInt16BigEndian((ushort)48879) || writer.Position!=8 || writer.Remaining!=0) return 31;
            if (packet[0]!=(byte)165 || packet[9]!=(byte)90 || packet[1]!=(byte)0 || packet[2]!=(byte)4 || packet[3]!=(byte)214 || packet[4]!=(byte)255 || packet[7]!=(byte)190 || packet[8]!=(byte)239) return 32;
            std::Binary::BinaryBufferReader reader = new(&packet[1],8);
            std::Binary::BinaryBufferReader part = new(); ushort field=0; int signedValue=0; byte extra=(byte)99;
            if (!reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)4 || !reader.TryReadSlice((nuint)field,ref part)) return 33;
            if (!part.TryReadInt32LittleEndian(ref signedValue) || signedValue!=-42 || part.TryReadByte(ref extra) || extra!=(byte)0) return 34;
            if (!reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)48879 || reader.Position!=8 || reader.Remaining!=0) return 35;
            if (reader.TryReadUInt16BigEndian(ref field) || field!=(ushort)0 || reader.Position!=8) return 36;
            if (packet[0]!=(byte)165 || packet[9]!=(byte)90) return 37;
            byte[4] bulk=[(byte)11,(byte)22,(byte)33,(byte)44];
            std::Binary::BinaryBufferWriter bulkWriter=new(bulk,4);
            if(!bulkWriter.TrySkip(1)||!bulkWriter.TryWriteBytes(bulk,3)||bulk[2]!=(byte)22||bulk[3]!=(byte)33)return 160;
            std::Binary::BinaryBufferReader bulkReader=new(bulk,4);
            if(!bulkReader.TryReadBytes(&bulk[1],3)||bulk[2]!=(byte)11||bulk[3]!=(byte)22)return 161;
            ushort[7] unicode = [(ushort)65, (ushort)0, (ushort)0x20ac, (ushort)0xd83d, (ushort)0xde00, (ushort)0xfeff, (ushort)0xffff];
            nuint unicodeBytes = 0; nuint unicodeUnits = 0; nuint unicodeScalars = 0;
            if (!std::Text::Utf16::TryCount(unicode, 7, &unicodeScalars) || unicodeScalars != 6) return 38;
            if (!std::Text::UnicodeTranscoding::TryGetUtf8Length(unicode, 7, ref unicodeBytes) || unicodeBytes != 15) return 39;
            if (!std::Text::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, input, 64, ref length) || length != 15) return 40;
            if (input[0] != 'A' || input[1] != '\0' || (byte)input[2] != (byte)0xe2 || (byte)input[5] != (byte)0xf0) return 41;
            if (!std::Text::UnicodeTranscoding::TryGetUtf16Length(input, length, ref unicodeUnits) || unicodeUnits != 7) return 43;
            ushort[9] converted;
            for (nuint i = 0; i < 9; i++) converted[i] = (ushort)123;
            if (!std::Text::UnicodeTranscoding::TryUtf8ToUtf16(input, length, &converted[1], 7, ref unicodeUnits) || unicodeUnits != 7) return 44;
            if (converted[0] != (ushort)123 || converted[8] != (ushort)123) return 45;
            for (nuint i = 0; i < 7; i++) if (unicode[i] != converted[i + 1]) return 46;
            for (nuint i = 0; i < 64; i++) decoded[i] = 'Z';
            if (std::Text::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, decoded, 14, ref decodedLength) || decodedLength != 0) return 47;
            for (nuint i = 0; i < 64; i++) if (decoded[i] != 'Z') return 48;
            unicode[6] = (ushort)0xd800;
            if (std::Text::UnicodeTranscoding::TryUtf16ToUtf8(unicode, 7, decoded, 64, ref decodedLength) || decodedLength != 0) return 49;
            for (nuint i = 0; i < 64; i++) if (decoded[i] != 'Z') return 50;
            for (nuint i = 0; i < 9; i++) converted[i] = (ushort)123;
            if (std::Text::UnicodeTranscoding::TryUtf8ToUtf16(input, length, &converted[1], 6, ref unicodeUnits) || unicodeUnits != 0) return 51;
            for (nuint i = 0; i < 9; i++) if (converted[i] != (ushort)123) return 52;
            input[length] = (char)255;
            if (std::Text::UnicodeTranscoding::TryUtf8ToUtf16(input, length + 1, &converted[1], 7, ref unicodeUnits) || unicodeUnits != 0) return 53;
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
            if(std::Text::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=0) return 63;
            if(std::Text::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=58) return 64;
            searchPattern[2]=(byte)2;
            if(std::Text::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=64) return 65;
            if(std::Text::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,5)!=64) return 66;
            if(std::Text::ByteSearch::IndexOf((byte*)searchInput,64,(byte*)searchPattern,0)!=0) return 67;
            if(std::Text::ByteSearch::LastIndexOf((byte*)searchInput,64,(byte*)searchPattern,0)!=64) return 68;
            char* cText=(char*)searchInput;
            if(std::Text::CString::Length(cText)!=1) return 69;
            if(std::Text::CString::Compare(cText,&cText[2])!=0 || !std::Text::CString::Equals(cText,&cText[2])) return 70;
            if(!std::Text::CString::StartsWith(cText,&cText[2]) || !std::Text::CString::EndsWith(cText,&cText[2])) return 71;
            length=99;
            if(std::Text::CString::TryGetLength(cText,1,ref length) || length!=0) return 72;
            if(!std::Text::CString::TryGetLength(cText,2,ref length) || length!=1) return 73;
            searchInput[1]=(byte)2;searchInput[2]=(byte)0;
            if(std::Text::CString::Length(cText)!=2 || std::Text::CString::Compare(cText,&cText[2])!=1) return 74;
            if(std::Text::CString::Equals(cText,&cText[2]) || std::Text::CString::EndsWith(cText,"x")) return 75;
            if(!std::Text::CString::EndsWith(cText,&cText[1]) || !std::Text::CString::StartsWith(cText,&cText[2])) return 76;
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
            if(!std::Json::JsonString::TryGetQuotedLength((char*)searchInput,64,ref jsonNeeded)||jsonNeeded!=386) return 87;
            if(!std::Json::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],jsonNeeded,ref jsonWritten)||jsonWritten!=386) return 88;
            if(jsonOutput[0]!='Z'||jsonOutput[387]!='Z'||jsonOutput[1]!='"'||jsonOutput[386]!='"') return 89;
            for(nuint i=0;i<64;i++) {
                nuint at=2+i*6;
                if(jsonOutput[at]!='\\'||jsonOutput[at+1]!='u'||jsonOutput[at+2]!='0'||jsonOutput[at+3]!='0'||jsonOutput[at+4]!='0') return 90;
                char digit='0';if(i%2==0)digit='1';
                if(jsonOutput[at+5]!=digit) return 91;
            }
            searchInput[0]=(byte)'"';searchInput[1]=(byte)'\\';searchInput[2]=(byte)'\n';
            if(!std::Json::JsonString::TryGetQuotedLength((char*)searchInput,64,ref jsonNeeded)||jsonNeeded!=374) return 92;
            if(!std::Json::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],jsonNeeded,ref jsonWritten)||jsonWritten!=374) return 93;
            readonly(char)* jsonPrefix="\\\"\\\\\\n\\u0000";
            for(nuint i=0;i<12;i++)if(jsonOutput[i+2]!=jsonPrefix[i]) return 94;
            for(nuint i=0;i<388;i++)jsonOutput[i]='Z';
            if(std::Json::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],jsonNeeded-1,ref jsonWritten)||jsonWritten!=0) return 95;
            for(nuint i=0;i<388;i++)if(jsonOutput[i]!='Z') return 96;
            searchInput[63]=(byte)255;
            if(std::Json::JsonString::TryGetQuotedLength((char*)searchInput,64,ref jsonNeeded)||jsonNeeded!=0) return 97;
            if(std::Json::JsonString::TryQuote((char*)searchInput,64,&jsonOutput[1],386,ref jsonWritten)||jsonWritten!=0) return 98;
            for(nuint i=0;i<388;i++)if(jsonOutput[i]!='Z') return 99;
            byte[12] variable;
            for(nuint i=0;i<12;i++)variable[i]=(byte)90;
            nuint variableLength=0; nuint variableConsumed=0;
            if(std::Binary::VarInt::GetUInt64Length(unsignedMaximum)!=10) return 100;
            if(std::Binary::VarInt::TryWriteUInt64(&variable[1],9,unsignedMaximum,ref variableLength)||variableLength!=0) return 101;
            for(nuint i=0;i<12;i++)if(variable[i]!=(byte)90) return 102;
            if(!std::Binary::VarInt::TryWriteUInt64(&variable[1],10,unsignedMaximum,ref variableLength)||variableLength!=10) return 103;
            if(variable[0]!=(byte)90||variable[11]!=(byte)90||variable[10]!=(byte)1) return 104;
            for(nuint i=1;i<10;i++)if(variable[i]!=(byte)255) return 105;
            ulong variableValue=42ul;
            if(std::Binary::VarInt::ReadUInt64(&variable[1],9,ref variableValue,ref variableConsumed)!=std::Binary::VarInt::Status::NeedMore||variableValue!=42ul||variableConsumed!=0) return 106;
            if(std::Binary::VarInt::ReadUInt64(&variable[1],11,ref variableValue,ref variableConsumed)!=std::Binary::VarInt::Status::Done||variableValue!=unsignedMaximum||variableConsumed!=10) return 107;
            variable[10]=(byte)2;
            if(std::Binary::VarInt::ReadUInt64(&variable[1],10,ref variableValue,ref variableConsumed)!=std::Binary::VarInt::Status::Invalid||variableValue!=unsignedMaximum||variableConsumed!=0) return 108;
            uint narrowVariable=(uint)(runtimeInput>>32)|300u; uint narrowDecoded=0u;
            if(!std::Binary::VarInt::TryWriteUInt32(&variable[1],10,narrowVariable,ref variableLength)||variableLength!=std::Binary::VarInt::GetUInt32Length(narrowVariable)) return 109;
            if(std::Binary::VarInt::ReadUInt32(&variable[1],variableLength,ref narrowDecoded,ref variableConsumed)!=std::Binary::VarInt::Status::Done||narrowDecoded!=narrowVariable||variableConsumed!=variableLength) return 110;
            uint floatBits=(uint)(runtimeInput>>32)|0x7f800001u;
            ulong doubleBits=runtimeInput|0x7ff0000000000001ul;
            float floatingValue=*((float*)&floatBits); double doubleValue=*((double*)&doubleBits);
            byte[14] ieee; ieee[0]=(byte)90; ieee[13]=(byte)165;
            std::Binary::BinaryBufferWriter ieeeWriter=new(&ieee[1],12);
            if(!ieeeWriter.TryWriteFloatLittleEndian(floatingValue)||!ieeeWriter.TryWriteDoubleBigEndian(doubleValue)||ieeeWriter.Position!=12) return 111;
            if(ieee[0]!=(byte)90||ieee[13]!=(byte)165||ieee[1]!=(byte)1||ieee[4]!=(byte)255||ieee[5]!=(byte)255||ieee[6]!=(byte)240||ieee[12]!=(byte)1) return 112;
            std::Binary::BinaryBufferReader ieeeReader=new(&ieee[1],12);
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
            if(std::Text::CString::TryCopy(cText,&copied[1],2)) return 137;
            for(nuint i=0;i<7;i++) if(copied[i]!='Z') return 138;
            if(!std::Text::CString::TryCopy(cText,&copied[1],3)||!std::Text::CString::Equals(cText,&copied[1])) return 139;
            if(!std::Text::CString::TryCopy(&copied[1],&copied[2],3)||!std::Text::CString::Equals(cText,&copied[2])) return 140;
            if(!std::Text::CString::TryCopy(&copied[2],&copied[1],3)||!std::Text::CString::Equals(cText,&copied[1])) return 141;
            if(copied[0]!='Z'||copied[5]!='Z'||copied[6]!='Z') return 142;
            if(std::Text::CString::TryCopy((readonly(char)*)1,(char*)1,0)) return 143;
            if(std::Binary::VarInt::ZigZagEncode((long)runtimeInput)!=unsignedMaximum ||
                std::Binary::VarInt::ZigZagDecode(unsignedMaximum)!=(long)runtimeInput) return 144;
            if(std::Binary::VarInt::ZigZagEncode((int)(runtimeInput>>32))!=(uint)unsignedMaximum ||
                std::Binary::VarInt::ZigZagDecode((uint)unsignedMaximum)!=(int)(runtimeInput>>32)) return 145;
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
            char[42] wideDecimal;
            for(nuint i=0;i<42;i++)wideDecimal[i]='Z';
            if(std::IntegerFormatting::TryFormat(wideBits,&wideDecimal[1],39,ref length)||length!=0) return 164;
            for(nuint i=0;i<42;i++)if(wideDecimal[i]!='Z') return 165;
            if(!std::IntegerFormatting::TryFormat(wideBits,&wideDecimal[1],40,ref length)||length!=40) return 166;
            if(wideDecimal[0]!='Z'||wideDecimal[41]!='Z') return 167;
            wideDecimal[41]='\0';
            if(!std::Text::CString::Equals(&wideDecimal[1],"-170141183460469231731687303715884105728")) return 168;
            byte[18] wideBinary; wideBinary[0]=(byte)90; wideBinary[17]=(byte)90;
            if(!std::Binary::BinaryPrimitives::TryWriteExtraLongLittleEndian(&wideBinary[1],16,wideBits)) return 169;
            extralong roundtrip=(extralong)1;
            if(!std::Binary::BinaryPrimitives::TryReadExtraLongLittleEndian(&wideBinary[1],16,ref roundtrip)||roundtrip!=wideBits) return 170;
            if(!std::Binary::BinaryPrimitives::TryWriteExtraLongBigEndian(&wideBinary[1],16,wideBits)) return 171;
            if(!std::Binary::BinaryPrimitives::TryReadExtraLongBigEndian(&wideBinary[1],16,ref roundtrip)||roundtrip!=wideBits) return 172;
            if(wideBinary[0]!=(byte)90||wideBinary[17]!=(byte)90) return 173;
            if(std::Hashing::Integer(runtimeInput)==std::Hashing::Integer(0ul)) return 174;
            ulong repeatedHashInput=(runtimeInput>>63)|((runtimeInput>>63)<<32);
            if(std::Hashing::Integer(repeatedHashInput)==std::Hashing::Integer(0ul)) return 175;
            std::Binary::BinaryBufferWriter wideCursorWriter=new(&wideBinary[1],16);
            std::Binary::BinaryBufferReader wideCursorReader=new(&wideBinary[1],16);
            if(!wideCursorWriter.TryWriteExtraLongBigEndian(wideBits)
                ||!wideCursorReader.TryReadExtraLongBigEndian(ref roundtrip)||roundtrip!=wideBits) return 176;
            wideCursorWriter.Reset();wideCursorReader.Reset();
            if(!wideCursorWriter.TryWriteExtraLongLittleEndian(wideBits)
                ||!wideCursorReader.TryReadExtraLongLittleEndian(ref roundtrip)||roundtrip!=wideBits) return 177;
            if(wideBinary[0]!=(byte)90||wideBinary[17]!=(byte)90) return 178;
            char[8] escaped=['"','\\','u','0','0','4',(char)(48ul+(runtimeInput>>63)),'"'];
            if(!std::Json::JsonString::ValueTextEquals(escaped,8,"A",1)
                ||std::Json::JsonString::ValueTextEquals(escaped,8,"B",1)) return 179;
            if (Sha256Proof() != 0) return 180;
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

        var sources = new[] { "Text/ascii.gf", "Text/utf8.gf", "Text/utf16.gf", "Text/UnicodeTranscoding.gf", "Encoding/Hex.gf", "Encoding/Base64.gf", "Encoding/Base64Url.gf", "Numerics/BitOperations.gf", "Hashing/primitive_hashing.gf", "Hashing/SHA256.gf",
            "Encoding/PercentEncoding.gf", "Binary/BinaryPrimitives.gf", "Hashing/CRC32.gf", "Hashing/Adler32.gf", "Text/Utf8Decoder.gf",
            "Binary/BinaryBufferReader.gf", "Binary/BinaryBufferWriter.gf", "IntegerFormatting.gf", "Text/hex_integer_parsing.gf", "Text/boolean_parsing.gf", "Text/ByteSearch.gf", "Text/CString.gf", "Memory/Bytes.gf", "Json/JsonString.gf", "Binary/VarInt.gf" }
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

