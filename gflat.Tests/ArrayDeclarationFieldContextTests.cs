namespace gflat.Tests;

public sealed class ArrayDeclarationFieldContextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeclaredFieldTargetsAndFloatingArrayStoresUseOrdinaryScalarConversions(int optimization)
    {
        const string source="""
            nuint[3] Global=[0,(nuint)7,1];
            const nuint[3] GlobalConstant=[0,(nuint)7,1];
            namespace Values {
                public nuint[3] Data=[0,(nuint)7,1];
                public const nuint[3] Constant=[0,(nuint)7,1];
            }
            struct StructFields {
                public nuint[3] Instance=[0,(nuint)7,1];
                public static nuint[3] Shared=[0,(nuint)7,1];
                public static const nuint[3] Constant=[0,(nuint)7,1];
            }
            class ClassFields {
                public nuint[3] Instance=[0,(nuint)7,1];
                public static nuint[3] Shared=[0,(nuint)7,1];
                public static const nuint[3] Constant=[0,(nuint)7,1];
            }
            bool Correct(nuint[3] values)=>values[0]==0&&values[1]==7&&values[2]==1;
            int main(){
                StructFields structure=new();ClassFields instance=new();
                if(!Correct(Global)||!Correct(GlobalConstant))return 1;
                if(!Correct(Values::Data)||!Correct(Values::Constant))return 2;
                if(!Correct(structure.Instance)||!Correct(StructFields::Shared)||!Correct(StructFields::Constant))return 3;
                if(!Correct(instance.Instance)||!Correct(ClassFields::Shared)||!Correct(ClassFields::Constant))return 4;
                nuint[3] structValues=structure.Instance;nuint[3] classValues=instance.Instance;
                if(sizeof(Global)!=3*sizeof(nuint)||sizeof(structValues)!=3*sizeof(nuint)||sizeof(classValues)!=3*sizeof(nuint))return 5;
                structure.Instance[1]=9;
                if(instance.Instance[1]!=7||StructFields::Shared[1]!=7||Global[1]!=7)return 6;
                float single=0.1f;
                double[4] values=[0.0d,single,0.1f,1.0d];
                double ordinary=single;
                if(sizeof(values)!=4*sizeof(double)||values[0]!=0.0d||values[3]!=1.0d)return 7;
                if(values[1]!=(double)single||values[1]!=ordinary||values[2]!=ordinary)return 8;
                // Exact binary32 0.1 promoted to binary64, not a freshly rounded binary64 0.1.
                if(values[1]!=0.100000001490116119384765625d||values[1]==0.1d)return 9;
                if(single!=0.1f)return 10;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(source,optimization);
        Assert.True(result.ExitCode==0,$"Array declaration field context exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
