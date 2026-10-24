using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed class ConditionalAliasProjectTests
{
    [Fact]
    public void OpeningInactivePlatformAliasDoesNotChangeTheActiveProjectModel()
    {
        string root=Path.Combine(Path.GetTempPath(),"gflat_conditional_alias_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try{
            string project=Path.Combine(root,"app.gfproj");
            File.WriteAllText(project,ProjectFileFixture.Xml(content:"""
                <PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
                <ItemGroup>
                  <Compile Include="core.gf;main.gf" />
                  <Compile Include="AliasA.gf" Condition="'$(OS)' == 'Windows_NT'" />
                  <Compile Include="AliasB.gf" Condition="'$(OS)' != 'Windows_NT'" />
                </ItemGroup>
                """));
            File.WriteAllText(Path.Combine(root,"core.gf"),"static class A { public static int Read()=>42; } static class B { public static int Read()=>7; }");
            string a=Path.Combine(root,"AliasA.gf"),b=Path.Combine(root,"AliasB.gf");
            const string aliasA="alias SelectedClock=A;",aliasB="alias SelectedClock=B;";
            File.WriteAllText(a,aliasA);File.WriteAllText(b,aliasB);
            string main=Path.Combine(root,"main.gf");const string text="int main()=>SelectedClock::Read();";
            File.WriteAllText(main,text);
            var documents=new Dictionary<string,OpenDocument>(Workspace.Paths){[main]=new(main,text,1)};
            var projects=new ProjectWorkspace();
            string active=OperatingSystem.IsWindows()?a:b,inactive=OperatingSystem.IsWindows()?b:a;
            string activeText=OperatingSystem.IsWindows()?aliasA:aliasB;
            var expected=new Location(Workspace.FileUri(active),Workspace.ToRange(new SourceFile(active,activeText).Span(6,13)));
            int use=text.IndexOf("SelectedClock",StringComparison.Ordinal)+1;

            var before=Workspace.Analyze(root,documents,projects);
            Assert.Empty(before.Diagnostics[main]);
            Assert.Equal(expected,Assert.IsType<Location>(before.Models![main].Definition(main,use)));

            documents[inactive]=new(inactive,File.ReadAllText(inactive),1);
            var after=Workspace.Analyze(root,documents,projects);
            Assert.Empty(after.Diagnostics[main]);
            Assert.Equal(expected,Assert.IsType<Location>(after.Models![main].Definition(main,use)));
            // The excluded document's standalone diagnostics/model are intentionally
            // outside this project-membership regression's contract.
        }finally{Directory.Delete(root,true);}
    }
}
