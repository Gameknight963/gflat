namespace gflat.Tests.Std.IO;

public sealed partial class PathRelativeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedZerosRemainDataAndResultsDoNotBorrowInputs(int optimization)
        => Run("""
            using Standard::IO;
            using Standard::Memory;
            using Standard::Text;

            int main()
            {
                char[7] from = ['/', 'a', '\0', 'b', '/', 'c', '!'];
                char[9] to = ['/', 'a', '\0', 'b', '/', 'd', '\0', 'e', '!'];
                Span<readonly(char)> origin = new(from, 6);
                Span<readonly(char)> target = new(to, 8);
                String result = Paths::GetRelativePath(origin, target, PathStyle::Unix);
                char[6] expected = ['.', '.', '/', 'd', '\0', 'e'];
                if (result.Length != 6 || result.Data[6] != '\0') return 1;
                for (nuint i = 0; i < 6; i++) if (result[i] != expected[i]) return 2;
                result[3] = 'x';
                if (to[5] != 'd' || from[5] != 'c') return 3;
                String cString = Paths::GetRelativePath(from, to, PathStyle::Unix);
                if (!cString.Equals(".")) return 4;
                String path = s"/a/b";
                path = Paths::GetRelativePath(path.Data, "/a/c", PathStyle::Unix);
                if (!path.Equals("../c")) return 5;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("siblings", 0)]
    [InlineData("siblings", 2)]
    [InlineData("same", 0)]
    [InlineData("same", 2)]
    [InlineData("root", 0)]
    [InlineData("root", 2)]
    public void EveryObservedAllocationFailurePreservesInputsAndAssignment(string scenario, int optimization)
    {
        string origin = scenario == "root" ? "C:/base/a" : "/base/a/b/c";
        string target = scenario switch { "same" => "/base/a/b/c/", "root" => "D:/data/b/../c/", _ => "/base/d/e/f/" };
        string expected = scenario switch { "same" => ".", "root" => "D:\\data\\c\\", _ => "../../../d/e/f/" };
        Run($$"""
            using Standard;
            using Standard::IO;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int requests = 0;
            int failAt = 0;
            int allocated = 0;
            int released = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    requests++;
                    if (requests == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            int main()
            {
                {
                    String origin = String::From("{{origin}}");
                    String target = String::From("{{target}}");
                    String output = s"previous";
                    readonly(char)* originData = origin.Data;
                    readonly(char)* targetData = target.Data;
                    readonly(char)* before = output.Data;
                    PathStyle style = PathStyle::{{(scenario == "root" ? "Windows" : "Unix")}};
                    int baseline = allocated - released;
                    int observed = 0;
                    {
                        requests = 0;
                        String result = Paths::GetRelativePath(origin.Data, target.Data, style);
                        if (!result.Equals("{{expected.Replace("\\", "\\\\")}}")) return 1;
                        observed = requests;
                    }
                    if (allocated - released != baseline || observed == 0) return 2;
                    for (int failure = 1; failure <= observed; failure++)
                    {
                        requests = 0;
                        failAt = failure;
                        bool caught = false;
                        try { output = Paths::GetRelativePath(origin.Data, target.Data, style); }
                        catch (BadAllocException* error) { caught = true; }
                        failAt = 0;
                        if (!caught || allocated - released != baseline || output.Data != before
                            || !output.Equals("previous") || origin.Data != originData || target.Data != targetData
                            || !origin.Equals("{{origin}}") || !target.Equals("{{target}}")) return 3;
                    }
                }
                if (allocated != released) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidRootsAndEmptySpansReportValidationErrorsWithTheAllocatorDenied(int optimization)
        => Run("""
            using Standard;
            using Standard::IO;
            using Standard::Memory;

            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) => null;
                public replace void Free(void* storage) nothrow { }
            }
            void Reject(readonly(char)* origin, readonly(char)* path, PathStyle style)
            {
                bool caught = false;
                try { Paths::GetRelativePath(origin, path, style); }
                catch (BadAllocException* error) { throw; }
                catch (Exception* error) { caught = true; }
                if (!caught) throw new Exception("Invalid path accepted");
            }
            int main()
            {
                Reject("", "/a", PathStyle::Unix);
                Reject("relative", "/a", PathStyle::Unix);
                Reject("/a", "relative", PathStyle::Unix);
                Reject("/a", "", PathStyle::Unix);
                Reject("C:base", "C:/a", PathStyle::Windows);
                Reject("C:/a", "D:target", PathStyle::Windows);
                Reject("C:/a", "//server", PathStyle::Windows);
                Reject("//?/C:/a", "C:/a", PathStyle::Windows);
                Reject("/a", "/b", (PathStyle)99);
                Span<readonly(char)> empty = new(((nuint)1).reinterpret<readonly(char)*>(), 0);
                Span<readonly(char)> valid = new("/a", 2);
                bool caught = false;
                try { Paths::GetRelativePath(empty, valid, PathStyle::Unix); }
                catch (BadAllocException* error) { throw; }
                catch (Exception* error) { caught = true; }
                if (!caught) return 1;
                caught = false;
                try { Paths::GetRelativePath(valid, empty, PathStyle::Unix); }
                catch (BadAllocException* error) { throw; }
                catch (Exception* error) { caught = true; }
                if (!caught) return 2;
                Span<readonly(char)> unreadable = new(((nuint)1).reinterpret<readonly(char)*>(), 1);
                caught = false;
                try { Paths::GetRelativePath(unreadable, unreadable, (PathStyle)99); }
                catch (BadAllocException* error) { throw; }
                catch (Exception* error) { caught = true; }
                if (!caught) return 3;
                return 0;
            }
            """, optimization);
}
