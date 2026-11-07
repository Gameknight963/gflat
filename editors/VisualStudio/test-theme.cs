using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;

namespace gflat.PackageTests
{
    // Exercise the production listener with real VS formatting objects. Interface
    // proxies supply the maps without starting an IDE or changing user settings.
    public static class ThemeColorCheck
    {
        private sealed class Proxy : RealProxy
        {
            private readonly Func<IMethodCallMessage, object> call;
            private readonly Type interfaceType;
            public Proxy(Type type, Func<IMethodCallMessage, object> call) : base(type) { this.call = call; interfaceType = type; }
            public override IMessage Invoke(IMessage message)
            {
                var request = (IMethodCallMessage)message;
                try {
                    object result = request.MethodName == "GetType" ? interfaceType : call(request);
                    return result as IMessage ?? new ReturnMessage(result, null, 0, request.LogicalCallContext, request);
                }
                catch (Exception error) { return new ReturnMessage(error, request); }
            }
        }

        private static T Interface<T>(Func<IMethodCallMessage, object> call)
        { return (T)new Proxy(typeof(T), call).GetTransparentProxy(); }

        public static void RunTextMateEnter(Assembly assembly, string configurationPath)
        {
            var configurationType = assembly.GetType("Microsoft.VisualStudio.TextMate.Core.LanguageConfiguration.LanguageConfigurationContract", true);
            var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
            var configuration = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
                .Invoke(null, new object[] { System.IO.File.ReadAllText(configurationPath), configurationType });
            var rules = (System.Collections.IEnumerable)configurationType.GetProperty("OnEnterRules").GetValue(configuration);
            var helperType = assembly.GetType("Microsoft.VisualStudio.TextMate.Core.Editor.LanguageConfiguration.Indentation.IndentationHelperService", true);
            var helper = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(helperType);
            var managerField = helperType.GetField("_indentationManagerService", BindingFlags.NonPublic | BindingFlags.Instance);
            var manager = new Proxy(managerField.FieldType, m => {
                if (m.MethodName != "GetIndentation") throw new Exception("Unexpected indentation option query: " + m.MethodName);
                var arguments = m.Args;
                arguments[2] = true; arguments[3] = 4; arguments[4] = 4;
                return new ReturnMessage(null, arguments, arguments.Length, m.LogicalCallContext, m);
            }).GetTransparentProxy();
            managerField.SetValue(helper, manager);
            const string document = "{\r\n}";
            Microsoft.VisualStudio.Text.ITextSnapshot snapshot = null;
            Microsoft.VisualStudio.Text.ITextSnapshotLine line = null, closer = null;
            snapshot = Interface<Microsoft.VisualStudio.Text.ITextSnapshot>(m => {
                if (m.MethodName == "get_Length") return document.Length;
                if (m.MethodName == "get_LineCount") return 2;
                if (m.MethodName == "get_Item") return document[(int)m.Args[0]];
                if (m.MethodName == "GetLineFromLineNumber") return (int)m.Args[0] == 0 ? line : closer;
                if (m.MethodName == "GetText") {
                    if (m.Args.Length == 0) return document;
                    if (m.Args.Length == 2) return document.Substring((int)m.Args[0], (int)m.Args[1]);
                    var span = (Microsoft.VisualStudio.Text.Span)m.Args[0];
                    return document.Substring(span.Start, span.Length);
                }
                throw new Exception("Unexpected Enter snapshot operation: " + m.MethodName);
            });
            Func<bool, Microsoft.VisualStudio.Text.ITextSnapshotLine> makeLine = closing => Interface<Microsoft.VisualStudio.Text.ITextSnapshotLine>(m => {
                int start = closing ? 3 : 0, end = start + 1, breakEnd = closing ? 4 : 3;
                if (m.MethodName == "GetText") return closing ? "}" : "{";
                if (m.MethodName == "get_Snapshot") return snapshot;
                if (m.MethodName == "get_LineNumber") return closing ? 1 : 0;
                if (m.MethodName == "get_Extent") return new Microsoft.VisualStudio.Text.SnapshotSpan(snapshot, start, 1);
                if (m.MethodName == "get_ExtentIncludingLineBreak") return new Microsoft.VisualStudio.Text.SnapshotSpan(snapshot, start, breakEnd - start);
                if (m.MethodName == "get_Start") return new Microsoft.VisualStudio.Text.SnapshotPoint(snapshot, start);
                if (m.MethodName == "get_End") return new Microsoft.VisualStudio.Text.SnapshotPoint(snapshot, end);
                if (m.MethodName == "get_EndIncludingLineBreak") return new Microsoft.VisualStudio.Text.SnapshotPoint(snapshot, breakEnd);
                throw new Exception("Unexpected Enter line operation: " + m.MethodName);
            });
            line = makeLine(false); closer = makeLine(true);
            var buffer = Interface<Microsoft.VisualStudio.Text.ITextBuffer>(m => {
                if (m.MethodName == "CreateEdit") throw new Exception("Enter indentation attempted a second buffer edit after brace completion");
                throw new Exception("Unexpected Enter buffer operation: " + m.MethodName);
            });
            var view = Interface<Microsoft.VisualStudio.Text.Editor.ITextView>(m => m.MethodName == "get_TextBuffer" ? buffer : null);
            var apply = helperType.GetMethod("ApplyOnEnterRule", BindingFlags.NonPublic | BindingFlags.Instance);
            int checkedRules = 0;
            foreach (var rule in rules)
            {
                var result = apply.Invoke(helper, new[] { (object)view, line, rule });
                if (!Equals(result.GetType().GetField("Item2").GetValue(result), 4))
                    throw new Exception("Enter between braces must indent the body by one level: " + result);
                checkedRules++;
            }
            if (checkedRules == 0) throw new Exception("No brace Enter rule was tested");
        }

