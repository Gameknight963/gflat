using System.Text;

namespace gflat.Tests;

public sealed class StandardCStringTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsignedByteOrderAndTerminatorSemanticsAreExhaustive(int optimization)
        => Run("""
            using std;
            int main() {
                char[4] left;char[4] right;
                left[1]='\0';right[1]='\0';left[2]='X';right[2]='Y';
                for(int a=0;a<=255;a++)for(int b=0;b<=255;b++) {
                    left[0]=(char)a;right[0]=(char)b;
                    int expected=0;if(a<b)expected=-1;else if(a>b)expected=1;
                    if(CString::Compare(left,right)!=expected)return 1;
                    if(CString::Equals(left,right)!=(a==b))return 2;
                    bool matches=a==b || b==0;
                    if(CString::StartsWith(left,right)!=matches)return 3;
                    if(CString::EndsWith(left,right)!=matches)return 4;
                    nuint length=1;if(a==0)length=0;
                    if(CString::Length(left)!=length)return 5;
                }
                if(!CString::Equals("same\0different","same\0suffix"))return 6;
                if(CString::Length("one\0trailing")!=3)return 7;
                if(!CString::StartsWith("hello","hell")||!CString::EndsWith("hello","ello"))return 8;
                if(CString::StartsWith("he","hello")||CString::EndsWith("lo","hello"))return 9;
                if(!CString::StartsWith("","")||!CString::EndsWith("",""))return 10;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundedLengthRespectsCapacityAndAllowsAlignedAliasedOutput(int optimization)
        => Run("""
            using std;
            int main() {
                char[35] storage;
                for(nuint count=0;count<=32;count++) {
                    for(nuint capacity=0;capacity<=33;capacity++) {
                        for(nuint i=0;i<35;i++)storage[i]='x';
                        storage[count+1]='\0';
                        nuint length=99;
                        bool found=CString::TryGetLength(&storage[1],capacity,ref length);
                        if(found!=(capacity>count))return 1;
                        if(found && length!=count)return 2;
                        if(!found && length!=0)return 3;
                        for(nuint i=0;i<35;i++) {
                            if(i==count+1) {if(storage[i]!='\0')return 4;}
                            else if(storage[i]!='x')return 5;
                        }
                    }
                }
                nuint length=99;
                if(CString::TryGetLength((readonly(char)*)1,0,ref length)||length!=0)return 6;
                nuint resultStorage=0;char* bytes=(char*)&resultStorage;
                bytes[0]='a';bytes[1]='b';bytes[2]='c';bytes[3]='\0';
                if(!CString::TryGetLength(bytes,4,ref resultStorage)||resultStorage!=3)return 7;
                for(nuint i=0;i<(nuint)sizeof(nuint);i++)bytes[i]='z';
                if(CString::TryGetLength(bytes,(nuint)sizeof(nuint),ref resultStorage)||resultStorage!=0)return 8;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedDotNetOracleMatchesRandomNullTerminatedRegions(int optimization)
    {
        var random=new Random(4717);
        var code=new StringBuilder("using std;int main(){char[35] left;char[35] right;\n");
        for(int trial=0;trial<100;trial++)
        {
            byte[] left=new byte[random.Next(33)],right=new byte[random.Next(33)];random.NextBytes(left);random.NextBytes(right);
            if(trial%3==0)right=left[..random.Next(left.Length+1)];
            if(trial%3==1)right=left[random.Next(left.Length+1)..];
            foreach(var (data,name) in new[]{(left,"left"),(right,"right")})
            {
                for(int i=0;i<data.Length;i++)code.AppendLine($"{name}[{i}]=(char){data[i]};");
                code.AppendLine($"{name}[{data.Length}]='\\0';{name}[{data.Length+1}]='Z';");
            }
            int leftZero=Array.IndexOf(left,(byte)0),rightZero=Array.IndexOf(right,(byte)0);
            if(leftZero>=0)left=left[..leftZero];if(rightZero>=0)right=right[..rightZero];
            int compare=Math.Sign(left.AsSpan().SequenceCompareTo(right));
            code.AppendLine($"if(CString::Length(left)!={left.Length}||CString::Length(right)!={right.Length})return 1;");
            code.AppendLine($"if(CString::Compare(left,right)!={compare})return 2;");
            code.AppendLine($"if(CString::Equals(left,right)!={Bool(left.AsSpan().SequenceEqual(right))})return 3;");
            code.AppendLine($"if(CString::StartsWith(left,right)!={Bool(left.AsSpan().StartsWith(right))})return 4;");
            code.AppendLine($"if(CString::EndsWith(left,right)!={Bool(left.AsSpan().EndsWith(right))})return 5;");
        }
        code.AppendLine("return 0;}");
        Run(code.ToString(),optimization);
    }

    private static string Bool(bool value)=>value?"true":"false";

    private static void Run(string code,int optimization)
    {
        SourceFile[] sources=[new("CString.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/CString.gf"))),new("main.gf",code)];
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        foreach(string forbidden in new[]{"invoke ","personality","@__gflat_eh_","gflat$Memory$","@malloc","@free"})Assert.DoesNotContain(forbidden,ir);
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}

