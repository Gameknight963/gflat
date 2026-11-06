using System.Reflection;

namespace gflat.LanguageServer.Tests;

public sealed class WatcherReuseTests
{
    [Fact]
    public void RecheckingTheSameProjectKeepsItsWatchersAndChangedInputsRefreshThem()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var server = new LanguageServer(Stream.Null, Stream.Null, TextWriter.Null);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var paths = (HashSet<string>)typeof(LanguageServer).GetField("watchedPaths", flags)!.GetValue(server)!;
        var watchers = (List<FileSystemWatcher>)typeof(LanguageServer).GetField("watchers", flags)!.GetValue(server)!;
        var update = typeof(LanguageServer).GetMethod("UpdateWatchers", flags)!;
        try
        {
            paths.Add(Path.Combine(directory, "main.gf"));
            update.Invoke(server, null);
            var original = Assert.Single(watchers);
            update.Invoke(server, null);
            Assert.Same(original, Assert.Single(watchers));
            paths.Add(Path.Combine(directory, "library.gf"));
            update.Invoke(server, null);
            Assert.NotSame(original, Assert.Single(watchers));
        }
        finally
        {
            foreach (var watcher in watchers) watcher.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
