using System.Text;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Preprocessing;

public sealed record MacroToken(string Text, string Classification, SourceSpan Span = default);
public sealed record MacroExpansionStep(string Name, string Before, string After, SourceSpan Declaration);

public sealed record MacroSymbol(string Name, SourceSpan Span, bool Definition, SourceSpan Declaration, string Signature,
    string? Expansion = null, string Classification = "macro", IReadOnlyList<MacroToken>? DeclarationTokens = null,
    IReadOnlyList<MacroToken>? ExpansionTokens = null, SourceSpan Invocation = default, string? FullExpansion = null,
    IReadOnlyList<MacroExpansionStep>? Steps = null);

public sealed record PreprocessingResult(List<Token> Tokens, IReadOnlyList<SourceFile> Sources,
    IReadOnlyList<string> Dependencies, IReadOnlyList<MacroSymbol> Symbols);

public sealed partial class Preprocessor
{
    private sealed record Piece(string Text, SourceSpan Span, bool Space = false, bool Verbatim = false,
        HashSet<string>? Hidden = null, bool Generated = false, bool JoinLeft = false, bool LiteralFragment = false,
        int[]? Occurrences = null);
    private sealed record Macro(string Name, string[]? Parameters, bool Variadic, List<Piece> Body, SourceSpan Span,
        IReadOnlyList<MacroToken>? DeclarationTokens = null);
    private readonly CompilationOptions options;
    private readonly DiagnosticBag diagnostics;
    private readonly CancellationToken cancellation;
    private readonly Dictionary<string, Macro> macros = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceFile> files = new(Paths);
    private readonly HashSet<string> dependencies = new(Paths);
    private readonly HashSet<string> once = new(Paths);
    private readonly List<MacroSymbol> symbols = [];
    public IReadOnlyList<SourceFile> Sources => files.Values.ToArray();
    public IReadOnlyList<string> Dependencies => dependencies.ToArray();
    public IReadOnlyList<MacroSymbol> Symbols => symbols;
    private int expansions;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public Preprocessor(CompilationOptions? options = null, DiagnosticBag? diagnostics = null, CancellationToken cancellationToken = default)
    {
        this.options = options ?? new();
        this.diagnostics = diagnostics ?? new();
        cancellation = cancellationToken;
    }

    public static bool IsNeeded(SourceFile source, CompilationOptions? options = null) =>
        options?.Defines.Count > 0 || source.Text.Contains('#') || source.Text.Contains("@(") ||
        source.Text.Contains("__LINE__", StringComparison.Ordinal) || source.Text.Contains("__FILE__", StringComparison.Ordinal) ||
        source.Text.Contains("\\\n") || source.Text.Contains("\\\r");

    public PreprocessingResult Process(SourceFile source)
    {
        macros.Clear(); files.Clear(); dependencies.Clear(); once.Clear(); symbols.Clear(); expansions = 0;
        foreach (var definition in options.Defines)
        {
            if (!Name(definition.Key)) throw Error($"Invalid macro name '{definition.Key}'", source.Span(0));
            var commandLine = new SourceFile("<command-line>", definition.Key + " " + definition.Value);
            var definitionTokens = Scan(commandLine);
            if (definitionTokens.Any(piece => piece.Text == "\n")) throw Error("A command-line definition must fit on one logical line", source.Span(0));
            Define(definitionTokens, commandLine.Span(0));
        }
        var pieces = Read(source, 0);
        return new(Convert(pieces, source), files.Values.ToArray(), dependencies.ToArray(), symbols.ToArray());
    }

    private static string Key(string path) => path.StartsWith('<') ? path : Path.GetFullPath(path);
    private static bool Name(string text) => text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') &&
        text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static TypeCheckException Error(string message, SourceSpan span) => new("Preprocessor: " + message, span);

    private static string Render(IEnumerable<Piece> pieces, Action<int, int, Piece>? range = null)
    {
        var text = new StringBuilder();
        Piece? previous = null;
        foreach (var piece in pieces)
        {
            bool literal = piece.Text.StartsWith('"') || piece.Text.StartsWith("$\"") || piece.Text == "$";
            bool adjacentLiteral = !piece.Space && previous != null &&
                (literal && Name(previous.Text) || piece.Text.StartsWith('"') && previous.Text == "$");
            if (text.Length != 0 && !piece.JoinLeft && !adjacentLiteral) text.Append(' ');
            int start = text.Length;
            if (piece.Verbatim) text.Append('@');
            text.Append(piece.Text);
            range?.Invoke(start, text.Length, piece);
            previous = piece;
        }
        return text.ToString();
    }

    private List<Token> Convert(List<Piece> pieces, SourceFile root)
    {
        var ranges = new List<(int Start, int End, Piece Piece)>();
        string rendered = Render(pieces, (start, end, piece) => ranges.Add((start, end, piece)));
        var expanded = new SourceFile(root.Path, rendered);
        List<Token> lexed;
        var errors = new DiagnosticBag();
        lexed = Lexer.Tokenize(expanded, errors);
        Piece? At(int position) => ranges.FirstOrDefault(range => position >= range.Start && position < range.End).Piece;
        foreach (var error in errors.Items)
            diagnostics.Report(error.Descriptor, At(error.Span.Start)?.Span ?? root.Span(root.Text.Length), error.Message);
        var result = new List<Token>(lexed.Count);
        int rangeIndex = 0;
        foreach (var token in lexed)
        {
            if (token.Kind == TokenKind.EndOfFile)
            {
                var end = root.Span(root.Text.Length);
                result.Add(new Token(TokenKind.EndOfFile, end.Line, end.Start, end.Start, column: end.Column) { Source = root });
                break;
            }
            while (rangeIndex + 1 < ranges.Count && ranges[rangeIndex].End <= token.Start) rangeIndex++;
            var range = ranges[rangeIndex];
            var origin = range.Piece.Span;
            if (!range.Piece.Generated && !range.Piece.Verbatim && origin.Source != null &&
                range.Piece.Text.Length == origin.Length)
                origin = origin.Source.Span(origin.Start + token.Start - range.Start, token.End - token.Start + 1);
            result.Add(new Token(token.Kind, origin.Line, origin.Start, origin.Start + Math.Max(1, origin.Length) - 1,
                token.Text, origin.Column) { Source = origin.Source ?? root, ExpandedStart = token.Start, ExpandedEnd = token.End,
                    IsEscapedIdentifier = token.IsEscapedIdentifier });
        }
        return result;
    }
}
