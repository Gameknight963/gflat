namespace gflat.Tests;

public sealed class TestDirectoryCleanupTests
{
    [Fact]
    public async Task WindowsSharingLockReleasedDuringCleanupAllowsDeletion()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "locked.bin");
            File.WriteAllText(path, "owned fixture");
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Task cleanup = TestDirectoryCleanup.DeleteAsync(directory);
            Assert.False(cleanup.IsCompleted);
            await Task.Delay(100);
            held.Dispose();
            await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    [Fact]
    public async Task WindowsPersistentSharingLockStillReportsFailureAndPreservesFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "locked.bin");
            File.WriteAllText(path, "owned fixture");
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Exception? failure = await Record.ExceptionAsync(() => TestDirectoryCleanup.DeleteAsync(directory));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.True(File.Exists(path));
            Assert.Equal("owned fixture", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
