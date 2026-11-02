using System.Text.Json;
using System.Xml.Linq;

static class FallbackPlan
{
    // Parameter names describe test intent. Observed default compilations take
    // precedence if a parameterized test also relies on the process default.
    public static void Augment(string directory, int shard)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var owner = manifest.RootElement.EnumerateArray().Single(item => item.GetProperty("shard").GetInt32() == shard);
        var owned = new HashSet<string>();
        var fallback = new HashSet<string>();
        foreach (var group in owner.GetProperty("classes").EnumerateArray())
            foreach (var method in group.GetProperty("methods").EnumerateArray())
            {
                string name = group.GetProperty("name").GetString() + "." + method.GetProperty("name").GetString();
                owned.Add(name);
                if (method.GetProperty("fallback").GetBoolean()) fallback.Add(name);
            }
        var additions = new HashSet<string>();
        string profiles = Path.Combine(directory, "profiles", $"{shard}-primary");
        if (Directory.Exists(profiles))
            foreach (string file in Directory.EnumerateFiles(profiles, "*.jsonl"))
                foreach (string line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.EndsWith('}')) continue;
                    using var document = JsonDocument.Parse(line);
                    var item = document.RootElement;
                    if (item.GetProperty("stage").GetString() == "native.compile-link" &&
                        item.GetProperty("detail").GetString()?.Contains(", default") == true &&
                        item.GetProperty("test").GetString() is { } name && owned.Contains(name) && !fallback.Contains(name))
                        additions.Add(name);
                }
        string path = Path.Combine(directory, $"{shard}-fallback.runsettings");
        var settings = XDocument.Load(path);
        var configuration = settings.Root!.Element("RunConfiguration")!;
        var filter = configuration.Element("TestCaseFilter")!;
        var existing = filter.Value.Split('|').ToHashSet(StringComparer.Ordinal);
        var conditions = additions.Order(StringComparer.Ordinal).Select(name => "FullyQualifiedName=" + name)
            .Where(condition => !existing.Contains(condition)).ToArray();
        if (conditions.Length != 0)
        {
            filter.Value += "|" + string.Join("|", conditions);
            configuration.Element("TreatNoTestsAsError")!.Value = "true";
            settings.Save(path);
        }
        File.WriteAllText(Path.Combine(directory, $"{shard}-observed-fallback.json"), JsonSerializer.Serialize(additions.Order(StringComparer.Ordinal)));
        Console.WriteLine($"Added {conditions.Length} fallback methods observed using default optimization.");
    }
}
