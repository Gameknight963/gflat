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
                try { return new ReturnMessage(request.MethodName == "GetType" ? interfaceType : call(request), null, 0, request.LogicalCallContext, request); }
                catch (Exception error) { return new ReturnMessage(error, request); }
            }
        }

        private static T Interface<T>(Func<IMethodCallMessage, object> call)
        { return (T)new Proxy(typeof(T), call).GetTransparentProxy(); }

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
            var type = Interface<IClassificationType>(m => m.MethodName == "get_Classification" ? "keyword" : null);
            var registry = Interface<IClassificationTypeRegistryService>(m =>
                m.MethodName == "GetClassificationType" && (string)m.Args[0] == "keyword" ? type : null);
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
