using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

static class Reports
{
    public static void Write(string directory)
    {
        directory = Path.GetFullPath(directory);
        var events = Directory.EnumerateFiles(directory, "pipeline-*.jsonl", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line) && line.EndsWith('}')).Select(line => JsonSerializer.Deserialize<Event>(line)!)).ToArray();
        var lines = new List<string> { "# Test pipeline timings", "", "Stage totals are cumulative worker time. Nested stages overlap and must not be added together.", "", "| Stage | Calls | Total seconds | Median ms | P95 ms |", "| --- | ---: | ---: | ---: | ---: |" };
        foreach (var group in events.GroupBy(item => item.stage).OrderByDescending(group => group.Sum(item => item.milliseconds)))
        {
            var times = group.Select(item => item.milliseconds).Order().ToArray();
            lines.Add(FormattableString.Invariant($"| {group.Key} | {times.Length} | {times.Sum() / 1000:F2} | {times[times.Length / 2]:F2} | {times[Math.Min(times.Length - 1, (int)(times.Length * .95))]:F2} |"));
        }
        lines.AddRange(["", "## Optimization passes", "", "| Pass | Test cases | Native compiles | Default optimization compiles |", "| --- | ---: | ---: | ---: |"]);
        foreach (var group in events.GroupBy(item => item.pass ?? "local").OrderBy(group => group.Key))
            lines.Add($"| {group.Key} | {group.Count(item => item.stage == "test")} | {group.Count(item => item.stage == "native.compile-link")} | {group.Count(item => item.stage == "native.compile-link" && item.detail?.Contains(", default") == true)} |");
        lines.AddRange(["", "## Slowest test methods", "", "Durations include all theory cases in each method.", "", "| Method | Total seconds |", "| --- | ---: |"]);
        foreach (var group in events.Where(item => item.stage == "test" && item.test != null).GroupBy(item => item.test!).OrderByDescending(group => group.Sum(item => item.milliseconds)).Take(30))
            lines.Add(FormattableString.Invariant($"| {group.Key} | {group.Sum(item => item.milliseconds) / 1000:F2} |"));
        var weights = new Dictionary<string, double>();
        var incomplete = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.trx", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(file);
            XNamespace ns = document.Root!.Name.Namespace;
            var names = document.Descendants(ns + "UnitTest").ToDictionary(node => (string)node.Attribute("id")!, node => (string?)node.Element(ns + "TestMethod")?.Attribute("className"));
            foreach (var result in document.Descendants(ns + "UnitTestResult"))
                if (names.GetValueOrDefault((string)result.Attribute("testId")!) is string name && result.Attribute("duration") is { } duration)
                {
                    if ((string?)result.Attribute("outcome") != "Passed") incomplete.Add(name);
                    else weights[name] = weights.GetValueOrDefault(name) + TimeSpan.Parse(duration.Value, CultureInfo.InvariantCulture).TotalMilliseconds;
                }
        }
        foreach (string name in incomplete) weights.Remove(name);
        File.WriteAllText(Path.Combine(directory, "class-durations.json"), JsonSerializer.Serialize(weights, new JsonSerializerOptions { WriteIndented = true }));
        string markdown = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        File.WriteAllText(Path.Combine(directory, "timings.md"), markdown);
        Console.Write(markdown);
    }

    private sealed record Event(string stage, string? detail, string? test, double milliseconds, string? pass = null);
}
