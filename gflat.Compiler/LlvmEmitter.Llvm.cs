using System.Text;
using System.Text.RegularExpressions;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class LlvmEmitter
{
    private int llvmHelperId;
    private readonly StringBuilder llvmFunctions = new();
    private readonly Dictionary<string, string> llvmExternalDeclarations = new();

    private bool TryEmitLlvmCall(CallExpression node)
    {
        if (_typeChecker.GetLlvmCall(node) is not { } info) return false;
        string prefix = "gflat$llvm$" + llvmHelperId++ + "$";
        string name = prefix + "body";
        string resultType = EmitType(info.Result);
        string[] types = info.Arguments.Select(EmitType).ToArray();
        bool doesNotUnwind = false;
        if (info.Entry == null)
        {
            RegisterLlvmRuntimeReferences(MaskLlvmCode(info.Ir, node, requireUnquotedSymbols: false));
            llvmFunctions.AppendLine($"define private {resultType} @{name}({string.Join(", ", types.Select((type, index) => type + " %" + index))}) alwaysinline{NoBuiltinsAttribute} {{");
            llvmFunctions.AppendLine(info.Ir);
            llvmFunctions.AppendLine("}");
        }
        else
        {
            name = prefix + info.Entry;
            llvmFunctions.AppendLine(PrepareLlvmModule(info.Ir, info.Entry, prefix, resultType, types, node, out doesNotUnwind));
        }
        var values = new List<string>();
        foreach (var expression in node.Arguments.Skip(info.SourceArguments))
        {
            expression.Accept(this);
            values.Add(Pop());
        }
        string arguments = string.Join(", ", types.Zip(values, (type, value) => type + " " + value));
        if (resultType == "void") EmitCallInstruction("void", "@" + name, arguments, handleUnwind: !doesNotUnwind);
        else
        {
            string value = NewTemp();
            EmitCallInstruction(resultType, "@" + name, arguments, value, handleUnwind: !doesNotUnwind);
            Push(value);
        }
        return true;
    }

    private static string MaskLlvmCode(string ir, AstNode node, bool requireUnquotedSymbols)
    {
        // Mask string data and comments without changing offsets. Rewriting only
        // symbol tokens keeps embedded strings and comments byte-for-byte intact.
        char[] mask = ir.ToCharArray();
        for (int at = 0; at < ir.Length; at++)
        {
            if (ir[at] == ';')
                for (; at < ir.Length && ir[at] != '\n'; at++) mask[at] = ' ';
            else if (ir[at] == '"')
            {
                bool symbol = at > 0 && ir[at - 1] is '@' or '%';
                if (symbol && requireUnquotedSymbols)
                    throw new TypeCheckException("LLVM module symbols must use unquoted names", node.Span);
                if (!symbol) mask[at] = ' ';
                for (at++; at < ir.Length; at++)
                {
                    if (!symbol) mask[at] = ' ';
                    if (ir[at] == '\\' && at + 1 < ir.Length) { at++; if (!symbol) mask[at] = ' '; continue; }
                    if (ir[at] == '"') break;
                }
            }
        }
        return new(mask);
    }

    private void RegisterLlvmRuntimeReferences(string code)
    {
        // Only the LLVM escape hatch needs symbol discovery. Normal code generation
        // registers dependencies through EmitRuntimeCall, before it becomes text.
        foreach (Match symbol in Regex.Matches(code, "@(?:\"(__gflat_eh_[A-Za-z0-9_]+)\"|(__gflat_eh_[A-Za-z0-9_]+))"))
        {
            string name = symbol.Groups[1].Success ? symbol.Groups[1].Value : symbol.Groups[2].Value;
            if (ExceptionRuntime.Functions.FirstOrDefault(f => f.Name == name) is { } function)
                _runtimeFunctions.Add(function);
        }
    }

    private string PrepareLlvmModule(string ir, string entry, string prefix, string resultType, string[] argumentTypes, AstNode node, out bool doesNotUnwind)
    {
        string code = MaskLlvmCode(ir, node, requireUnquotedSymbols: true);
        if (Regex.IsMatch(code, @"(?m)^\s*(target\s|source_filename\s|attributes\s|!|\$)"))
            throw new TypeCheckException("LLVM modules cannot include target directives, attribute groups, metadata or comdat declarations; use inline function attributes", node.Span);
        const string symbol = @"[A-Za-z_$.-][A-Za-z0-9_$.-]*";
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match definition in Regex.Matches(code, @"(?m)^\s*define\b[^\r\n@]*@(" + symbol + @")\s*\("))
            names.Add("@" + definition.Groups[1].Value);
        if (!names.Contains("@" + entry)) throw new TypeCheckException($"LLVM module must define entry function '{entry}'", node.Span);
        var header = Regex.Match(code, @"(?m)^\s*define\s+(?:(?:private|internal|external|dso_local)\s+)*(" + Regex.Escape(resultType) + @")\s+@" + Regex.Escape(entry) + @"\s*\(");
        if (!header.Success) throw new TypeCheckException("LLVM entry result type must match the explicit gflat result type and use the default calling convention", node.Span);
        int parameterStart = header.Index + header.Length;
        int depth = 1;
        int end = parameterStart;
        for (; end < code.Length && depth > 0; end++)
        {
            if (code[end] == '(') depth++;
            if (code[end] == ')') depth--;
        }
        if (depth != 0) throw new TypeCheckException("LLVM entry has an incomplete parameter list", node.Span);
        // Only an explicit attribute on this entry can suppress unwind handling.
        // Masked strings/comments, LLVM symbols, and metadata keys cannot supply attributes.
        int bodyStart = code.IndexOf('{', end);
        doesNotUnwind = bodyStart >= 0 && Regex.IsMatch(code[end..bodyStart],
            @"(?<![@%!A-Za-z0-9_$.-])nounwind(?![A-Za-z0-9_$.-])");
        var parameterText = code[parameterStart..(end - 1)].Trim();
        var parameters = new List<string>();
        int nested = 0, start = 0;
        for (int at = 0; at < parameterText.Length; at++)
        {
            if (parameterText[at] is '(' or '[' or '{') nested++;
            if (parameterText[at] is ')' or ']' or '}') nested--;
            if (parameterText[at] == ',' && nested == 0) { parameters.Add(parameterText[start..at].Trim()); start = at + 1; }
        }
        if (parameterText.Length > 0) parameters.Add(parameterText[start..].Trim());
        if (parameters.Count != argumentTypes.Length || parameters.Where((parameter, index) =>
            !Regex.IsMatch(parameter, @"\A" + Regex.Escape(argumentTypes[index]) + @"(?:\s+%(?:" + symbol + @"|[0-9]+))?\s*\z")).Any())
            throw new TypeCheckException("LLVM entry parameter types must match the gflat argument types exactly; variadics and ABI parameter attributes are unsupported", node.Span);
        foreach (Match definition in Regex.Matches(code, @"(?m)^\s*([@%]" + symbol + @")\s*="))
            names.Add(definition.Groups[1].Value);
        var rewritten = new StringBuilder();
        int cursor = 0;
        foreach (Match token in Regex.Matches(code, @"[@%]" + symbol))
        {
            rewritten.Append(ir, cursor, token.Index - cursor);
            rewritten.Append(names.Contains(token.Value) ? token.Value[0] + prefix + token.Value[1..] : token.Value);
            cursor = token.Index + token.Length;
        }
        rewritten.Append(ir, cursor, ir.Length - cursor);
        RegisterLlvmRuntimeReferences(MaskLlvmCode(rewritten.ToString(), node, requireUnquotedSymbols: true));
        var output = new StringBuilder();
        foreach (string line in rewritten.ToString().Split('\n'))
        {
            if (Regex.IsMatch(line, @"^\s*declare\b"))
            {
                var declaration = Regex.Match(line, @"@(" + symbol + @")\s*\(");
                if (!declaration.Success) throw new TypeCheckException("LLVM external declarations must use a single line and unquoted symbol", node.Span);
                string declarationText = line.Trim();
                string external = declaration.Groups[1].Value;
                if (llvmExternalDeclarations.TryGetValue(external, out string? previous))
                {
                    if (previous != declarationText) throw new TypeCheckException($"Conflicting LLVM declarations for '{external}'", node.Span);
                    continue;
                }
                llvmExternalDeclarations[external] = declarationText;
            }
            output.AppendLine(line);
        }
        return output.ToString();
    }
}
