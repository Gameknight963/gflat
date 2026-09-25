using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool EmitPropertyRead(AstNode node)
    {
        var call = _typeChecker.GetPropertyRead(node);
        if (call == null) return false;
        call.Accept(this);
        return true;
    }

    private bool EmitPropertyWrite(AstNode node)
    {
        var write = _typeChecker.GetPropertyWrite(node);
        if (write == null) return false;
        EmitExpressionWithCleanup(() =>
        {
            var temporaries = new List<string>();
            void Bind(IdentifierExpression temporary, string value)
            {
                string type = EmitType(_typeChecker.GetType(temporary));
                string slot = NewTemp();
                Emit($"    {slot} = alloca {type}");
                Emit($"    store {type} {value}, {type}* {slot}");
                _locals[temporary.Name] = slot;
                temporaries.Add(temporary.Name);
            }
            try
            {
                if (write.Receiver != null)
                {
                    if (write.ReceiverByAddress) EmitAddress(write.Receiver);
                    else write.Receiver.Accept(this);
                    Bind(write.ReceiverTemporary!, Pop());
                }
                foreach (var argument in write.Arguments)
                {
                    TypeExpression target = _typeChecker.GetType(argument.Temporary);
                    EmitValueForTarget(argument.Value, target);
                    Bind(argument.Temporary, EmitImplicitCast(Pop(), _typeChecker.GetType(argument.Value), target));
                }
                string? oldValue = null;
                if (write.Getter != null)
                {
                    write.Getter.Accept(this);
                    oldValue = Pop();
                    Bind(write.OldTemporary!, oldValue);
                }
                TypeExpression type = _typeChecker.GetType(write.ValueTemporary);
                EmitValueForTarget(write.Value, type);
                string assigned = EmitImplicitCast(Pop(), _typeChecker.GetType(write.Value), type);
                Bind(write.ValueTemporary, assigned);
                write.Setter.Accept(this);
                Push(write.Postfix ? oldValue! : assigned);
            }
            finally
            {
                foreach (string temporary in temporaries) _locals.Remove(temporary);
            }
        });
        return true;
    }
}
