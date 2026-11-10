using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class ForeignLanguageEditorTests
{
    [Fact]
    public void NativeReferenceResultsHaveBorrowedTypesAndReferenceSignatures()
    {
        const string text = """
            #lang cpp
            int storage = 42;
            int& Borrow() noexcept { return storage; }
            #lang gflat
            int main() { Borrow() = 41; int* address = &Borrow(); return *address; }
            """;
        var source = new SourceFile(Path.GetFullPath("native-reference-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Borrow()", StringComparison.Ordinal);
        var signatures = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + "Borrow(".Length));
        string label = Assert.Single(signatures.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString()!;
        Assert.Contains("ref int Borrow()", label);
        Assert.DoesNotContain("int*", label);
        string hover = JsonSerializer.Serialize(model.Hover(source.Path, call));
        Assert.Contains("Borrow", hover);
        Assert.DoesNotContain("__gflat_foreign_", hover);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void SameNamedFileLocalFunctionsKeepTheirOwnEditorSignatures(string language)
    {
        var first = new SourceFile(Path.GetFullPath("native-local-signature-first.gf"),
            $"#lang {language}\nstatic int Read(int value) {{ return value; }}\n#lang gflat\nint First() => Read(1);");
        var second = new SourceFile(Path.GetFullPath("native-local-signature-second.gf"),
            $"#lang {language}\nstatic double Read(double value) {{ return value; }}\n#lang gflat\ndouble Second() => Read(1.0);");
        var snapshot = new AnalysisSnapshot([first, second], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [first, second]);
        foreach (var (source, expected) in new[] { (first, "public int Read(int value)"), (second, "public double Read(double value)") })
        {
            int call = source.Text.LastIndexOf("Read(", StringComparison.Ordinal);
            var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
            Assert.Equal(expected, Assert.Single(help.GetProperty("signatures").EnumerateArray()).GetProperty("label").GetString());
        }
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void FileLocalNativeFunctionsHaveSourceNamesAndStayOutOfOtherFilesCompletion(string language)
    {
        string Native(string name) => language == "cpp"
            ? $"namespace Native {{ static int {name}() {{ return 42; }} }}"
            : $"static int {name}(void) {{ return 42; }}";
        string imports = language == "cpp" ? "using Native;\n" : "";
        var first = new SourceFile(Path.GetFullPath("native-local-first.gf"),
            $"#lang {language}\n{Native("FirstOnly")}\n#lang gflat\n{imports}int First() => FirstOnly();");
        var second = new SourceFile(Path.GetFullPath("native-local-second.gf"),
            $"#lang {language}\n{Native("SecondOnly")}\n#lang gflat\n{imports}int Second() => SecondOnly();");
        var snapshot = new AnalysisSnapshot([first, second], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [first, second]);
        int call = first.Text.LastIndexOf("FirstOnly(", StringComparison.Ordinal);
        var completion = JsonSerializer.SerializeToElement(model.Completion(first.Path, call));
        var labels = completion.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("FirstOnly", labels);
        Assert.DoesNotContain("SecondOnly", labels);
        Assert.DoesNotContain(labels, label => label?.StartsWith("__gflat_foreign_local_", StringComparison.Ordinal) == true);
        string signature = JsonSerializer.Serialize(model.SignatureHelp(first.Path, call + "FirstOnly(".Length));
        Assert.Contains("FirstOnly", signature);
        Assert.DoesNotContain("__gflat_foreign_local_", signature);
        string hover = JsonSerializer.Serialize(model.Hover(first.Path, call));
        Assert.Contains("FirstOnly", hover);
        Assert.DoesNotContain("__gflat_foreign_local_", hover);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void NativeConstVoidResultsRetainReadonlyPointeesInSignatures(string language)
    {
        string text = "#lang " + language + "\nconst void* Read(void) { return 0; }\n" + """
            #lang gflat
            readonly(void)*? Use() => Read();
            """;
        var source = new SourceFile(Path.GetFullPath("native-const-void-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Read(", StringComparison.Ordinal);
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
        Assert.Equal("public readonly(void)*? Read()", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Theory]
    [InlineData("c", "restrict")]
    [InlineData("cpp", "__restrict")]
    public void NativeRestrictedPointersExposeTheirPointeeQualifiers(string language, string qualifier)
    {
        string text = "#lang " + language + "\nint Read(const int* " + qualifier + " value) { return *value; }\n" + """
            #lang gflat
            int Use(readonly(int)* value) => Read(value);
            """;
        var source = new SourceFile(Path.GetFullPath("native-restrict-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Read(", StringComparison.Ordinal);
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
        Assert.Equal("public int Read(readonly(int)*? value)", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void ForwardDeclaredNativeHandlesAppearInCallSignatures()
    {
        const string text = """
            #lang c
            struct Handle;
            int Read(const struct Handle* value) { return 42; }
            #lang gflat
            int Use(Handle* value) => Read(value);
            """;
        var source = new SourceFile(Path.GetFullPath("native-forward-handle.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Read(", StringComparison.Ordinal);
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
        string label = signature.GetProperty("signatures")[0].GetProperty("label").GetString()!;
        Assert.Equal("public int Read(readonly(Handle)*? value)", label);
    }

    [Fact]
    public void NativeNonthrowingContractsAppearInSignatures()
    {
        const string text = """
            #lang cpp
            int ReadGlobal() noexcept(1) { return 21; }
            int Conditional() noexcept(false) { return 21; }
            struct Factory {
                int value;
                int Read() const noexcept { return value; }
            };
            #lang gflat
            int Use(readonly(Factory)* value) => ReadGlobal() + value.Read() + Conditional();
            """;
        var source = new SourceFile(Path.GetFullPath("native-nothrow-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        foreach (var (name, noThrow) in new[] { ("ReadGlobal(", true), ("Read(", true), ("Conditional(", false) })
        {
            int call = text.LastIndexOf(name, StringComparison.Ordinal);
            var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + name.Length));
            string label = signature.GetProperty("signatures")[0].GetProperty("label").GetString()!;
            Assert.Equal(noThrow, label.Contains("nothrow", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NativeTrailingReturnsExposeTheDeclaredResultAndReceiver()
    {
        const string text = """
            #lang cpp
            struct Factory {
                int value;
                auto Read(int input) const noexcept -> int { return value + input; }
            };
            #lang gflat
            int Use(readonly(Factory)* value) => value.Read(1);
            """;
        var source = new SourceFile(Path.GetFullPath("native-trailing-return-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Read(", StringComparison.Ordinal);
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
        string label = signature.GetProperty("signatures")[0].GetProperty("label").GetString()!;
        Assert.Contains("readonly int", label);
        Assert.Contains("Read(int input)", label);
        Assert.DoesNotContain("auto", label);
    }

    [Fact]
    public void NativeMethodCallbackResultsKeepReceiverQualifiersSeparate()
    {
        const string text = """
            #lang cpp
            struct Factory { int (*Read() const noexcept)(int) { return nullptr; } };
            #lang gflat
            void Use(readonly(Factory)* value) { int(int)*? callback = value.Read(); }
            """;
        var source = new SourceFile(Path.GetFullPath("native-method-callback-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Read(", StringComparison.Ordinal);
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
        string label = signature.GetProperty("signatures")[0].GetProperty("label").GetString()!;
        Assert.Contains("readonly int(int)*?", label);
        Assert.Contains("Read()", label);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void NativeSignaturesEscapeOnlyNamesThatRequireIt(string language)
    {
        string text = "#lang " + language + "\n" + """
            struct Payload { int number; };
            struct readonly { int number; };
            int Read(const struct Payload* value) { return value->number; }
            int ReadKeyword(const struct readonly* immutable) { return immutable->number; }
            #lang gflat
            int Use(Payload* value, @readonly* keyword) => Read(value) + ReadKeyword(keyword);
            """;
        var source = new SourceFile(Path.GetFullPath("native-signature-spelling.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        foreach (var (name, expected) in new[] {
            ("Read(", "public int Read(readonly(Payload)*? value)"),
            ("ReadKeyword(", "public int ReadKeyword(readonly(@readonly)*? @immutable)") })
        {
            int call = text.LastIndexOf(name, StringComparison.Ordinal);
            var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + name.Length));
            Assert.Equal(expected, signature.GetProperty("signatures")[0].GetProperty("label").GetString());
        }
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void NativeUnionPointersAppearInSignatures(string language)
    {
        string text = "#lang " + language + "\nunion Payload { int number; double floating; };\n" +
            "int Read(const union Payload* value) { return value->number; }\n" +
            "#lang gflat\nint Use(Payload* value) => Read(value);";
        var source = new SourceFile(Path.GetFullPath("native-union-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Read(", StringComparison.Ordinal);
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
        Assert.Contains("readonly(Payload)*?", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void NativeUnicodePointersShowTheirCodeUnitTypes()
    {
        const string text = """
            #lang cpp
            const char16_t* Utf16Text() { return u"\u03a9"; }
            const char32_t* Utf32Text() { return U"\U0001f642"; }
            #lang gflat
            ushort Read16() => (Utf16Text()!)[0];
            uint Read32() => (Utf32Text()!)[0];
            """;
        var source = new SourceFile(Path.GetFullPath("native-unicode-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        foreach (var (name, type) in new[] { ("Utf16Text(", "readonly(ushort)*?"), ("Utf32Text(", "readonly(uint)*?") })
        {
            int call = text.LastIndexOf(name, StringComparison.Ordinal);
            var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + name.Length));
            Assert.Contains(type, signature.GetProperty("signatures")[0].GetProperty("label").GetString());
        }
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void NativeCallbackResultsHavePointerSignatures(string language)
    {
        string text = "#lang " + language + "\nint (*Echo(int (*callback)(int)))(int) { return callback; }\n" +
            "#lang gflat\nint Double(int value) => value * 2;\nint Read() { int(int)*? callback = Echo(Double); return callback!(21); }";
        var source = new SourceFile(Path.GetFullPath("native-callback-result-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int call = text.LastIndexOf("Echo(", StringComparison.Ordinal);
        var signature = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + 5));
        Assert.Contains("int(int)*?", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void ConstNativeCallbackFieldsShowReadonlyTypes(string language)
    {
        string text = "#lang " + language + "\nstruct Callbacks { int (*const Read)(int); };\n" +
            "#lang gflat\nint Call(Callbacks* value) => (value.Read!)(21);";
        var source = new SourceFile(Path.GetFullPath("native-const-callback-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int field = text.LastIndexOf("Read", StringComparison.Ordinal);
        string hover = JsonSerializer.Serialize(model.Hover(source.Path, field));
        Assert.Contains("readonly", hover);
        Assert.Contains("Read", hover);
    }

    [Fact]
    public void InheritedNativeMethodsAppearInCompletionAndHovers()
    {
        const string text = """
            #lang cpp
            namespace Native {
                struct Base { int Read() const { return 42; } };
                struct Derived : Base {};
            }
            #lang gflat
            int Read(Native::Derived* value) => value.Read();
            """;
        var source = new SourceFile(Path.GetFullPath("native-inheritance-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int method = text.LastIndexOf("Read", StringComparison.Ordinal);
        string hover = JsonSerializer.Serialize(model.Hover(source.Path, method));
        Assert.Contains("Read", hover);
        Assert.Contains("Derived", hover);
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, method));
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Read");
    }

    [Fact]
    public void NativeEnumConstantsAndFunctionParametersHaveTypeAwareHovers()
    {
        const string text = """
            #lang cpp
            namespace Native {
                enum class Mode : unsigned short { Answer = 42 };
                Mode Echo(Mode input) { return input; }
            }
            #lang gflat
            int main() => (int)Native::Echo(Native::Mode::Answer);
            """;
        var source = new SourceFile(Path.GetFullPath("native-enum-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        string member = JsonSerializer.Serialize(model.Hover(source.Path, text.LastIndexOf("Answer", StringComparison.Ordinal)));
        Assert.Contains("Mode", member);
        Assert.Contains("Answer", member);
        string signature = JsonSerializer.Serialize(model.SignatureHelp(source.Path, text.LastIndexOf("Echo(", StringComparison.Ordinal) + 5));
        Assert.Contains("Mode", signature);
        Assert.Contains("input", signature);
    }

    [Fact]
    public void NativeConstructionShowsPublicConstructorAndOriginalParameterName()
    {
        const string text = """
            #lang cpp
            namespace Native {
                class Widget {
                    int value;
                public:
                    Widget(int initial) : value(initial) {}
                    int Read() const { return value; }
                };
            }
            #lang gflat
            int main()
            {
                Native::Widget* widget = new* Native::Widget(42);
                int result = widget.Read();
                delete widget;
                return result;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("native-constructor-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int creation = text.IndexOf("new*", StringComparison.Ordinal);
        string hover = JsonSerializer.Serialize(model.Hover(source.Path, creation));
        string signature = JsonSerializer.Serialize(model.SignatureHelp(source.Path, text.IndexOf("(42)", creation, StringComparison.Ordinal) + 1));
        foreach (string display in new[] { hover, signature })
        {
            Assert.Contains("Widget", display);
            Assert.Contains("initial", display);
            Assert.DoesNotContain("__gflat_native_", display);
            Assert.DoesNotContain("private", display);
        }
    }

    [Fact]
    public void ImportedRecordFieldsAndMembersHaveCallSiteHoversAndCompletion()
    {
        const string text = """
            #lang cpp
            namespace Native {
                struct Counter { int value; int Read() const { return value; } static int Twice(int value) { return value * 2; } };
            }
            #lang gflat
            int main()
            {
                Native::Counter counter = default(Native::Counter);
                return counter.Read() + counter.value + Native::Counter::Twice(21);
            }
            """;
        var source = new SourceFile(Path.GetFullPath("foreign-record-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int field = text.LastIndexOf("value", StringComparison.Ordinal);
        int method = text.LastIndexOf("Read", StringComparison.Ordinal);
        Assert.Contains("int", JsonSerializer.Serialize(model.Hover(source.Path, field)));
        Assert.Contains("Read", JsonSerializer.Serialize(model.Hover(source.Path, method)));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, field));
        var labels = completion.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("value", labels);
        Assert.Contains("Read", labels);
        int staticMethod = text.LastIndexOf("Twice", StringComparison.Ordinal);
        Assert.Contains("Twice", JsonSerializer.Serialize(model.Hover(source.Path, staticMethod)));
        var statics = JsonSerializer.SerializeToElement(model.Completion(source.Path, staticMethod));
        Assert.Contains(statics.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Twice");
    }

    [Fact]
    public void WorkspaceInvalidatesWhenAnEarlierNestedNativeHeaderAppears()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_foreign_header_order_" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(directory, "first"), second = Path.Combine(directory, "second");
        Directory.CreateDirectory(Path.Combine(first, "headers"));
        Directory.CreateDirectory(Path.Combine(second, "headers"));
        try
        {
            File.WriteAllText(Path.Combine(second, "headers", "api.h"), "int FromHeader(void);");
            var source = new SourceFile(Path.Combine(directory, "main.gf"),
                "#lang c\n#include <headers/api.h>\n#lang gflat\ndouble Read() => (double)FromHeader();");
            File.WriteAllText(source.Path, source.Text);
            var options = new CompilationOptions { NoStdLib = true, IncludeDirectories = [first, second] };
            var cache = new WorkspaceAnalysisCache();
            var before = cache.Get([source], options).Snapshot;
            Assert.Empty(before.Diagnostics);
            Assert.Same(before, cache.Get([source], options).Snapshot);
            File.WriteAllText(Path.Combine(first, "headers", "api.h"), "double FromHeader(void);");
            var after = cache.Get([source], options).Snapshot;
            Assert.NotSame(before, after);
            Assert.Empty(after.Diagnostics);
            var method = after.Nodes.Select(node => node.Node).OfType<gflat.ast.MethodDeclaration>().Single(method => method.Name == "FromHeader");
            Assert.Equal("double", TypeChecker.TypeName(method.ReturnType));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void WorkspaceReusesForeignAnalysisAndInvalidatesOnNativeHeaderContentChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_foreign_editor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string header = Path.Combine(directory, "api.h");
            File.WriteAllText(header, "static int native() { return 1; }");
            DateTime written = File.GetLastWriteTimeUtc(header);
            var source = new SourceFile(Path.Combine(directory, "main.gf"), "#lang cpp\n#include \"api.h\"\n#lang gflat\nint main() { return native(); }");
            File.WriteAllText(source.Path, source.Text);
            var cache = new WorkspaceAnalysisCache();
            var options = new CompilationOptions { NoStdLib = true };
            var first = cache.Get([source], options).Snapshot;
            Assert.Empty(first.Diagnostics);
            Assert.Single(first.ForeignDependencies.Values, input => !input.IsDirectory);
            Assert.DoesNotContain(first.PreprocessorDependencies, path => path.EndsWith("clang.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Same(first, cache.Get([source], options).Snapshot);
            File.WriteAllText(header, "static int native() { return 2; }");
            File.SetLastWriteTimeUtc(header, written);
            var changed = cache.Get([source], options).Snapshot;
            Assert.NotSame(first, changed);
            Assert.Empty(changed.Diagnostics);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ForeignCodeHasNoGflatHoverCompletionOrSemanticClassifications()
    {
        const string text = "#lang cpp\nint native() { return 42; }\n#lang gflat\nint Read(int value) { return value; }\n";
        var source = new SourceFile(Path.GetFullPath("foreign-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true }, checkTypes: false);
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        int offset = text.IndexOf("native", StringComparison.Ordinal);
        Assert.Null(model.Hover(source.Path, offset));
        Assert.Null(model.Definition(source.Path, offset));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset));
        Assert.Empty(completion.GetProperty("items").EnumerateArray());
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int line = 0;
        for (int index = 0; index < data.Length; index += 5)
        {
            line += data[index];
            Assert.NotEqual(1, line);
        }
        Assert.NotNull(model.Hover(source.Path, text.LastIndexOf("value", StringComparison.Ordinal)));
    }

    [Fact]
    public void FormattingPreservesForeignTextExactlyAndFormatsGflatAfterward()
    {
        const string foreign = "#lang cpp\nint  native( ){return  42;}\n#define OPEN {\n";
        const string normal = "#lang gflat\nint Read(){return 1;}\n";
        var source = new SourceFile(Path.GetFullPath("foreign-format.gf"), foreign + normal);
        var edits = SourceFormatter.Format(source, new());
        Assert.NotEmpty(edits);
        Assert.All(edits, edit => Assert.True(edit.Range.Start.Line >= 3));
    }

    [Fact]
    public void ImportedFreeFunctionsHaveGflatCallSiteHovers()
    {
        const string text = "#lang cpp\nint native(int x) { return x + 1; }\n#lang gflat\nint main() { return native(2); }\n";
        var source = new SourceFile(Path.GetFullPath("foreign-call.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, [source]);
        var hover = model.Hover(source.Path, text.LastIndexOf("native", StringComparison.Ordinal));
        Assert.NotNull(hover);
        Assert.Contains("native", JsonSerializer.Serialize(hover));
    }
}
