namespace gflat.Tests.Std.Collections;

public sealed partial class LinkedListSearchTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/LinkedList.gf", "core/Collections/EqualityComparer.gf", "core/Collections/linked_list_search.gf"))
            .Append(new SourceFile("linked-list-search.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Linked list search exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("LINKED_LIST_SEARCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FirstLastAndValueRemovalMatchDotNetNodePositions(int optimization)
    {
        List<int[]> cases = [[], [1], [2, 1, 2, 1, 2], [int.MinValue, int.MaxValue, 0, int.MinValue]];
        var random = new Random(293847);
        for (int sample = 0; sample < 64; sample++)
            cases.Add(Enumerable.Range(0, random.Next(33)).Select(_ => random.Next(-3, 4)).ToArray());
        int[] queries = [-3, 2, int.MinValue, 0, 1, int.MaxValue, -2, 3, -1, 2, 0, 1];
        string path = Path.Combine(Path.GetTempPath(), "gflat_linked_search_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (int[] input in cases)
            {
                writer.Write(input.Length);
                foreach (int value in input) writer.Write(value);
                var list = new LinkedList<int>(input);
                foreach (int key in queries)
                {
                    var values = list.ToArray();
                    writer.Write(key);
                    writer.Write(Array.IndexOf(values, key));
                    writer.Write(Array.LastIndexOf(values, key));
                    writer.Write(list.Remove(key) ? 1 : 0);
                    writer.Write(list.Count);
                    long signature = 0;
                    foreach (int value in list) signature = unchecked(signature * 31 + value);
                    writer.Write(signature);
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            Run($$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int fgetc(void* file);
                LinkedList<int>::Node*? At(LinkedList<int>* source, int position)
                {
                    if (position < 0) return null;
                    LinkedList<int>::Node*? node = source.First;
                    for (int i = 0; i < position; i++) node = node!.Next;
                    return node;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int sample = 0; sample < {{cases.Count}}; sample++)
                    {
                        int count = 0;
                        if (fread(&count, 4, 1, file!) != 1) return 2;
                        LinkedList<int> list = new();
                        for (int i = 0; i < count; i++)
                        {
                            int value = 0;
                            if (fread(&value, 4, 1, file!) != 1) return 3;
                            list.AddLast(value);
                        }
                        for (int query = 0; query < {{queries.Length}}; query++)
                        {
                            int[5] row = default(int[5]);
                            long signature = 0l;
                            if (fread(&row[0], 4, 5, file!) != 5 || fread(&signature, 8, 1, file!) != 1) return 4;
                            int key = row[0];
                            LinkedList<int>::Node*? first = list.Find(&key);
                            LinkedList<int>::Node*? last = list.FindLast(&key);
                            if (first != At(&list, row[1]) || last != At(&list, row[2])) return 5;
                            if (Find(&list, &key) != first || FindLast(&list, &key) != last) return 6;
                            immutable(LinkedList<int>)* view = &list;
                            if (view.Contains(&key) != (row[1] >= 0) || Contains(view, &key) != (row[1] >= 0)) return 7;
                            bool removed = false;
                            if (query % 2 == 0) removed = list.RemoveValue(&key);
                            else removed = RemoveValue(&list, &key);
                            if (removed != (row[3] != 0) || list.Count != (nuint)row[4]) return 8;
                            long actual = 0l;
                            var iterator = list.GetEnumerator();
                            while (iterator.MoveNext()) actual = actual * 31l + (long)iterator.Current;
                            if (actual != signature) return 9;
                        }
                    }
                    if (fgetc(file!) != -1) return 10;
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }
}
