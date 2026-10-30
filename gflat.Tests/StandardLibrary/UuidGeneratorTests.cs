using System.Text;

namespace gflat.Tests.Std;

public sealed class UuidGeneratorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeterministicPayloadsPreserveEveryNonVersionBitWithoutAllocating(int optimization)
    {
        List<byte[]> payloads = [new byte[16], Enumerable.Repeat((byte)255, 16).ToArray(),
            Convert.FromHexString("919108F752D143209BACF847DB4148A8")];
        for (int bit = 0; bit < 128; bit++)
        {
            byte[] payload = new byte[16]; payload[bit / 8] = (byte)(1 << (bit % 8)); payloads.Add(payload);
        }
        for (int row = 0; row < 256; row++)
            payloads.Add(Enumerable.Range(0, 16).Select(index => (byte)((row + index * 37) ^ (index * 11))).ToArray());
        var records = new StringBuilder();
        foreach (byte[] payload in payloads)
        {
            byte[] expected = payload.ToArray();
            expected[6] = (byte)((expected[6] & 0x0f) | 0x40);
            expected[8] = (byte)((expected[8] & 0x3f) | 0x80);
            records.Append(Convert.ToHexString(payload));
            records.Append(Convert.ToHexString(expected));
            records.Append(new Guid(expected, bigEndian: true).ToString("D"));
        }
        Run($$"""
            using Standard; using Standard::Text;
            extern void*? malloc(nuint count); extern void free(void* storage);
            bool deny=false; int allocations=0; int calls=0; nuint requested=0;
            readonly(byte)* payload=((nuint)1).reinterpret<readonly(byte)*>();
            namespace Memory {
                public replace void*? Allocate(nuint count) { if(deny){allocations++;return null;}return malloc(count); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int Digit(char value) nothrow { if(value<='9')return value-'0';return value-'A'+10; }
            byte ReadByte(readonly(char)* data,ref nuint at) nothrow {
                byte value=(byte)((Digit(data[at])<<4)|Digit(data[at+1]));at+=2;return value;
            }
            bool Fill(byte* output,nuint count) {
                calls++;requested=count;if(count!=16)return false;
                for(nuint i=0;i<16;i++)output[i]=payload[i];return true;
            }
            int main(){
                readonly(char)* records="{{records}}";nuint at=0;deny=true;
                for(int row=0;row<{{payloads.Count}};row++){
                    byte[16] source;byte[16] saved;byte[16] expected;
                    for(nuint i=0;i<16;i++){source[i]=ReadByte(records,ref at);saved[i]=source[i];}
                    for(nuint i=0;i<16;i++)expected[i]=ReadByte(records,ref at);
                    payload=&source[0];calls=0;requested=0;Uuid result=new(1ul,2ul);
                    if(!UuidGenerator::TryCreateVersion4(Fill,ref result)||calls!=1||requested!=16)return 1;
                    byte[18] output;for(nuint i=0;i<18;i++)output[i]=(byte)165;nuint written=99;
                    if(!result.TryWriteBytes(&output[1],16,ref written)||written!=16)return 2;
                    if(output[0]!=(byte)165||output[17]!=(byte)165)return 3;
                    for(nuint i=0;i<16;i++)if(output[i+1]!=expected[i]||source[i]!=saved[i])return 4;
                    char[38] formatted;for(nuint i=0;i<38;i++)formatted[i]='?';written=99;
                    if(!result.TryFormat(&formatted[1],36,ref written)||written!=36||formatted[0]!='?'||formatted[37]!='?')return 5;
                    for(nuint i=0;i<36;i++)if(formatted[i+1]!=records[at+i])return 6;
                    at+=36;calls=0;requested=0;Uuid created=UuidGenerator::CreateVersion4(Fill);
                    if(calls!=1||requested!=16||!created.Equals(&result))return 7;
                    for(nuint i=0;i<16;i++)if(source[i]!=saved[i])return 8;
                }
                deny=false;if(at!={{records.Length}}||allocations!=0)return 9;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FalseProvidersDiscardEveryPartialPayloadAndThrowTheDocumentedFailure(int optimization)
        => Run("""
            using Standard;using Standard::Text;
            int calls=0;nuint requested=0;nuint writeCount=0;
            bool Fail(byte* output,nuint count){
                calls++;requested=count;if(count!=16)return false;
                for(nuint i=0;i<writeCount;i++)output[i]=(byte)(i+1);return false;
            }
            int main(){
                for(int mode=0;mode<3;mode++){
                    writeCount=0;if(mode==1)writeCount=7;else if(mode==2)writeCount=16;
                    calls=0;requested=0;Uuid result=new(1ul,2ul);
                    if(UuidGenerator::TryCreateVersion4(Fail,ref result)||!result.IsNil||calls!=1||requested!=16)return 1;
                    calls=0;requested=0;bool caught=false;
                    try{Uuid unexpected=UuidGenerator::CreateVersion4(Fail);}
                    catch(ref Exception error){caught=error.GetCode()==1&&CString::Equals(error.GetMessage(),"UUID random source failed");}
                    if(!caught||calls!=1||requested!=16)return 2;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ProviderExceptionsPropagateWithoutChangingAnExistingResultOrRetrying(int optimization)
        => Run("""
            using Standard;using Standard::Text;
            int calls=0;nuint requested=0;
            bool Throw(byte* output,nuint count){
                calls++;requested=count;
                if(count==16){output[0]=(byte)91;output[1]=(byte)92;output[2]=(byte)93;}
                throw new Exception("original UUID provider",73);
            }
            int main(){
                Uuid result=new(123ul,456ul);Uuid saved=result;bool caught=false;
                try{UuidGenerator::TryCreateVersion4(Throw,ref result);}
                catch(ref Exception error){caught=error.GetCode()==73&&CString::Equals(error.GetMessage(),"original UUID provider");}
                if(!caught||calls!=1||requested!=16||!result.Equals(&saved))return 1;
                calls=0;requested=0;caught=false;
                try{Uuid unexpected=UuidGenerator::CreateVersion4(Throw);}
                catch(ref Exception error){caught=error.GetCode()==73&&CString::Equals(error.GetMessage(),"original UUID provider");}
                if(!caught||calls!=1||requested!=16||!result.Equals(&saved))return 2;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("uuid-generator.gf", program));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
