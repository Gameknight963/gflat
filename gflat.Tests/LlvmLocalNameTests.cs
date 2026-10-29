namespace gflat.Tests;

public sealed class LlvmLocalNameTests
{
    public static IEnumerable<object[]> CollisionPrograms()
    {
        string[] programs = [
            "int Read(int entry,int arg_entry)=>entry+arg_entry;int main()=>Read(40,2)-42;",
            "int Read(int t0,int t1)=>t0+t1;void Increment(ref int t0){t0+=1;}int main(){int value=40;Increment(ref value);return Read(value,1)-42;}",
            "int Read(int then_0,int else_1,int merge_2){if(then_0>0)return else_1;else return merge_2;}int main()=>Read(1,42,7)-42;"
        ];
        foreach (int optimization in new[] { 0, 2 })
        for (int shape = 0; shape < programs.Length; shape++)
            yield return [shape, programs[shape], optimization];
    }

    [Theory]
    [MemberData(nameof(CollisionPrograms))]
    public void SourceParametersDoNotCollideWithEntryTempsOrGeneratedLabels(int shape, string program, int optimization)
    {
        _ = shape;
        Run(program, optimization, minimal: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstructorsCopiesMethodsAndOperatorsKeepTheirSourceBindings(int optimization)
        => Run("""
            int copies=0;
            struct Value {
                public int Number;
                public Value(int entry){Number=entry;}
                public copy Value(immutable(Value)* t0){Number=t0.Number;copies++;}
                public int Read(int entry)=>Number+entry;

            }
            struct Operand {
                public int Number;
                public Operand(int entry){Number=entry;}
                public static int operator +(Operand entry,Operand t0)=>entry.Number+t0.Number;
            }
            class Owner {
                public int Number;
                public Owner(int t0){Number=t0;}
                public copy Owner(immutable(Owner)* entry){Number=entry.Number;copies++;}
                public int Read(int entry)=>Number+entry;
            }
            int main(){
                Value first=new Value(20);Value second=new Value(22);Value copy=first;
                Owner owner=new Owner(40);Owner ownerCopy=owner;
                Operand left=new Operand(20);Operand right=new Operand(22);
                if(copy.Number!=20||left+right!=42||copy.Read(22)!=42)return 1;
                if(ownerCopy.Read(2)!=42||copies<2)return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedLambdasAndReferenceParametersUseIndependentIncomingNames(int optimization)
        => Run("""
            int Read(int entry){
                int(int)* outer=(int t0)=>{
                    int(int)* inner=(int entry)=>entry+1;
                    return inner(t0);
                };
                return outer(entry);
            }
            int main(){
                int value=40;
                void(ref int)* increment=(ref int entry)=>{entry+=1;};
                increment(ref value);
                return Read(value)-42;
            }
            """, optimization);

    private static void Run(string program, int optimization, bool minimal = false)
    {
        string ir = Compiler.Emit(program, options: new() { NoStdLib = minimal, NoBuiltins = minimal, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
