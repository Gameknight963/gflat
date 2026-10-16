using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private string DescribeSourceCallable(MethodDeclaration method, string? owner = null, bool capitalized = false)
    {
        string kind = method.PropertyName switch
        {
            null => "method",
            "$index" => "indexer accessor",
            _ => "property accessor"
        };
        string name = method.PropertyName == "$index" ? "this[]" : method.PropertyName ?? DisplayName(method.Name);
        if (owner != null) name = DisplayName(owner) + "." + name;
        if (capitalized) kind = char.ToUpperInvariant(kind[0]) + kind[1..];
        return $"{kind} '{name}'";
    }
}
