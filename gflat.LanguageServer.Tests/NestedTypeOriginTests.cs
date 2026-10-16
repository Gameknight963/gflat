using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class NestedTypeOriginTests
{
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void EmptyNestedSpecializationsDoNotBorrowTheirParentsMembers(string kind)
    {
        string text = $$"""
            namespace Library {
                public {{kind}} Family<T> {
                    public int unrelated;
                    public struct Item {}
                }
            }
            int main() {
                Library::Family<int>::Item empty = default;
                empty.notHere;
                return 0;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("empty-nested-origin.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        var model = new EditorModel(snapshot, [source]);
        int offset = text.IndexOf("empty.notHere", StringComparison.Ordinal) + "empty.".Length;
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset));
        Assert.DoesNotContain(completion.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "unrelated");
        var registered = snapshot.Checker!.GetTypeDeclaration("Library$Family$int.Item");
        Assert.NotNull(registered);
        Assert.Equal(text.IndexOf("public struct Item", StringComparison.Ordinal), registered.Span.Start);
    }
}
