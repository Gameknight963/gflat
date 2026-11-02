using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length == 0) throw new ArgumentException("Use plan <assembly> <output> <shards> [durations.json], or report <directory>.");
if (args[0] == "report") { Reports.Write(args[1]); return; }
if (args[0] == "fallback") { FallbackPlan.Augment(args[1], int.Parse(args[2])); return; }
if (args[0] != "plan" || args.Length is < 4 or > 5) throw new ArgumentException("Use plan <assembly> <output> <shards> [durations.json].");
string assemblyPath = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
int count = int.Parse(args[3]);
if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
bool IsTest(MethodInfo method) => method.CustomAttributes.Any(attribute =>
{
    for (Type? type = attribute.AttributeType; type != null; type = type.BaseType)
        if (type.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute") return true;
    return false;
});
bool ExplicitOptimization(MethodInfo method) => method.GetParameters().Any(parameter =>
    parameter.ParameterType == typeof(int) && parameter.Name?.ToLowerInvariant() is
        "optimization" or "optimizationlevel" or "opt" or "optlevel");
var weights = args.Length == 5
    ? JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(args[4]))!
    : new Dictionary<string, double>();
var classes = assembly.GetTypes().Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
    .Select(type => (Type: type, Methods: type.GetMethods().Where(IsTest).ToArray()))
    .Where(group => group.Methods.Length != 0)
    .Select(group =>
    {
        int cases = group.Methods.Sum(method => Math.Max(1,
            method.CustomAttributes.Count(attribute => attribute.AttributeType.FullName == "Xunit.InlineDataAttribute"))
            * (ExplicitOptimization(method) ? 1 : 2));
        return new TestClass(group.Type.FullName!, group.Methods, weights.GetValueOrDefault(group.Type.FullName!, cases));
    })
    .OrderByDescending(group => group.Weight).ThenBy(group => group.Name, StringComparer.Ordinal).ToArray();
if (weights.Count != 0)
{
    // New classes need weights in the same units as the supplied durations.
    var estimates = classes.Where(group => weights.ContainsKey(group.Name))
        .Select(group => group.Weight / Math.Max(1, group.Methods.Sum(method => Math.Max(1,
            method.CustomAttributes.Count(attribute => attribute.AttributeType.FullName == "Xunit.InlineDataAttribute"))
            * (ExplicitOptimization(method) ? 1 : 2)))).Order().ToArray();
    double unit = estimates.Length == 0 ? 1 : estimates[estimates.Length / 2];
    classes = classes.Select(group => weights.ContainsKey(group.Name) ? group : group with { Weight = group.Weight * unit })
        .OrderByDescending(group => group.Weight).ThenBy(group => group.Name, StringComparer.Ordinal).ToArray();
}
var shards = Enumerable.Range(0, count).Select(_ => new List<TestClass>()).ToArray();
double[] totals = new double[count];
foreach (var group in classes)
{
    int shard = Enumerable.Range(0, count).MinBy(index => totals[index]);
    shards[shard].Add(group);
    totals[shard] += group.Weight;
}
Directory.CreateDirectory(output);
for (int shard = 0; shard < count; shard++)
{
    var groups = shards[shard];
    string[] primary = groups.Select(group => "FullyQualifiedName~" + group.Name + ".").ToArray();
    string[] fallback = groups.SelectMany(group => group.Methods.Where(method => !ExplicitOptimization(method))
        .Select(method => "FullyQualifiedName=" + group.Name + "." + method.Name)).ToArray();
    Settings(Path.Combine(output, $"{shard}-primary.runsettings"), primary, "0", "primary");
    Settings(Path.Combine(output, $"{shard}-fallback.runsettings"), fallback, "2", "fallback");
    Console.WriteLine($"Shard {shard}: {groups.Count} classes, {groups.Sum(group => group.Methods.Length)} methods, {fallback.Length} fallback methods, weight {totals[shard]:F1}");
}
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(shards.Select((groups, index) => new
{
    shard = index, classes = groups.Select(group => new { name = group.Name, methods = group.Methods.Select(method => new
    { name = method.Name, fallback = !ExplicitOptimization(method) }), weight = group.Weight })
}), new JsonSerializerOptions { WriteIndented = true }));

void Settings(string path, string[] filters, string optimization, string pass)
{
    var settings = new XDocument(new XElement("RunSettings", new XElement("RunConfiguration",
        new XElement("TestCaseFilter", filters.Length == 0 ? "FullyQualifiedName=__empty_shard__" : string.Join("|", filters)),
        new XElement("TreatNoTestsAsError", filters.Length != 0),
        new XElement("EnvironmentVariables", new XElement("GFLAT_OPT_LEVEL", optimization),
            new XElement("GFLAT_PROFILE_DIRECTORY", Path.Combine(output, "profiles", Path.GetFileNameWithoutExtension(path))),
            new XElement("GFLAT_PROFILE_PASS", pass)))));
    settings.Save(path);
}

record TestClass(string Name, MethodInfo[] Methods, double Weight);
