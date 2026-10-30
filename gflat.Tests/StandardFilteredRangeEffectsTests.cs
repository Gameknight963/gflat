namespace gflat.Tests;

public sealed class StandardFilteredRangeEffectsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FusedTakeSkipPreservePredicateEffectsAndZeroTakeShortCircuiting(int optimization)
    {
        const string program = """
using Standard; using Standard::Queries;
int calls=0; int mode=0; int failAt=0;
bool Predicate(int value) {
    calls++;
    if(calls==failAt) throw new Exception("predicate",77);
    if(mode==0)return true;
    if(mode==1)return value%2==0;
    return false;
}
bool DirectAny(int id,int count) {
    if(id==0)return new IntRange(0,count).Where(&Predicate).Take(3).Skip(3).Any();
    if(id==1)return new IntRange(0,count).Where(&Predicate).Take(3).Skip(9).Any();
    if(id==2)return new IntRange(0,count).Where(&Predicate).Skip(2).Take(3).Skip(3).Any();
    if(id==3)return new IntRange(0,count).Where(&Predicate).Take(3).Skip(2).Any();
    if(id==4)return new IntRange(0,count).Where(&Predicate).Take(3).Skip(3).Take(1).Any();
    if(id==5)return new IntRange(0,count).Where(&Predicate).Take(3).Skip(3).Take(0).Any();
    if(id==6)return new IntRange(0,count).Where(&Predicate).Skip(3).Take(0).Any();
    if(id==7)return new IntRange(0,count).Where(&Predicate).Take(0).Skip(3).Any();
    if(id==8)return new IntRange(0,count).Where(&Predicate).Skip(3).Take(0).Skip(3).Take(1).Any();
    if(id==9)return new IntRange(0,count).Where(&Predicate).Take(3).Skip(2).Skip(9).Any();
    return false;
}
nuint DirectCount(int id,int count) {
    if(id==0)return (nuint)new IntRange(0,count).Where(&Predicate).Take(3).Skip(3).Count();
    if(id==1)return (nuint)new IntRange(0,count).Where(&Predicate).Take(3).Skip(9).Count();
    if(id==2)return (nuint)new IntRange(0,count).Where(&Predicate).Skip(2).Take(3).Skip(3).Count();
    if(id==3)return (nuint)new IntRange(0,count).Where(&Predicate).Take(3).Skip(2).Count();
    if(id==4)return (nuint)new IntRange(0,count).Where(&Predicate).Take(3).Skip(3).Take(1).Count();
    if(id==5)return (nuint)new IntRange(0,count).Where(&Predicate).Take(3).Skip(3).Take(0).Count();
    if(id==6)return (nuint)new IntRange(0,count).Where(&Predicate).Skip(3).Take(0).Count();
    if(id==7)return (nuint)new IntRange(0,count).Where(&Predicate).Take(0).Skip(3).Count();
    if(id==8)return (nuint)new IntRange(0,count).Where(&Predicate).Skip(3).Take(0).Skip(3).Take(1).Count();
    if(id==9)return (nuint)new IntRange(0,count).Where(&Predicate).Take(3).Skip(2).Skip(9).Count();
    return 0;
}
bool GeneralAny(int id,int count) {
    if(id==0)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(3).Any();
    if(id==1)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(9).Any();
    if(id==2)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Skip(2).Take(3).Skip(3).Any();
    if(id==3)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(2).Any();
    if(id==4)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(3).Take(1).Any();
    if(id==5)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(3).Take(0).Any();
    if(id==6)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Skip(3).Take(0).Any();
    if(id==7)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(0).Skip(3).Any();
    if(id==8)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Skip(3).Take(0).Skip(3).Take(1).Any();
    if(id==9)return new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(2).Skip(9).Any();
    return false;
}
nuint GeneralCount(int id,int count) {
    if(id==0)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(3).Count();
    if(id==1)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(9).Count();
    if(id==2)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Skip(2).Take(3).Skip(3).Count();
    if(id==3)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(2).Count();
    if(id==4)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(3).Take(1).Count();
    if(id==5)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(3).Take(0).Count();
    if(id==6)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Skip(3).Take(0).Count();
    if(id==7)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(0).Skip(3).Count();
    if(id==8)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Skip(3).Take(0).Skip(3).Take(1).Count();
    if(id==9)return (nuint)new IntRange(0,count).AsEnumerable().Where(&Predicate).Take(3).Skip(2).Skip(9).Count();
    return 0;
}
int main() {
    int[3] sizes=[0,1,7]; int[6] failures=[0,1,2,3,4,8];
    for(int id=0;id<10;id++)
    for(int size=0;size<3;size++)
    for(int predicate=0;predicate<3;predicate++)
    for(int failure=0;failure<6;failure++)
    for(int terminal=0;terminal<2;terminal++) {
        mode=predicate;failAt=failures[failure];calls=0;
        bool directThrows=false;int directResult=0;
        try {
            if(terminal==0) {if(DirectAny(id,sizes[size]))directResult=1;}
            else directResult=(int)DirectCount(id,sizes[size]);
        } catch {directThrows=true;}
        int directCalls=calls;calls=0;
        bool generalThrows=false;int generalResult=0;
        try {
            if(terminal==0) {if(GeneralAny(id,sizes[size]))generalResult=1;}
            else generalResult=(int)GeneralCount(id,sizes[size]);
        } catch {generalThrows=true;}
        if(directThrows!=generalThrows||directCalls!=calls||directResult!=generalResult)return id+1;
    }
    return 0;
}


""";
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("filtered-range-effects.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Filtered range exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}