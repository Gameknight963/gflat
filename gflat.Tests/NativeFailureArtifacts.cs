using System.Text.Json;

namespace gflat.Tests;

internal static class NativeFailureArtifacts
{
    internal static void Preserve(string directory, string testId, Exception error)
    {
        string? root = Environment.GetEnvironmentVariable("GFLAT_TEST_FAILURE_ARTIFACT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            string destination = Path.Combine(Path.GetFullPath(root), testId);
            Directory.CreateDirectory(destination);
            foreach (string name in new[] { "test.ll", "test.exe", "test.pdb", "test.ll.foreign.json" })
            {
                string source = Path.Combine(directory, name);
                if (File.Exists(source) && (File.GetAttributes(source) & FileAttributes.ReparsePoint) == 0)
                    File.Copy(source, Path.Combine(destination, name));
            }
            File.WriteAllText(Path.Combine(destination, "failure.json"), JsonSerializer.Serialize(new
            {
                TestName = PipelineTimings.TestName,
                OriginalDirectory = directory,
                Exception = error.ToString()
            }, new JsonSerializerOptions { WriteIndented = true }));
            error.Data["CompilerTestHelperArtifactDirectory"] = destination;
        }
        catch (Exception artifactError)
        {
            // Artifact storage must not replace the compiler or process failure.
            error.Data["CompilerTestHelperArtifactError"] = artifactError.Message;
        }
    }
}
