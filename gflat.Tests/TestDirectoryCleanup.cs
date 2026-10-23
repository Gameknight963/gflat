namespace gflat.Tests;

internal static class TestDirectoryCleanup
{
    public static async Task DeleteAsync(string directory)
    {
        // Windows can briefly retain an exited executable or an external reader.
        // Preserve the existing bounded retry policy and surface persistent failures.
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Delete(directory, true); return; }
            catch (Exception error) when (OperatingSystem.IsWindows() && attempt < 8 &&
                error is IOException or UnauthorizedAccessException)
            { await Task.Delay(50 * (attempt + 1)); }
        }
    }
}
