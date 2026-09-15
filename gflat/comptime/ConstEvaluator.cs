using System;
using System.Collections.Generic;
using gflat.ast;

namespace gflat.comptime
{
    public class ConstEvalException : Exception
    {
        public int Line { get; }

        public ConstEvalException(string message, int line) : base(message)
        {
            Line = line;
        }
    }

    public class ConstEvaluator : IVisitor
    {
        private const int MaxSteps = 100000;
        private const int MaxCallDepth = 256;

        private readonly TypeChecker _typeChecker;
        private readonly Stack<Dictionary<string, ConstValue>> _scopes = new();
        private int _stepCount = 0;
        private int _callDepth = 0;

        private ConstValue? _currentValue = null;
        private bool _hasReturned = false;
        private ConstValue? _returnValue = null;
        private bool _hasBroken = false;
        private bool _hasContinued = false;

        public ConstEvaluator(TypeChecker typeChecker)
        {
            _typeChecker = typeChecker;
        }

        public bool TryEvaluate(AstNode node, out ConstValue? result, out string? failureReason)
        {
            result = null;
            failureReason = null;
            _stepCount = 0;
            _callDepth = 0;
            _hasReturned = false;
            _returnValue = null;
            _hasBroken = false;
            _hasContinued = false;
            _currentValue = null;
            _scopes.Clear();
            _scopes.Push(new Dictionary<string, ConstValue>());

            try
            {
                node.Accept(this);
                result = _currentValue;
                return result != null;
            }
            catch (ConstEvalException ex)
            {
                failureReason = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }

        private void CheckSteps(int line)
        {
            _stepCount++;
            if (_stepCount > MaxSteps)
            {
                throw new ConstEvalException($"Compile-time evaluation exceeded maximum step limit of {MaxSteps}", line);
            }
        }

        private void PushScope()
        {
            _scopes.Push(new Dictionary<string, ConstValue>());
        }

        private void PopScope()
        {
            _scopes.Pop();
        }

        private void SetVariable(string name, ConstValue value)
        {
            foreach (Dictionary<string, ConstValue> scope in _scopes)
            {
                if (scope.ContainsKey(name))
                {
                    scope[name] = value;
                    return;
                }
            }
            _scopes.Peek()[name] = value;
        }

        private bool TryGetVariable(string name, out ConstValue? value)
        {
            foreach (Dictionary<string, ConstValue> scope in _scopes)
            {
                if (scope.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            if (_typeChecker.TryGetConstValueByName(name, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        public void Visit(LiteralExpression node)
        {
            CheckSteps(node.Line);
            _currentValue = node.Token.Kind switch
            {
                TokenKind.IntLiteral => new ConstValue.Integer(Convert.ToInt64(node.Token.Text)),
                TokenKind.UIntLiteral => new ConstValue.UInteger(Convert.ToUInt64(node.Token.Text.TrimEnd('u', 'U'))),
                TokenKind.HexInt => ParseHexInt(node.Token.Text),
                TokenKind.FloatLiteral => new ConstValue.Float(Convert.ToDouble(node.Token.Text.TrimEnd('f', 'F'), System.Globalization.CultureInfo.InvariantCulture)),
                TokenKind.DoubleLiteral => new ConstValue.Float(Convert.ToDouble(node.Token.Text, System.Globalization.CultureInfo.InvariantCulture)),
                TokenKind.LongLiteral => new ConstValue.Integer(Convert.ToInt64(node.Token.Text.TrimEnd('l', 'L'))),
                TokenKind.ULongLiteral => new ConstValue.UInteger(Convert.ToUInt64(node.Token.Text.TrimEnd('u', 'U', 'l', 'L'))),
                TokenKind.CharLiteral => new ConstValue.Char(ParseChar(node.Token.Text)),
                TokenKind.StringLiteral => new ConstValue.String(node.Token.Text[1..^1]),
                TokenKind.True => new ConstValue.Boolean(true),
                TokenKind.False => new ConstValue.Boolean(false),
                _ => throw new ConstEvalException($"Unsupported compile-time literal '{node.Token.Kind}'", node.Line)
            };
        }

        private static ConstValue ParseHexInt(string text)
        {
            string clean = text[2..];
            if (text.EndsWith("ul", StringComparison.OrdinalIgnoreCase) || text.EndsWith("lu", StringComparison.OrdinalIgnoreCase))
            {
                return new ConstValue.UInteger(Convert.ToUInt64(clean[..^2], 16));
            }
            if (text.EndsWith("u", StringComparison.OrdinalIgnoreCase))
            {
                return new ConstValue.UInteger(Convert.ToUInt64(clean[..^1], 16));
            }
            if (text.EndsWith("l", StringComparison.OrdinalIgnoreCase))
            {
                return new ConstValue.Integer(Convert.ToInt64(clean[..^1], 16));
            }
            return new ConstValue.Integer(Convert.ToInt64(clean, 16));
        }

        private static char ParseChar(string text)
        {
            if (text.Length >= 3 && text[0] == '\'' && text[^1] == '\'')
            {
                string inner = text[1..^1];
                if (inner.Length == 1) return inner[0];
                if (inner == "\\n") return '\n';
                if (inner == "\\r") return '\r';
                if (inner == "\\t") return '\t';
                if (inner == "\\0") return '\0';
                if (inner == "\\\\") return '\\';
                if (inner == "\\'") return '\'';
            }
            return text[1];
        }

        public void Visit(IdentifierExpression node)
        {
            CheckSteps(node.Line);
            if (!TryGetVariable(node.Name, out ConstValue? val) || val == null)
            {
                if (TryGetVariable("this", out ConstValue? thisVal))
                {
                    if (thisVal is ConstValue.Struct thisStruct && thisStruct.Fields.TryGetValue(node.Name, out ConstValue? sVal))
                    {
                        _currentValue = sVal;
                        return;
                    }
                    if (thisVal is ConstValue.ClassInstance thisClass && thisClass.Fields.TryGetValue(node.Name, out ConstValue? cVal))
                    {
                        _currentValue = cVal;
                        return;
                    }
                }
                throw new ConstEvalException($"Variable '{node.Name}' is not a compile-time constant", node.Line);
            }
            _currentValue = val;
        }

        public void Visit(BinaryExpression node)
        {
            CheckSteps(node.Line);

            // Short-circuit logical AND
            if (node.Operator == TokenKind.AmpersandAmpersand)
            {
                node.Left.Accept(this);
                if (_currentValue is not ConstValue.Boolean leftBool)
                {
                    throw new ConstEvalException("'&&' requires boolean operands", node.Line);
                }
                if (!leftBool.Value)
                {
                    _currentValue = new ConstValue.Boolean(false);
                    return;
                }
                node.Right.Accept(this);
                if (_currentValue is not ConstValue.Boolean rightBool)
                {
                    throw new ConstEvalException("'&&' requires boolean operands", node.Line);
                }
                _currentValue = new ConstValue.Boolean(rightBool.Value);
                return;
            }

            // Short-circuit logical OR
            if (node.Operator == TokenKind.PipePipe)
            {
                node.Left.Accept(this);
                if (_currentValue is not ConstValue.Boolean leftBool)
                {
                    throw new ConstEvalException("'||' requires boolean operands", node.Line);
                }
                if (leftBool.Value)
                {
                    _currentValue = new ConstValue.Boolean(true);
                    return;
                }
                node.Right.Accept(this);
                if (_currentValue is not ConstValue.Boolean rightBool)
                {
                    throw new ConstEvalException("'||' requires boolean operands", node.Line);
                }
                _currentValue = new ConstValue.Boolean(rightBool.Value);
                return;
            }

            node.Left.Accept(this);
            ConstValue left = _currentValue!;

            node.Right.Accept(this);
            ConstValue right = _currentValue!;

            switch (node.Operator)
            {
                case TokenKind.Plus:
                    if (ConstValue.TryAdd(left, right, out ConstValue? addRes))
                    {
                        _currentValue = addRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot add '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.Minus:
                    if (ConstValue.TrySubtract(left, right, out ConstValue? subRes))
                    {
                        _currentValue = subRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot subtract '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.Star:
                    if (ConstValue.TryMultiply(left, right, out ConstValue? mulRes))
                    {
                        _currentValue = mulRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot multiply '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.Slash:
                    if (ConstValue.TryDivide(left, right, out ConstValue? divRes, out string? divErr))
                    {
                        _currentValue = divRes;
                        return;
                    }
                    throw new ConstEvalException(divErr ?? $"Cannot divide '{left}' by '{right}' at compile time", node.Line);

                case TokenKind.Percent:
                    if (ConstValue.TryModulo(left, right, out ConstValue? modRes, out string? modErr))
                    {
                        _currentValue = modRes;
                        return;
                    }
                    throw new ConstEvalException(modErr ?? $"Cannot modulo '{left}' by '{right}' at compile time", node.Line);

                case TokenKind.EqualsEquals:
                    if (ConstValue.TryEqual(left, right, out bool eq))
                    {
                        _currentValue = new ConstValue.Boolean(eq);
                        return;
                    }
                    _currentValue = new ConstValue.Boolean(false);
                    return;

                case TokenKind.NotEquals:
                    if (ConstValue.TryEqual(left, right, out bool neq))
                    {
                        _currentValue = new ConstValue.Boolean(!neq);
                        return;
                    }
                    _currentValue = new ConstValue.Boolean(true);
                    return;

                case TokenKind.Less:
                    if (ConstValue.TryCompare(left, right, out int cmpLt))
                    {
                        _currentValue = new ConstValue.Boolean(cmpLt < 0);
                        return;
                    }
                    throw new ConstEvalException($"Cannot compare '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.LessEquals:
                    if (ConstValue.TryCompare(left, right, out int cmpLte))
                    {
                        _currentValue = new ConstValue.Boolean(cmpLte <= 0);
                        return;
                    }
                    throw new ConstEvalException($"Cannot compare '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.Greater:
                    if (ConstValue.TryCompare(left, right, out int cmpGt))
                    {
                        _currentValue = new ConstValue.Boolean(cmpGt > 0);
                        return;
                    }
                    throw new ConstEvalException($"Cannot compare '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.GreaterEquals:
                    if (ConstValue.TryCompare(left, right, out int cmpGte))
                    {
                        _currentValue = new ConstValue.Boolean(cmpGte >= 0);
                        return;
                    }
                    throw new ConstEvalException($"Cannot compare '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.Ampersand:
                    if (ConstValue.TryBitwiseAnd(left, right, out ConstValue? bandRes))
                    {
                        _currentValue = bandRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot bitwise AND '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.Pipe:
                    if (ConstValue.TryBitwiseOr(left, right, out ConstValue? borRes))
                    {
                        _currentValue = borRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot bitwise OR '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.Caret:
                    if (ConstValue.TryBitwiseXor(left, right, out ConstValue? bxorRes))
                    {
                        _currentValue = bxorRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot bitwise XOR '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.LessLess:
                    if (ConstValue.TryShiftLeft(left, right, out ConstValue? shlRes))
                    {
                        _currentValue = shlRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot shift left '{left}' and '{right}' at compile time", node.Line);

                case TokenKind.GreaterGreater:
                    if (ConstValue.TryShiftRight(left, right, out ConstValue? shrRes))
                    {
                        _currentValue = shrRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot shift right '{left}' and '{right}' at compile time", node.Line);

                default:
                    throw new ConstEvalException($"Binary operator '{node.Operator}' not supported at compile time", node.Line);
            }
        }

        public void Visit(UnaryExpression node)
        {
            CheckSteps(node.Line);

            if (node.Operator == TokenKind.PlusPlus)
            {
                if (node.Operand is IdentifierExpression ppIdent && TryGetVariable(ppIdent.Name, out ConstValue? ppVal))
                {
                    ConstValue one = ppVal is ConstValue.UInteger ? new ConstValue.UInteger(1) : new ConstValue.Integer(1);
                    if (ConstValue.TryAdd(ppVal!, one, out ConstValue? nextVal))
                    {
                        SetVariable(ppIdent.Name, nextVal!);
                        _currentValue = node.IsPrefix ? nextVal : ppVal;
                        return;
                    }
                }
                else if (node.Operand is MemberAccessExpression ppMem)
                {
                    ppMem.Object.Accept(this);
                    if (_currentValue is ConstValue.Struct ppStruct && ppStruct.Fields.TryGetValue(ppMem.Member, out ConstValue? memVal))
                    {
                        ConstValue one = memVal is ConstValue.UInteger ? new ConstValue.UInteger(1) : new ConstValue.Integer(1);
                        if (ConstValue.TryAdd(memVal, one, out ConstValue? nextVal))
                        {
                            ppStruct.Fields[ppMem.Member] = nextVal!;
                            _currentValue = node.IsPrefix ? nextVal : memVal;
                            return;
                        }
                    }
                }
                throw new ConstEvalException($"Cannot apply '++' to '{node.Operand}' at compile time", node.Line);
            }

            if (node.Operator == TokenKind.MinusMinus)
            {
                if (node.Operand is IdentifierExpression mmIdent && TryGetVariable(mmIdent.Name, out ConstValue? mmVal))
                {
                    ConstValue one = mmVal is ConstValue.UInteger ? new ConstValue.UInteger(1) : new ConstValue.Integer(1);
                    if (ConstValue.TrySubtract(mmVal!, one, out ConstValue? nextVal))
                    {
                        SetVariable(mmIdent.Name, nextVal!);
                        _currentValue = node.IsPrefix ? nextVal : mmVal;
                        return;
                    }
                }
                else if (node.Operand is MemberAccessExpression mmMem)
                {
                    mmMem.Object.Accept(this);
                    if (_currentValue is ConstValue.Struct mmStruct && mmStruct.Fields.TryGetValue(mmMem.Member, out ConstValue? memVal))
                    {
                        ConstValue one = memVal is ConstValue.UInteger ? new ConstValue.UInteger(1) : new ConstValue.Integer(1);
                        if (ConstValue.TrySubtract(memVal, one, out ConstValue? nextVal))
                        {
                            mmStruct.Fields[mmMem.Member] = nextVal!;
                            _currentValue = node.IsPrefix ? nextVal : memVal;
                            return;
                        }
                    }
                }
                throw new ConstEvalException($"Cannot apply '--' to '{node.Operand}' at compile time", node.Line);
            }

            node.Operand.Accept(this);
            ConstValue operand = _currentValue!;

            switch (node.Operator)
            {
                case TokenKind.Ampersand:
                    if (operand is ConstValue.Struct s)
                    {
                        string? gName = node.Operand is IdentifierExpression id ? id.Name : null;
                        _currentValue = new ConstValue.Pointer(s.StructName, s, gName);
                        return;
                    }
                    if (operand is ConstValue.ClassInstance c)
                    {
                        string? gName = node.Operand is IdentifierExpression id ? id.Name : null;
                        _currentValue = new ConstValue.Pointer(c.ClassName, c, gName);
                        return;
                    }
                    throw new ConstEvalException($"Cannot take address of non-struct/class '{operand}' at compile time", node.Line);

                case TokenKind.Star:
                    if (operand is ConstValue.Pointer ptr)
                    {
                        _currentValue = ptr.Target;
                        return;
                    }
                    throw new ConstEvalException($"Cannot dereference non-pointer '{operand}' at compile time", node.Line);

                case TokenKind.Minus:
                    if (ConstValue.TryNegate(operand, out ConstValue? negRes))
                    {
                        _currentValue = negRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot negate '{operand}' at compile time", node.Line);

                case TokenKind.Bang:
                    if (ConstValue.TryLogicalNot(operand, out ConstValue? notRes))
                    {
                        _currentValue = notRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot apply '!' to '{operand}' at compile time", node.Line);

                case TokenKind.Tilde:
                    if (ConstValue.TryBitwiseNot(operand, out ConstValue? bnotRes))
                    {
                        _currentValue = bnotRes;
                        return;
                    }
                    throw new ConstEvalException($"Cannot apply '~' to '{operand}' at compile time", node.Line);

                default:
                    throw new ConstEvalException($"Unary operator '{node.Operator}' not supported at compile time", node.Line);
            }
        }

        public void Visit(MemberAccessExpression node)
        {
            CheckSteps(node.Line);
            node.Object.Accept(this);
            ConstValue obj = _currentValue!;

            if (obj is ConstValue.Pointer ptrVal)
            {
                obj = ptrVal.Target;
            }

            if (obj is ConstValue.String strVal)
            {
                if (node.Member == "length")
                {
                    _currentValue = new ConstValue.Integer(strVal.Value.Length);
                    return;
                }
                throw new ConstEvalException($"Unknown member '{node.Member}' on string at compile time", node.Line);
            }

            if (obj is ConstValue.Array arrVal)
            {
                if (node.Member == "length")
                {
                    _currentValue = new ConstValue.Integer(arrVal.Elements.Count);
                    return;
                }
                throw new ConstEvalException($"Unknown member '{node.Member}' on array at compile time", node.Line);
            }

            if (obj is ConstValue.Struct structVal)
            {
                if (structVal.Fields.TryGetValue(node.Member, out ConstValue? fieldVal))
                {
                    _currentValue = fieldVal;
                    return;
                }
                throw new ConstEvalException($"Struct '{structVal.StructName}' has no field '{node.Member}'", node.Line);
            }

            if (obj is ConstValue.ClassInstance classVal)
            {
                if (classVal.Fields.TryGetValue(node.Member, out ConstValue? fieldVal))
                {
                    _currentValue = fieldVal;
                    return;
                }
                throw new ConstEvalException($"Class '{classVal.ClassName}' has no field '{node.Member}'", node.Line);
            }

            throw new ConstEvalException($"Cannot access member '{node.Member}' on non-struct/non-string at compile time", node.Line);
        }

        public void Visit(IndexExpression node)
        {
            CheckSteps(node.Line);
            node.Target.Accept(this);
            ConstValue target = _currentValue!;

            node.Index.Accept(this);
            ConstValue index = _currentValue!;

            long idx = index switch
            {
                ConstValue.Integer i => i.Value,
                ConstValue.UInteger u => (long)u.Value,
                _ => throw new ConstEvalException("Index must be an integer", node.Line)
            };

            if (target is ConstValue.String str)
            {
                if (idx < 0 || idx > str.Value.Length)
                {
                    throw new ConstEvalException($"String index {idx} out of bounds (length {str.Value.Length})", node.Line);
                }
                if (idx == str.Value.Length)
                {
                    _currentValue = new ConstValue.Char('\0');
                    return;
                }
                _currentValue = new ConstValue.Char(str.Value[(int)idx]);
                return;
            }

            if (target is ConstValue.Array arr)
            {
                if (idx < 0 || idx >= arr.Elements.Count)
                {
                    throw new ConstEvalException($"Array index {idx} out of bounds (length {arr.Elements.Count})", node.Line);
                }
                _currentValue = arr.Elements[(int)idx];
                return;
            }

            throw new ConstEvalException("Cannot index non-array/non-string at compile time", node.Line);
        }

        public void Visit(CastExpression node)
        {
            CheckSteps(node.Line);
            node.Operand.Accept(this);
            ConstValue val = _currentValue!;

            // Simple numeric casts
            if (val is ConstValue.Integer i)
            {
                if (node.TargetType is NamedTypeExpression { Name: "char" })
                {
                    _currentValue = new ConstValue.Char((char)i.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "uint" or "ulong" or "ushort" or "usize" })
                {
                    _currentValue = new ConstValue.UInteger((ulong)i.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "int" or "long" or "short" or "byte" or "sbyte" or "isize" })
                {
                    _currentValue = new ConstValue.Integer(i.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "float" or "double" })
                {
                    _currentValue = new ConstValue.Float(i.Value);
                    return;
                }
            }
            if (val is ConstValue.UInteger ui)
            {
                if (node.TargetType is NamedTypeExpression { Name: "char" })
                {
                    _currentValue = new ConstValue.Char((char)ui.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "int" or "long" or "short" or "byte" or "sbyte" or "isize" })
                {
                    _currentValue = new ConstValue.Integer((long)ui.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "uint" or "ulong" or "ushort" or "usize" })
                {
                    _currentValue = new ConstValue.UInteger(ui.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "float" or "double" })
                {
                    _currentValue = new ConstValue.Float(ui.Value);
                    return;
                }
            }
            if (val is ConstValue.Char c)
            {
                if (node.TargetType is NamedTypeExpression { Name: "int" or "long" or "short" or "byte" or "sbyte" or "isize" })
                {
                    _currentValue = new ConstValue.Integer((long)c.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "uint" or "ulong" or "ushort" or "usize" })
                {
                    _currentValue = new ConstValue.UInteger((ulong)c.Value);
                    return;
                }
            }
            if (val is ConstValue.Float f)
            {
                if (node.TargetType is NamedTypeExpression { Name: "int" or "long" or "short" or "byte" or "sbyte" or "isize" })
                {
                    _currentValue = new ConstValue.Integer((long)f.Value);
                    return;
                }
                if (node.TargetType is NamedTypeExpression { Name: "uint" or "ulong" or "ushort" or "usize" })
                {
                    _currentValue = new ConstValue.UInteger((ulong)f.Value);
                    return;
                }
            }
        }

        public void Visit(NewExpression node)
        {
            CheckSteps(node.Line);
            if (node.Kind == AllocationKind.Managed)
            {
                throw new ConstEvalException("Managed allocation ('new^') is not permitted at compile time", node.Line);
            }

            TypeExpression resolvedType = _typeChecker.ResolveAlias(node.Type);
            if (resolvedType is not NamedTypeExpression named)
            {
                throw new ConstEvalException("Cannot instantiate anonymous type at compile time", node.Line);
            }

            List<ConstValue> args = new();
            foreach (AstNode arg in node.Arguments)
            {
                arg.Accept(this);
                args.Add(_currentValue!);
            }

            TypeChecker.StructInfo? structInfo = _typeChecker.GetStruct(named.Name);
            if (structInfo != null)
            {
                Dictionary<string, ConstValue> fields = new();
                foreach ((string Name, TypeExpression Type) f in structInfo.Fields)
                {
                    fields[f.Name] = new ConstValue.Integer(0);
                }
                ConstructorDeclaration? matchingCtor = null;
                foreach (ConstructorDeclaration ctor in structInfo.Constructors)
                {
                    if (ctor.Parameters.Count == args.Count)
                    {
                        matchingCtor = ctor;
                        break;
                    }
                }

                ConstValue.Struct instance = new ConstValue.Struct(structInfo.Name, fields);
                if (matchingCtor != null)
                {
                    PushScope();
                    _scopes.Peek()["this"] = instance;
                    for (int i = 0; i < matchingCtor.Parameters.Count; i++)
                    {
                        _scopes.Peek()[matchingCtor.Parameters[i].Name] = args[i];
                    }

                    matchingCtor.Body.Accept(this);
                    PopScope();
                }
                else
                {
                    for (int i = 0; i < structInfo.Fields.Count; i++)
                    {
                        if (i < args.Count)
                        {
                            fields[structInfo.Fields[i].Name] = args[i];
                        }
                        else
                        {
                            fields[structInfo.Fields[i].Name] = new ConstValue.Integer(0);
                        }
                    }
                }

                if (node.Kind == AllocationKind.Pointer)
                {
                    _currentValue = new ConstValue.Pointer(structInfo.Name, instance);
                }
                else
                {
                    _currentValue = instance;
                }
                return;
            }

            TypeChecker.ClassInfo? classInfo = _typeChecker.GetClass(named.Name);
            if (classInfo != null)
            {
                if (classInfo.IsAbstract)
                {
                    throw new ConstEvalException($"Cannot instantiate abstract class '{classInfo.Name}' at compile time", node.Line);
                }

                Dictionary<string, ConstValue> fields = new();
                foreach ((string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass) f in classInfo.Fields)
                {
                    fields[f.Name] = new ConstValue.Integer(0);
                }
                ConstructorDeclaration? matchingCtor = null;
                foreach (ConstructorDeclaration ctor in classInfo.Constructors)
                {
                    if (ctor.Parameters.Count == args.Count)
                    {
                        matchingCtor = ctor;
                        break;
                    }
                }

                ConstValue.ClassInstance instance = new ConstValue.ClassInstance(classInfo.Name, fields);
                if (matchingCtor != null)
                {
                    PushScope();
                    _scopes.Peek()["this"] = instance;
                    for (int i = 0; i < matchingCtor.Parameters.Count; i++)
                    {
                        _scopes.Peek()[matchingCtor.Parameters[i].Name] = args[i];
                    }

                    if (matchingCtor.BaseArguments != null && classInfo.BaseClass != null && _typeChecker.GetClass(classInfo.BaseClass) is TypeChecker.ClassInfo baseClassInfo)
                    {
                        List<ConstValue> baseArgs = new();
                        foreach (AstNode bArg in matchingCtor.BaseArguments)
                        {
                            bArg.Accept(this);
                            baseArgs.Add(_currentValue!);
                        }
                        ConstructorDeclaration? baseCtor = baseClassInfo.Constructors.FirstOrDefault(c => c.Parameters.Count == baseArgs.Count);
                        if (baseCtor != null)
                        {
                            PushScope();
                            _scopes.Peek()["this"] = instance;
                            for (int b = 0; b < baseCtor.Parameters.Count; b++)
                            {
                                _scopes.Peek()[baseCtor.Parameters[b].Name] = baseArgs[b];
                            }
                            baseCtor.Body.Accept(this);
                            PopScope();
                        }
                    }

                    matchingCtor.Body.Accept(this);
                    PopScope();
                }
                else
                {
                    for (int i = 0; i < classInfo.Fields.Count; i++)
                    {
                        if (i < args.Count)
                        {
                            fields[classInfo.Fields[i].Name] = args[i];
                        }
                        else
                        {
                            fields[classInfo.Fields[i].Name] = new ConstValue.Integer(0);
                        }
                    }
                }

                if (node.Kind == AllocationKind.Pointer)
                {
                    _currentValue = new ConstValue.Pointer(classInfo.Name, instance);
                }
                else
                {
                    _currentValue = instance;
                }
                return;
            }

            throw new ConstEvalException($"Unknown struct or class '{named.Name}'", node.Line);
        }

        public void Visit(CallExpression node)
        {
            CheckSteps(node.Line);

            if (_callDepth >= MaxCallDepth)
            {
                throw new ConstEvalException($"Compile-time recursion exceeded maximum call depth of {MaxCallDepth}", node.Line);
            }

            string? funcName = null;
            MethodDeclaration? method = null;
            ConstValue.Struct? thisReceiver = null;

            if (node.Callee is IdentifierExpression ident)
            {
                funcName = ident.Name;
                method = _typeChecker.ResolveFunctionForComptime(funcName);
                if (method == null && TryGetVariable("this", out ConstValue? thisVal) && thisVal is ConstValue.Struct thisStruct)
                {
                    TypeChecker.StructInfo? sInfo = _typeChecker.GetStruct(thisStruct.StructName);
                    if (sInfo != null && sInfo.Methods.TryGetValue(funcName, out MethodDeclaration? sm))
                    {
                        method = sm;
                        thisReceiver = thisStruct;
                    }
                }
            }
            else if (node.Callee is MemberAccessExpression memberAccess)
            {
                memberAccess.Object.Accept(this);
                ConstValue targetObj = _currentValue!;
                if (targetObj is ConstValue.Struct sVal)
                {
                    TypeChecker.StructInfo? sInfo = _typeChecker.GetStruct(sVal.StructName);
                    if (sInfo != null && sInfo.Methods.TryGetValue(memberAccess.Member, out MethodDeclaration? sm))
                    {
                        method = sm;
                        funcName = $"{sVal.StructName}.{memberAccess.Member}";
                        thisReceiver = sVal;
                    }
                }
            }

            if (method == null)
            {
                throw new ConstEvalException($"Cannot call '{funcName}' at compile time", node.Line);
            }

            if (!method.IsConst)
            {
                throw new ConstEvalException($"Function '{funcName}' is not marked 'const' and cannot be evaluated at compile time", node.Line);
            }

            List<ConstValue> evaluatedArgs = new();
            foreach (AstNode arg in node.Arguments)
            {
                arg.Accept(this);
                evaluatedArgs.Add(_currentValue!);
            }

            _callDepth++;
            PushScope();

            if (thisReceiver != null)
            {
                _scopes.Peek()["this"] = thisReceiver;
            }

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                _scopes.Peek()[method.Parameters[i].Name] = evaluatedArgs[i];
            }

            bool prevReturned = _hasReturned;
            ConstValue? prevReturnVal = _returnValue;
            _hasReturned = false;
            _returnValue = null;

            method.Body?.Accept(this);

            ConstValue? callResult = _returnValue;

            _hasReturned = prevReturned;
            _returnValue = prevReturnVal;
            PopScope();
            _callDepth--;

            _currentValue = callResult ?? new ConstValue.Integer(0);
        }

        public void Visit(AssignmentExpression node)
        {
            CheckSteps(node.Line);
            node.Value.Accept(this);
            ConstValue val = _currentValue!;

            if (node.Target is IdentifierExpression ident)
            {
                if (TryGetVariable("this", out ConstValue? thisVal))
                {
                    if (thisVal is ConstValue.Struct thisStruct)
                    {
                        TypeChecker.StructInfo? sInfo = _typeChecker.GetStruct(thisStruct.StructName);
                        if ((sInfo != null && sInfo.FieldIndex(ident.Name) >= 0) || thisStruct.Fields.ContainsKey(ident.Name))
                        {
                            thisStruct.Fields[ident.Name] = val;
                            _currentValue = val;
                            return;
                        }
                    }
                    if (thisVal is ConstValue.ClassInstance thisClass)
                    {
                        TypeChecker.ClassInfo? cInfo = _typeChecker.GetClass(thisClass.ClassName);
                        if ((cInfo != null && cInfo.FieldIndex(ident.Name) >= 0) || thisClass.Fields.ContainsKey(ident.Name))
                        {
                            thisClass.Fields[ident.Name] = val;
                            _currentValue = val;
                            return;
                        }
                    }
                }
                SetVariable(ident.Name, val);
                _currentValue = val;
                return;
            }

            if (node.Target is MemberAccessExpression memberAccess)
            {
                memberAccess.Object.Accept(this);
                ConstValue obj = _currentValue!;
                if (obj is ConstValue.Pointer pVal)
                {
                    obj = pVal.Target;
                }
                if (obj is ConstValue.Struct sVal)
                {
                    sVal.Fields[memberAccess.Member] = val;
                    _currentValue = val;
                    return;
                }
                if (obj is ConstValue.ClassInstance cVal)
                {
                    cVal.Fields[memberAccess.Member] = val;
                    _currentValue = val;
                    return;
                }
            }

            throw new ConstEvalException("Invalid assignment target at compile time", node.Line);
        }

        public void Visit(VariableDeclaration node)
        {
            CheckSteps(node.Line);
            if (node.Initializer != null)
            {
                node.Initializer.Accept(this);
                ConstValue val = _currentValue!;
                _scopes.Peek()[node.Name] = val;
            }
            else
            {
                _scopes.Peek()[node.Name] = new ConstValue.Integer(0);
            }
        }

        public void Visit(BlockStatement node)
        {
            PushScope();
            foreach (AstNode stmt in node.Statements)
            {
                stmt.Accept(this);
                if (_hasReturned || _hasBroken || _hasContinued)
                {
                    break;
                }
            }
            PopScope();
        }

        public void Visit(IfStatement node)
        {
            CheckSteps(node.Line);
            node.Condition.Accept(this);
            if (_currentValue is not ConstValue.Boolean b)
            {
                throw new ConstEvalException("If condition must be boolean at compile time", node.Line);
            }

            if (b.Value)
            {
                node.Then.Accept(this);
            }
            else
            {
                node.Else?.Accept(this);
            }
        }

        public void Visit(WhileStatement node)
        {
            while (true)
            {
                CheckSteps(node.Line);
                node.Condition.Accept(this);
                if (_currentValue is not ConstValue.Boolean b)
                {
                    throw new ConstEvalException("While condition must be boolean at compile time", node.Line);
                }
                if (!b.Value)
                {
                    break;
                }

                node.Body.Accept(this);

                if (_hasReturned) break;
                if (_hasBroken)
                {
                    _hasBroken = false;
                    break;
                }
                if (_hasContinued)
                {
                    _hasContinued = false;
                }
            }
        }

        public void Visit(ForStatement node)
        {
            PushScope();
            node.Initializer?.Accept(this);

            while (true)
            {
                CheckSteps(node.Line);
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                    if (_currentValue is not ConstValue.Boolean b)
                    {
                        throw new ConstEvalException("For condition must be boolean at compile time", node.Line);
                    }
                    if (!b.Value) break;
                }

                node.Body.Accept(this);

                if (_hasReturned) break;
                if (_hasBroken)
                {
                    _hasBroken = false;
                    break;
                }
                if (_hasContinued)
                {
                    _hasContinued = false;
                }

                node.Increment?.Accept(this);
            }

            PopScope();
        }

        public void Visit(ForeachStatement node)
        {
            if (node.Desugared != null)
            {
                node.Desugared.Accept(this);
                return;
            }

            CheckSteps(node.Line);
            node.Collection.Accept(this);
            if (_currentValue is not ConstValue.Array arr)
            {
                throw new ConstEvalException("Cannot iterate over non-array at compile time", node.Line);
            }

            PushScope();
            for (int i = 0; i < arr.Elements.Count; i++)
            {
                CheckSteps(node.Line);
                _scopes.Peek()[node.VariableName] = arr.Elements[i];
                node.Body.Accept(this);

                if (_hasReturned) break;
                if (_hasBroken)
                {
                    _hasBroken = false;
                    break;
                }
                if (_hasContinued)
                {
                    _hasContinued = false;
                }
            }
            PopScope();
        }

        public void Visit(ReturnStatement node)
        {
            CheckSteps(node.Line);
            if (node.Value != null)
            {
                node.Value.Accept(this);
                _returnValue = _currentValue;
            }
            else
            {
                _returnValue = null;
            }
            _hasReturned = true;
        }

        public void Visit(BreakStatement node)
        {
            CheckSteps(node.Line);
            _hasBroken = true;
        }

        public void Visit(ContinueStatement node)
        {
            CheckSteps(node.Line);
            _hasContinued = true;
        }

        public void Visit(ExpressionStatement node)
        {
            node.Expression.Accept(this);
        }

        public void Visit(CompilationUnit node)
        {
            foreach (AstNode m in node.Members)
            {
                m.Accept(this);
            }
        }

        public void Visit(FunctionPointerTypeExpression node) { }
        public void Visit(NamedTypeExpression node) { }
        public void Visit(NestedTypeExpression node) { }
        public void Visit(PointerTypeExpression node) { }
        public void Visit(ManagedTypeExpression node) { }
        public void Visit(ArrayTypeExpression node) { }
        public void Visit(NamespaceDeclaration node) { }
        public void Visit(NamespaceAccessExpression node) { }
        public void Visit(UsingDirective node) { }
        public void Visit(StructDeclaration node) { }
        public void Visit(ClassDeclaration node) { }
        public void Visit(InterfaceDeclaration node) { }
        public void Visit(EnumDeclaration node) { }
        public void Visit(EnumMemberDeclaration node) { }
        public void Visit(MethodDeclaration node) { }
        public void Visit(FieldDeclaration node) { }
        public void Visit(ConstructorDeclaration node) { }
        public void Visit(DestructorDeclaration node) { }
        public void Visit(OperatorDeclaration node) { }
        public void Visit(Parameter node) { }
        public void Visit(AttributeNode node) { }
        public void Visit(ExternDeclaration node) { }
        public void Visit(DefaultExpression node) { }
        public void Visit(LambdaExpression node) { }
        public void Visit(DeferStatement node) { }
        public void Visit(GlobalExpression node) { }
        public void Visit(AliasDeclaration node) { }
        public void Visit(InterpolatedStringExpression node) { }
        public void Visit(ThrowStatement node) => throw new ConstEvalException("Throw statement is not supported at compile time", node.Line);
        public void Visit(TryStatement node) => throw new ConstEvalException("Try statement is not supported at compile time", node.Line);
        public void Visit(CatchClause node) => throw new ConstEvalException("Catch clause is not supported at compile time", node.Line);

        public void Visit(SizeofExpression node)
        {
            CheckSteps(node.Line);
            TypeExpression targetType = _typeChecker.ResolveAlias(node.TargetType);
            if (targetType is NamedTypeExpression named &&
                !TypeChecker.IsPrimitive(named.Name) &&
                _typeChecker.GetStruct(named.Name) == null &&
                _typeChecker.GetClass(named.Name) == null &&
                _typeChecker.GetInterface(named.Name) == null &&
                _typeChecker.ResolveEnum(named) == null &&
                TryGetVariable(named.Name, out _))
            {
                if (_typeChecker.TryLookupVariable(named.Name, out TypeExpression? varType) && varType != null)
                {
                    targetType = _typeChecker.ResolveAlias(varType);
                }
            }
            int size = _typeChecker.GetTypeSize(targetType);
            _currentValue = new ConstValue.Integer(size);
        }

        public void Visit(NameofExpression node)
        {
            CheckSteps(node.Line);
            string name = _typeChecker.ExtractName(node.Target);
            _currentValue = new ConstValue.String(name);
        }

        public void Visit(PrefixedStringLiteralExpression node)
        {
            CheckSteps(node.Line);

            if (node.Prefix == "c")
            {
                _currentValue = new ConstValue.String(node.Literal.Token.Text[1..^1]);
                return;
            }

            TypeChecker.StringPrefixHandler? handler = _typeChecker.GetResolvedPrefixHandler(node);
            if (handler == null)
            {
                throw new ConstEvalException($"Unresolved string prefix '{node.Prefix}'", node.Line);
            }

            if (!handler.IsConst)
            {
                throw new ConstEvalException($"String prefix handler for '{node.Prefix}' is not marked 'const' and cannot be evaluated at compile time", node.Line);
            }

            string rawText = node.Literal.Token.Text[1..^1];
            ConstValue strArg = new ConstValue.String(rawText);

            if (handler.IsConstructor)
            {
                ConstructorDeclaration ctor = handler.Constructor!;
                string typeName = handler.EnclosingTypeName!;
                TypeChecker.StructInfo? structInfo = _typeChecker.GetStruct(typeName);
                if (structInfo == null)
                {
                    throw new ConstEvalException($"Cannot instantiate '{typeName}' at compile time (only structs are supported)", node.Line);
                }

                Dictionary<string, ConstValue> fields = new();
                ConstValue.Struct instance = new ConstValue.Struct(structInfo.Name, fields);

                PushScope();
                _scopes.Peek()["this"] = instance;
                _scopes.Peek()[ctor.Parameters[0].Name] = strArg;

                ctor.Body.Accept(this);
                PopScope();

                _currentValue = instance;
            }
            else if (handler.Method != null)
            {
                MethodDeclaration method = handler.Method;
                if (_callDepth >= MaxCallDepth)
                {
                    throw new ConstEvalException($"Compile-time recursion exceeded maximum call depth of {MaxCallDepth}", node.Line);
                }

                _callDepth++;
                PushScope();
                _scopes.Peek()[method.Parameters[0].Name] = strArg;

                bool prevReturned = _hasReturned;
                ConstValue? prevReturnVal = _returnValue;
                _hasReturned = false;
                _returnValue = null;

                method.Body?.Accept(this);

                ConstValue? callResult = _returnValue;

                _hasReturned = prevReturned;
                _returnValue = prevReturnVal;
                PopScope();
                _callDepth--;

                _currentValue = callResult ?? new ConstValue.Integer(0);
            }
            else
            {
                throw new ConstEvalException($"Unsupported prefix handler kind for '{node.Prefix}'", node.Line);
            }
        }
    }
}
