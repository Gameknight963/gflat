namespace gflat.Tests;

public sealed class ProgramArgumentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunForwardsProgramArgumentsWithoutReinterpretingOrRequotingThem(bool project)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_arguments_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "main.gf");
            File.WriteAllText(source, """
                extern int strcmp(readonly(char*) left, readonly(char*) right);
                int main(int argc, char** argv) {
                    if (argc != 6) return 1;
                    if (strcmp(argv[1], "two words") != 0) return 2;
                    if (strcmp(argv[2], "quote\"inside") != 0) return 3;
                    if (strcmp(argv[3], "") != 0) return 4;
                    if (strcmp(argv[4], "-o") != 0) return 5;
                    if (strcmp(argv[5], "--check") != 0) return 6;
                    return 17;
                }
                """);
            string input = source;
            if (project)
            {
                input = Path.Combine(directory, "app.gfproj");
                File.WriteAllText(input, TestSupport.ProjectFileFixture.Xml());
            }
            Assert.Equal(17, Program.Main([input, "--run", "--", "two words", "quote\"inside", "", "-o", "--check"]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ProgramArgumentsRequireRunBeforeLoadingSources()
        => Assert.Equal(2, Program.Main(["missing.gf", "--", "argument"]));
}