        public static void RunLibraryProtection(Assembly assembly)
        {
            var listener = assembly.GetType("gflat.VisualStudio.GflatLibraryProtection", true);
            bool created = false, applied = false, disposed = false;
            var snapshot = Interface<Microsoft.VisualStudio.Text.ITextSnapshot>(m => m.MethodName == "get_Length" ? (object)42 : null);
            var region = Interface<Microsoft.VisualStudio.Text.IReadOnlyRegion>(m => null);
            var edit = Interface<Microsoft.VisualStudio.Text.IReadOnlyRegionEdit>(m => {
                if (m.MethodName == "CreateReadOnlyRegion") {
                    var span = (Microsoft.VisualStudio.Text.Span)m.Args[0];
                    if (span.Start != 0 || span.Length != 42 ||
                        (Microsoft.VisualStudio.Text.SpanTrackingMode)m.Args[1] != Microsoft.VisualStudio.Text.SpanTrackingMode.EdgeInclusive ||
                        (Microsoft.VisualStudio.Text.EdgeInsertionMode)m.Args[2] != Microsoft.VisualStudio.Text.EdgeInsertionMode.Deny)
                        throw new Exception("Library protection must include the whole buffer and both insertion edges");
                    created = true;
                    return region;
                }
                if (m.MethodName == "Apply") { applied = true; return snapshot; }
                if (m.MethodName == "Dispose") { disposed = true; return null; }
                throw new Exception("Unexpected read-only region edit operation: " + m.MethodName);
            });
            var buffer = Interface<Microsoft.VisualStudio.Text.ITextBuffer>(m =>
                m.MethodName == "get_CurrentSnapshot" ? (object)snapshot :
                m.MethodName == "CreateReadOnlyRegionEdit" ? edit : null);
            var result = listener.GetMethod("Protect", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { buffer });
            if (!created || !applied || !disposed || !Object.ReferenceEquals(result, region))
                throw new Exception("Library protection did not commit its read-only region");
            if (!typeof(Microsoft.VisualStudio.Text.Editor.IWpfTextViewCreationListener).IsAssignableFrom(listener))
                throw new Exception("Library protection must be registered as a view creation listener");
        }

