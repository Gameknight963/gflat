using System.Text;
using System.Text.RegularExpressions;
using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private string EmitManagedAllocation(string bytes)
    {
        string slot = NewTemp(), first = NewTemp(), failed = NewTemp();
        string retry = NewLabel("gc_allocation_retry"), ready = NewLabel("gc_allocation_ready");
        Emit($"    {slot} = alloca i8*");
        Emit($"    {first} = call i8* @gflat$Memory$Allocate(i64 {bytes})");
        Emit($"    store i8* {first}, i8** {slot}");
        Emit($"    {failed} = icmp eq i8* {first}, null");
        Emit($"    br i1 {failed}, label %{retry}, label %{ready}");
        Emit(retry + ":");
        Emit("    call i64 @gflat$GC$Collect()");
        string second = NewTemp();
        Emit($"    {second} = call i8* @gflat$Memory$Allocate(i64 {bytes})");
        Emit($"    store i8* {second}, i8** {slot}");
        Emit($"    br label %{ready}");
        Emit(ready + ":");
        string result = NewTemp();
        Emit($"    {result} = load i8*, i8** {slot}");
        return result;
    }

    private string EmitManagedAdoption(NewExpression node, string address)
    {
        MethodDeclaration method = _typeChecker.GetManagedAdoption(node)!;
        string ns = _typeChecker.GetFunctionNamespace(method).Replace("::", "$");
        string name = "gflat$" + (ns.Length > 0 ? ns + "$" : "") + method.Name + _typeChecker.GetOverloadSuffix(method);
        string type = EmitType(method.Parameters[0].Type);
        string rawType = EmitType(node.Type) + "*";
        string argument = address;
        if (type != rawType)
        {
            argument = NewTemp();
            Emit($"    {argument} = bitcast {rawType} {address} to {type}");
        }
        string result = NewTemp();
        Emit($"    {result} = call {EmitType(method.ReturnType)} @{name}({type} {argument})");
        return result;
    }

    // This emitter already produces textual LLVM. Root lowering operates on its
    // completed functions so expression prologues and lambda bodies cannot bypass
    // registration. Slot addresses escape to the runtime, preventing LLVM from
    // removing roots even when the user value would otherwise stay in a register.
    private string LowerCollectorRoots(string ir)
    {
        if (!_typeChecker.UsesCollector) return ir;
        var result = new StringBuilder();
        string[] lines = ir.Replace("\r\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("define "))
            {
                result.AppendLine(lines[index]);
                continue;
            }
            var function = new List<string> { lines[index] };
            while (++index < lines.Length)
            {
                function.Add(lines[index]);
                if (lines[index] == "}") break;
            }
            result.Append(LowerCollectorFunction(function));
        }
        return result.ToString();
    }

    private static string ReadLlvmType(string text)
    {
        int depth = 0, end = 0;
        for (; end < text.Length; end++)
        {
            char c = text[end];
            if (c is '[' or '{' or '(') depth++;
            if (c is ']' or '}' or ')') depth--;
            if (c == ',' && depth == 0) break;
            if (c == ' ' && depth == 0)
            {
                // Function-pointer types have a space before the argument list.
                if (end + 1 < text.Length && text[end + 1] == '(') continue;
                break;
            }
        }
        return text[..end];
    }

    private string LowerCollectorFunction(List<string> lines)
    {
        if (Regex.IsMatch(lines[0], @"@gflat\$GC\$(EnterFrame|AddRoot|LeaveFrame|TrackNative|ForgetNative)\("))
            return string.Join("\n", lines) + "\n";
        var prologue = new StringBuilder("    %gc.frame = call i8* @gflat$GC$EnterFrame()\n");
        var body = new StringBuilder();
        var normalStores = new Dictionary<string, string>();
        int rootIndex = 0;
        bool allocationHook = lines[0].Contains("@gflat$Memory$Allocate(");
        bool freeHook = lines[0].Contains("@gflat$Memory$Free(");
        string firstParameter = Regex.Match(lines[0], @"\([^%]*?(%[\w$\.]+)\)").Groups[1].Value;
        void Root(string address, string type)
        {
            int id = rootIndex++;
            prologue.AppendLine($"    store {type} zeroinitializer, {type}* {address}");
            prologue.AppendLine($"    %gc.address.{id} = bitcast {type}* {address} to i8*");
            prologue.AppendLine($"    %gc.end.{id} = getelementptr {type}, {type}* null, i32 1");
            prologue.AppendLine($"    %gc.size.{id} = ptrtoint {type}* %gc.end.{id} to i64");
            prologue.AppendLine($"    call void @gflat$GC$AddRoot(i8* %gc.frame, i8* %gc.address.{id}, i64 %gc.size.{id})");
        }
        for (int index = 1; index < lines.Count - 1; index++)
        {
            string line = lines[index];
            if (line == "entry:") continue;
            var allocation = Regex.Match(line, @"^\s*(%[\w$\.]+) = alloca (.+)$");
            if (allocation.Success)
            {
                string type = ReadLlvmType(allocation.Groups[2].Value);
                prologue.AppendLine(line);
                Root(allocation.Groups[1].Value, type);
                continue;
            }
            if (line.TrimStart().StartsWith("ret "))
            {
                if (allocationHook)
                {
                    string value = line.Trim()["ret i8* ".Length..];
                    int id = rootIndex++;
                    body.AppendLine($"    %gc.track.{id} = call i1 @gflat$GC$TrackNative(i8* {value}, i64 {firstParameter})");
                    body.AppendLine($"    br i1 %gc.track.{id}, label %gc.track.ok.{id}, label %gc.track.fail.{id}");
                    body.AppendLine($"gc.track.fail.{id}:");
                    body.AppendLine($"    call void @gflat$Memory$Free(i8* {value})");
                    body.AppendLine("    call void @gflat$GC$LeaveFrame(i8* %gc.frame)");
                    body.AppendLine("    ret i8* null");
                    body.AppendLine($"gc.track.ok.{id}:");
                }
                body.AppendLine("    call void @gflat$GC$LeaveFrame(i8* %gc.frame)");
            }
            // A direct rethrow here is the emitter's final propagation path,
            // after language cleanup. Local catches use invoke and retain roots.
            if (line.Contains("call void @__gflat_eh_rethrow("))
                body.AppendLine("    call void @gflat$GC$LeaveFrame(i8* %gc.frame)");
            body.AppendLine(line);
            if (normalStores.TryGetValue(line.TrimEnd(':'), out string? pending)) body.AppendLine(pending);
            var valueDefinition = Regex.Match(line, @"^\s*(%[\w$\.]+) = (load|call|invoke|insertvalue) (.+)$");
            if (!valueDefinition.Success) continue;
            string valueType = ReadLlvmType(valueDefinition.Groups[3].Value);
            // Vararg calls print the function prototype after the result type.
            // It is not a function-pointer return unless followed by '*'.
            if (valueDefinition.Groups[2].Value is "call" or "invoke" && valueType.EndsWith(')'))
                valueType = valueType[..valueType.IndexOf(" (", StringComparison.Ordinal)];
            if (!valueType.Contains('*') && !valueType.StartsWith('%') && !valueType.StartsWith('[') && !valueType.StartsWith('{')) continue;
            string slot = "%gc.value." + rootIndex;
            prologue.AppendLine($"    {slot} = alloca {valueType}");
            Root(slot, valueType);
            string store = $"    store {valueType} {valueDefinition.Groups[1].Value}, {valueType}* {slot}";
            if (valueDefinition.Groups[2].Value == "invoke")
            {
                string label = Regex.Match(line, @"to label %([\w$\.]+)").Groups[1].Value;
                normalStores[label] = store;
            }
            else body.AppendLine(store);
        }
        if (freeHook) prologue.AppendLine($"    call void @gflat$GC$ForgetNative(i8* {firstParameter})");
        return lines[0] + "\nentry:\n" + prologue + body + "}\n";
    }
}
