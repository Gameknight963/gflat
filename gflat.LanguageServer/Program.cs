namespace gflat.LanguageServer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help"))
        {
            Console.WriteLine("gflat.LanguageServer: LSP over standard input/output. Workspace configuration: gflat-workspace.json.");
            return 0;
        }
        try
        {
            return await new LanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error).RunAsync();
        }
        catch (Exception error)
        {
            await Console.Error.WriteLineAsync(error.ToString());
            return 1;
        }
    }
}