        public static void RunTextMateTheme(Assembly assembly, string grammarPath)
        {
            var grammarInterface = assembly.GetType("Microsoft.VisualStudio.TextMate.Core.Parser.IGrammar", true);
            var repositoriesInterface = assembly.GetType("Microsoft.VisualStudio.TextMate.Core.Hosting.ICustomRepositoryService", true);
            var grammar = new Proxy(grammarInterface, m => m.MethodName == "get_SourcePath" ? grammarPath : null).GetTransparentProxy();
            var repositories = new Proxy(repositoriesInterface, m => { throw new Exception("Visual Studio fell back to repository/default themes instead of loading the packaged gflat theme"); }).GetTransparentProxy();
            var registry = Interface<IClassificationTypeRegistryService>(m => {
                if (m.MethodName != "GetClassificationType") throw new Exception("Unexpected registry operation: " + m.MethodName);
                string name = (string)m.Args[m.Args.Length - 1];
                return new Proxy(((MethodInfo)m.MethodBase).ReturnType, r => r.MethodName == "get_Classification" ? name : null).GetTransparentProxy();
            });
            var maps = Interface<IClassificationFormatMapService>(m => { throw new Exception("The theme must use VS classifications rather than generated colors"); });
            var themeType = assembly.GetType("Microsoft.VisualStudio.TextMate.Core.Theming.TMTheme", true);
            var load = themeType.GetMethod("LoadTheme", BindingFlags.Public | BindingFlags.Static);
            var theme = load.Invoke(null, new[] { repositories, grammar, registry, maps });
            string actual = (string)themeType.GetField("FilePath").GetValue(theme);
            string expected = System.IO.Path.ChangeExtension(grammarPath, ".tmTheme");
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Visual Studio loaded " + actual + " instead of " + expected);
            if (themeType.GetField("_scopeMap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(theme) == null)
                throw new Exception("Visual Studio did not load the packaged theme mappings");
            foreach (var mapping in new[] {
                new[] { "entity.name.qualifier.gflat", "identifier" },
                new[] { "entity.name.type.gflat", "type" },
                new[] { "keyword.control.gflat", "keyword - control" },
                new[] { "keyword.other.gflat", "keyword" },
                new[] { "entity.name.function.preprocessor.gflat", "cppMacro" },
                new[] { "keyword.control.directive.gflat", "preprocessor keyword" },
                new[] { "constant.numeric.gflat", "number" },
                new[] { "comment.line.documentation.gflat", "xml doc comment - text" },
                new[] { "entity.name.tag.documentation.gflat", "xml doc comment - name" },
                new[] { "comment.line.double-slash.gflat", "comment" },
                new[] { "comment.block.gflat", "comment" }
            }) {
                var scope = themeType.GetMethod("GetComplexScopeMap", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(theme, new object[] { mapping[0] });
                if (scope == null) throw new Exception("VS did not load scope " + mapping[0]);
                var settings = scope.GetType().GetField("_simpleSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scope);
                if (settings == null) throw new Exception("VS did not load settings for " + mapping[0]);
                var tag = (ClassificationTag)settings.GetType().GetField("ClassificationTag").GetValue(settings);
                if (tag.ClassificationType.Classification != mapping[1]) throw new Exception("Incorrect VS classification for " + mapping[0]);
            }
        }

        public static void Run(Type listenerType)
        {
            foreach (string classification in new[] { "keyword", "cppMacro", "preprocessor keyword", "identifier", "number", "string", "operator", "punctuation" })
                RunClassification(listenerType, classification);
        }

        private static void RunClassification(Type listenerType, string classification)
        {
            var type = Interface<IClassificationType>(m => m.MethodName == "get_Classification" ? classification : null);
            var registry = Interface<IClassificationTypeRegistryService>(m =>
                m.MethodName == "GetClassificationType" && (string)m.Args[0] == classification ? type : null);
            var source = TextFormattingRunProperties.CreateTextFormattingRunProperties(new Typeface("Consolas"), 20, Colors.Red);
            var original = TextFormattingRunProperties.CreateTextFormattingRunProperties(new Typeface("Segoe UI"), 13, Colors.Blue)
                .SetBackgroundBrush(Brushes.Black).SetBold(true);
            var target = original;
            int subscriptions = 0, writes = 0;
            var sourceMap = Interface<IClassificationFormatMap>(m => {
                switch (m.MethodName) {
                    case "GetTextProperties": return source;
                    case "add_ClassificationFormatMappingChanged": subscriptions++; return null;
                    case "remove_ClassificationFormatMappingChanged": subscriptions--; return null;
                    default: throw new InvalidOperationException("Unexpected source operation: " + m.MethodName);
                }
            });
            var targetMap = Interface<IClassificationFormatMap>(m => {
                if (m.MethodName == "GetTextProperties") return target;
                if (m.MethodName == "SetTextProperties") { target = (TextFormattingRunProperties)m.Args[1]; writes++; return null; }
                throw new InvalidOperationException("Unexpected tooltip operation: " + m.MethodName);
            });
            var maps = Interface<IClassificationFormatMapService>(m => {
                if (m.MethodName != "GetClassificationFormatMap") throw new InvalidOperationException(m.MethodName);
                switch ((string)m.Args[0]) {
                    case "text": return sourceMap;
                    case "tooltip": return targetMap;
                    default: throw new InvalidOperationException("Unexpected format category: " + m.Args[0]);
                }
            });
            var listener = Activator.CreateInstance(listenerType, registry, maps);
            var refresh = listenerType.GetMethod("RefreshThemeColors");
            foreach (var color in new[] { Brushes.Red, Brushes.Green }) {
                source = source.SetForegroundBrush(color);
                refresh.Invoke(listener, null);
                if (target.ForegroundBrush != source.ForegroundBrush) throw new Exception("Tooltip syntax foreground did not follow the editor map");
                if (target.Typeface != original.Typeface || target.FontRenderingEmSize != original.FontRenderingEmSize ||
                    target.BackgroundBrush != original.BackgroundBrush || target.Bold != original.Bold)
                    throw new Exception("Theme synchronization changed tooltip font, size, background, or bold formatting");
                if (subscriptions != 1) throw new Exception("Theme change subscription was lost or duplicated");
            }
            if (writes != 2) throw new Exception("Missing classifications must be skipped");
        }
    }
}
