using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ArithmeticValueQualificationTests
{
    [Theory]
    [InlineData("readonly",0)]
    [InlineData("readonly",2)]
    [InlineData("immutable",0)]
    [InlineData("immutable",2)]
    public void NumericLambdaResultsAreValuesWhileTheirPointeeParametersRemainProtected(string protection,int optimization)
    {
        var integerOperators=new (string Operator,int Expected)[]{("+",47),("-",37),("*",210),("/",8),("%",2),
            ("&",0),("|",47),("^",47),("<<",1344),(">>",1)};
        string integers=string.Join('\n',integerOperators.Select((item,index)=>$$"""
            {
                int({{protection}}(int)*,{{protection}}(int)*)* operation=({{protection}}(int)* left,{{protection}}(int)* right)=>*left {{item.Operator}} *right;
                if(operation(&left,&right)!={{item.Expected}}||left!=42||right!=5)return {{index+1}};
            }
            """));
        string Floats(string type,string suffix,int failure){
            (string Operator,string Expected)[] operators=[("+","9.5"),("-","5.5"),("*","15.0"),("/","3.75"),("%","1.5")];
            string operations=string.Join('\n',operators.Select((item,index)=>$$"""
                {
                    {{type}}({{protection}}({{type}})*,{{protection}}({{type}})*)* operation=({{protection}}({{type}})* first,{{protection}}({{type}})* second)=>*first {{item.Operator}} *second;
                    if(operation(&a,&b)!={{item.Expected}}{{suffix}}||a!=7.5{{suffix}}||b!=2.0{{suffix}})return {{failure+index}};
                }
                """));
            return $$"""
                {
                    {{type}} a=7.5{{suffix}};{{type}} b=2.0{{suffix}};
                    {{operations}}
                    {{type}}({{protection}}({{type}})*)* negate=({{protection}}({{type}})* value)=>-*value;
                    if(negate(&a)!=-7.5{{suffix}}||a!=7.5{{suffix}})return {{failure+5}};
                }
                """;
        }
        string source=$$"""
            int Named({{protection}}(int)* left,{{protection}}(int)* right)=>*left-*right;
            int Apply(int({{protection}}(int)*,{{protection}}(int)*)* callback,{{protection}}(int)* left,{{protection}}(int)* right)=>callback(left,right);
            alias Word=int;
            int main(){
                int left=42;int right=5;
                int({{protection}}(int)*,{{protection}}(int)*)* named=&Named;
                if(named(&left,&right)!=37||Apply(&Named,&left,&right)!=37)return 20;
                if(Apply(({{protection}}(int)* a,{{protection}}(int)* b)=>*a-*b,&left,&right)!=37)return 21;
                {{integers}}
                int({{protection}}(int)*)* negate=({{protection}}(int)* value)=>-*value;
                if(negate(&left)!=-42||left!=42)return 22;
                int({{protection}}(int)*,int)* leftOnly=({{protection}}(int)* value,int other)=>*value+other;
                int(int,{{protection}}(int)*)* rightOnly=(int value,{{protection}}(int)* other)=>value+*other;
                if(leftOnly(&left,right)!=47||rightOnly(left,&right)!=47)return 23;
                int({{protection}}(Word)*,{{protection}}(Word)*)* aliasProduct=({{protection}}(Word)* a,{{protection}}(Word)* b)=>(*a)*(*b);
                if(aliasProduct(&left,&right)!=210)return 24;
                {{Floats("float","f",30)}}
                {{Floats("double","d",40)}}
                // A genuinely protected pointer return still carries its capability.
                {{protection}}(int)*({{protection}}(int)*)* identity=({{protection}}(int)* value)=>value;
                if(identity(&left)!=&left||*identity(&left)!=42)return 50;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(source,optimization);
        Assert.True(result.ExitCode==0,$"Arithmetic value qualification exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    public static IEnumerable<object[]> ProtectedStorageCases()
    {
        foreach(string protection in new[]{"readonly","immutable"}){
            yield return [$$"""
                int main(){int value=1;{{protection}}(int)* pointer=&value;int computed=*pointer+1;*pointer=computed;return 0;}
                """];
            yield return [$$"""
                int main(){int value=1;{{protection}}(int)* pointer=&value;int computed=-*pointer;*pointer=computed;return 0;}
                """];
            yield return [$$"""
                int main(){int*({{protection}}(int)*)* callback=({{protection}}(int)* value)=>value;return 0;}
                """];
            yield return [$$"""
                {{protection}}(int)* Identity({{protection}}(int)* value)=>value;
                int main(){int*({{protection}}(int)*)* callback=&Identity;return 0;}
                """];
        }
        yield return ["""
            struct Number{public int Value;public static immutable(Number) operator +(Number left,Number right)=>default;}
            int main(){Number value=default;var result=value+value;result.Value=1;return 0;}
            """];
    }

    [Theory]
    [MemberData(nameof(ProtectedStorageCases))]
    public void ArithmeticNormalizationDoesNotRemovePointeePointerOrDeclaredOperatorCapabilities(string source)
        =>Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
}
