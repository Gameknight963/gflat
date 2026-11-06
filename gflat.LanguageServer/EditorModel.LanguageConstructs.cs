namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private object? LanguageConstructHover(string path, int offset, VisualStudioHover? presentation)
    {
        int index = TokenAt(path, offset);
        if (index < 0) return null;
        var token = tokens[path][index];
        var directive = MacroAt(path, offset) is { Classification: "preprocessor" } found ? found : null;
        if (directive == null && token.Kind == TokenKind.Identifier && token.Start > 0 && sources[path].Text[token.Start - 1] == '@') return null;
        var span = directive?.Span ?? token.Span;
        string word = sources[path].Text.Substring(span.Start, span.Length).Trim();
        string key = word.TrimStart('#');
        if (directive != null) key = "#" + key;
        else if (key == "static" && index + 1 < tokens[path].Length && tokens[path][index + 1].Text is "if" or "foreach")
            key += " " + tokens[path][index + 1].Text;
        else if (key == "default" && index + 1 < tokens[path].Length && tokens[path][index + 1].Kind == TokenKind.Colon)
            key = "default case";
        string? description = key switch {
            "struct" => "Declares a value type. Members are public by default.",
            "class" => "Declares a type with virtual dispatch and implementation inheritance. Members are private by default.",
            "interface" => "Declares a contract of methods and properties that implementing types provide.",
            "enum" => "Declares named values with an integer representation.",
            "namespace" => "Groups declarations under a qualified name.",
            "using" => "Makes a namespace's declarations available for unqualified lookup in this file.",
            "if" => "Executes a branch when its condition is true.",
            "else" => "Executes the alternative branch when the preceding condition is false.",
            "while" => "Repeats a body while its condition is true.",
            "for" => "Repeats a body with initialization, a condition, and an iteration step.",
            "foreach" => "Visits elements supplied by an enumerable's iterator.",
            "break" => "Leaves the enclosing loop or switch.",
            "continue" => "Starts the next iteration of the enclosing loop.",
            "return" => "Leaves the current function, returning its result when supplied.",
            "switch" => "Selects a branch by matching a value against case labels.",
            "case" => "Introduces a matching branch in a switch.",
            "default case" => "Introduces the switch branch used when no case matches.",
            "default" => "Produces the default value of the specified or contextually inferred type.",
            "static if" => "Selects a branch at compile time. The unselected branch is not instantiated.",
            "static foreach" => "Expands the body for each element of a compile-time list.",
            "static" => "Declares a member belonging to the type rather than an instance.",
            "new" => "Constructs an object using the selected allocation form.",
            "delete" => "Destroys an object and releases its allocation.",
            "defer" => "Runs an action when the enclosing scope exits, including during exception unwinding.",
            "try" => "Introduces a block whose exceptions can be handled by the following catch clauses.",
            "catch" => "Handles an exception leaving the preceding try block.",
            "throw" => "Raises an exception. A bare throw rethrows the currently handled exception.",
            "nothrow" => "Terminates if an exception escapes this function boundary.",
            "extern" => "Declares a function supplied by an external definition. Explicit C linkage uses the native symbol name.",
            "virtual" => "Enables dispatch to an implementation selected by the object's runtime type.",
            "override" => "Replaces an inherited virtual implementation.",
            "abstract" => "Declares a contract that a concrete derived type must implement.",
            "reinterpret" => "Reinterprets a value's representation without a numeric conversion.",
            "readonly" => "Protects this value's storage from modification through this view.",
            "immutable" => "Protects this value and storage reached through it from modification through this view.",
            "ref" => "Borrows a variable's storage as a parameter or return value.",
            "const" => "Declares a compile-time value or a function that can execute at compile time.",
            "move" => "Transfers a value and ends the source value's lifetime.",
            "copy" => "Declares the operation used to copy an object.",
            "alias" => "Introduces a name for a type.",
            "constrained" => "Limits an alias's exposed members to its declared contracts.",
            "weak" => "Provides a default function implementation that can be replaced.",
            "replace" => "Supplies the implementation for a matching replaceable function declaration.",
            "expect" => "Declares a contract whose implementation is supplied elsewhere.",
            "extension" => "Adds callable methods to an existing type within the declaration's visibility scope.",
            "#include" => "Processes another file at this position, sharing the current preprocessor state.",
            "#define" => "Defines a macro for subsequent preprocessing in this translation unit.",
            "#undef" => "Removes a macro definition from the current preprocessor state.",
            "#pragma" => "Applies a supported compiler directive. pragma once processes a header once per translation unit.",
            "#if" or "#elif" => "Includes this branch when its preprocessor condition is true and no preceding branch was selected.",
            "#ifdef" or "#elifdef" => "Selects this preprocessor branch when the named macro is defined and no preceding branch was selected.",
            "#ifndef" or "#elifndef" => "Selects this preprocessor branch when the named macro is undefined and no preceding branch was selected.",
            "#else" => "Includes the alternative preprocessor branch when no preceding branch was selected.",
            "#endif" => "Ends a conditional preprocessor group.",
            _ => null
        };
        if (description == null) return null;
        return new HoverResult(new("plaintext", word + "\n\n" + description), Workspace.ToRange(span),
            presentation?.Render([new(directive != null ? "preprocessor keyword" : "keyword", word)], [new("", description)], "keyword"));
    }
}
