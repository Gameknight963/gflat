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

    public partial class ConstEvaluator : IVisitor
    {
        private const int MaxSteps = 100000;
        private const int MaxCallDepth = 256;

        private readonly IConstEvaluationContext _context;
        private readonly Stack<Dictionary<string, ConstValue>> _scopes = new();
        private int _stepCount = 0;
        private int _callDepth = 0;

        private ConstValue? _currentValue = null;
        private bool _hasReturned = false;
        private ConstValue? _returnValue = null;
        private TypeExpression? _returnTarget;
        private bool _hasBroken = false;
        private bool _hasContinued = false;
        private bool _evaluating;

        public ConstEvaluator(IConstEvaluationContext context)
        {
            _context = context;
        }

        public bool TryEvaluate(AstNode node, out ConstValue? result, out string? failureReason)
            => TryEvaluate(node, null, out result, out failureReason);

        public bool TryEvaluate(AstNode node, TypeExpression? target, out ConstValue? result, out string? failureReason)
        {
            // Resolving a type during evaluation may require another constant
            // (for example an array bound). Give that query independent state.
            if (_evaluating)
                return new ConstEvaluator(_context).TryEvaluate(node, target, out result, out failureReason);
            _evaluating = true;
            result = null;
            failureReason = null;
            _stepCount = 0;
            _callDepth = 0;
            _hasReturned = false;
            _returnValue = null;
            _returnTarget = null;
            _hasBroken = false;
            _hasContinued = false;
            _currentValue = null;
            _scopes.Clear();
            _scopes.Push(new Dictionary<string, ConstValue>());

            try
            {
                if (target == null) node.Accept(this);
                else _currentValue = EvaluateValueForTarget(node, target);
                if (_currentValue != null && ContainsTransientAddress(_currentValue))
                    throw new ConstEvalException("A borrowed compile-time address cannot escape into runtime storage", node.Line);
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
            finally { _evaluating = false; }
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

        private bool TryGetVariable(string name, out ConstValue? value, AstNode? binding = null)
        {
            foreach (Dictionary<string, ConstValue> scope in _scopes)
            {
                if (scope.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            if (binding != null && _context.TryGetConstValue(binding, out value)) return true;
            if (_context.TryGetConstValueByName(name, out value))
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
                TokenKind.IntLiteral or TokenKind.UIntLiteral or TokenKind.HexInt or
                    TokenKind.LongLiteral or TokenKind.ULongLiteral => ParseIntegerLiteral(node),
                TokenKind.FloatLiteral or TokenKind.DoubleLiteral => new ConstValue.Float(FloatingLiteralEncoding.Value(node.Token)),
                TokenKind.CharLiteral => new ConstValue.Char((char)CharacterLiteralEncoding.Value(node.Token.Text, node.Line)),
                TokenKind.StringLiteral => new ConstValue.String(node.Token.Text[1..^1]),
                TokenKind.True => new ConstValue.Boolean(true),
                TokenKind.False => new ConstValue.Boolean(false),
                TokenKind.Null => new ConstValue.Integer(0),
                _ => throw new ConstEvalException($"Unsupported compile-time literal '{node.Token.Kind}'", node.Line)
            };
        }

        private static ConstValue ParseIntegerLiteral(LiteralExpression node)
        {
            if (!TypeChecker.TryGetIntegerLiteralValue(node, out var integer))
                throw new ConstEvalException("Invalid integer literal", node.Line);
            if (node.Token.Kind is TokenKind.UIntLiteral or TokenKind.ULongLiteral)
                return new ConstValue.UInteger((ulong)integer);
            if (node.Token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return new ConstValue.Integer(unchecked((long)(ulong)integer));
            return new ConstValue.Integer((long)integer);
        }

        public void Visit(IdentifierExpression node)
        {
            if (_context.GetPropertyRead(node) != null || _context.GetPropertyWrite(node) != null)
                throw new ConstEvalException("Property access is not yet supported in constant evaluation", node.Line);
            CheckSteps(node.Line);
            if (!TryGetVariable(node.Name, out ConstValue? val, node) || val == null)
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
            EvaluateBinary(node);
            _currentValue = Normalize(_currentValue!, _context.GetType(node), node.Line);
        }

        private ConstValue Normalize(ConstValue value, TypeExpression type, int line)
        {
            if (NumericType(type) is NamedTypeExpression named &&
                (NumericSemantics.IntegerType(named.Name) != null || named.Name is "float" or "double"))
                return NumericSemantics.Cast(value, named.Name, line);
            return value;
        }

        private ConstValue EvaluateValueForTarget(AstNode expression, TypeExpression target)
        {
            if (!_context.IsFreshValue(expression) && _context.RequiresCopy(target))
                throw new ConstEvalException("Nontrivial copying is not supported during constant evaluation", expression.Line);
            if (NumericType(target) is NamedTypeExpression named &&
                NumericSemantics.IntegerType(named.Name) is { } integerType &&
                TypeChecker.TryGetIntegerLiteralValue(expression, out var integer) &&
                TypeChecker.FitsIntegerLiteral(named.Name, integer))
                return integerType.Bits > 64 ? new ConstValue.WideInteger(integer) :
                    integerType.Signed ? new ConstValue.Integer((long)integer) : new ConstValue.UInteger((ulong)integer);
            expression.Accept(this);
            return CopyTransferredValue(Normalize(_currentValue!, target, expression.Line), target);
        }

        private void EvaluateBinary(BinaryExpression node)
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

            _context.TryGetNativeIntegerOperands(node, out TypeExpression nativeOperands);
            if (nativeOperands == null) _context.TryGetIntegerComparisonOperands(node, out nativeOperands);
            if (nativeOperands == null) _context.TryGetFloatingOperands(node, out nativeOperands);
            TypeExpression? operandType = nativeOperands ?? IntegerArithmeticOperands(node);
            ConstValue EvaluateOperand(AstNode operand)
            {
                if (operandType != null) return EvaluateValueForTarget(operand, operandType);
                if (node.Operator is TokenKind.LessLess or TokenKind.GreaterGreater)
                    return EvaluateValueForTarget(operand, _context.GetType(operand));
                operand.Accept(this);
                return _currentValue!;
            }
            ConstValue left = EvaluateOperand(node.Left);
            ConstValue right = EvaluateOperand(node.Right);

            if (EvaluateCStringBinary(node, left, right)) return;
            left = ArithmeticOperand(left);
            right = ArithmeticOperand(right);
            if (EvaluateIntegerShift(node, left, right)) return;
            CheckIntegerDivision(node, left, right);

            if (left is ConstValue.Float a && right is ConstValue.Float b &&
                node.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals)
            {
                _currentValue = new ConstValue.Boolean(node.Operator switch
                {
                    TokenKind.EqualsEquals => a.Value == b.Value,
                    TokenKind.NotEquals => a.Value != b.Value,
                    TokenKind.Less => a.Value < b.Value,
                    TokenKind.LessEquals => a.Value <= b.Value,
                    TokenKind.Greater => a.Value > b.Value,
                    _ => a.Value >= b.Value
                });
                return;
            }

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
            if (node.Operator == TokenKind.Bang && !node.IsPrefix)
            {
                node.Operand.Accept(this);
                return;
            }
            if (_context.GetPropertyRead(node) != null || _context.GetPropertyWrite(node) != null)
                throw new ConstEvalException("Property access is not yet supported in constant evaluation", node.Line);
            CheckSteps(node.Line);

            if (_context.ResolveAlias(_context.GetType(node)) is NamedTypeExpression unaryType &&
                NumericSemantics.IntegerType(unaryType.Name) is { } integerType &&
                TypeChecker.TryGetIntegerLiteralValue(node, out var integer) && TypeChecker.FitsIntegerLiteral(unaryType.Name, integer))
            {
                _currentValue = integerType.Bits > 64 ? new ConstValue.WideInteger(integer) :
                    integerType.Signed ? new ConstValue.Integer((long)integer) : new ConstValue.UInteger((ulong)integer);
                return;
            }

            if (node.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus)
            {
                var storage = CaptureAssignmentStorage(node.Operand);
                ConstValue previous = storage.Read();
                ConstValue one = previous is ConstValue.Float ? new ConstValue.Float(1) :
                    previous is ConstValue.UInteger ? new ConstValue.UInteger(1) : new ConstValue.Integer(1);
                bool decrement = node.Operator == TokenKind.MinusMinus;
                bool valid = StepCString(previous, node.Operand, decrement, out ConstValue? next);
                if (!valid)
                    valid = decrement ? ConstValue.TrySubtract(ArithmeticOperand(previous), one, out next) :
                        ConstValue.TryAdd(ArithmeticOperand(previous), one, out next);
                if (!valid || next == null)
                    throw new ConstEvalException($"Cannot apply '{(decrement ? "--" : "++")}' to '{node.Operand}' at compile time", node.Line);
                next = Normalize(next, _context.GetType(node.Operand), node.Line);
                storage.Write(next);
                _currentValue = node.IsPrefix ? next : previous;
                return;
            }

            if (TryBorrowScalar(node)) return;
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
                    if (operand is ConstValue.BorrowedReference borrowed)
                    {
                        _currentValue = borrowed.Read();
                        return;
                    }
                    if (operand is ConstValue.ArrayPointer arrayPointer)
                    {
                        if (arrayPointer.Target.Elements.Count == 0) throw new ConstEvalException("Cannot dereference an empty constant array", node.Line);
                        _currentValue = arrayPointer.Target.Elements[0];
                        return;
                    }
                    if (operand is ConstValue.String text)
                    {
                        RequireCStringByteView(node.Operand);
                        _currentValue = ReadCString(text, 0, node.Line);
                        return;
                    }
                    if (operand is ConstValue.Pointer ptr)
                    {
                        _currentValue = ptr.Target;
                        return;
                    }
                    throw new ConstEvalException($"Cannot dereference non-pointer '{operand}' at compile time", node.Line);

                case TokenKind.Minus:
                    if (operand is ConstValue.UInteger unsigned)
                    {
                        _currentValue = Normalize(new ConstValue.UInteger(unchecked(0ul - unsigned.Value)), _context.GetType(node), node.Line);
                        return;
                    }
                    if (ConstValue.TryNegate(ArithmeticOperand(operand), out ConstValue? negRes))
                    {
                        _currentValue = Normalize(negRes!, _context.GetType(node), node.Line);
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
                    if (ConstValue.TryBitwiseNot(ArithmeticOperand(operand), out ConstValue? bnotRes))
                    {
                        _currentValue = Normalize(bnotRes!, _context.GetType(node), node.Line);
                        return;
                    }
                    throw new ConstEvalException($"Cannot apply '~' to '{operand}' at compile time", node.Line);

                default:
                    throw new ConstEvalException($"Unary operator '{node.Operator}' not supported at compile time", node.Line);
            }
        }

        public void Visit(MemberAccessExpression node)
        {
            if (_context.GetPropertyRead(node) != null || _context.GetPropertyWrite(node) != null)
                throw new ConstEvalException("Property access is not yet supported in constant evaluation", node.Line);
            CheckSteps(node.Line);
            node.Object.Accept(this);
            ConstValue obj = _currentValue!;

            if (obj is ConstValue.BorrowedReference borrowed) obj = borrowed.Read();

            if (obj is ConstValue.Pointer ptrVal)
            {
                obj = ptrVal.Target;
            }
            if (obj is ConstValue.ArrayPointer arrayPointer)
            {
                if (arrayPointer.Target.Elements.Count == 0) throw new ConstEvalException("Cannot dereference an empty constant array", node.Line);
                obj = arrayPointer.Target.Elements[0];
            }

            if (obj is ConstValue.String)
            {
                throw new ConstEvalException("C-string pointers have no members or implicit length", node.Line);
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

        public void Visit(ArrayLiteralExpression node)
        {
            CheckSteps(node.Line);
            var elements = new List<ConstValue>();
            TypeExpression? elementType = (_context.ResolveAlias(_context.GetType(node)) as ArrayTypeExpression)?.ElementType;
            foreach (var element in node.Elements)
                elements.Add(EvaluateValueForTarget(element, elementType ?? _context.GetType(element)));
            _currentValue = new ConstValue.Array(elements);
        }

        public void Visit(IndexExpression node)
        {
            if (_context.GetPropertyRead(node) != null)
                throw new ConstEvalException("Indexer access is not yet supported in constant evaluation", node.Line);
            CheckSteps(node.Line);
            node.Target.Accept(this);
            ConstValue target = _currentValue!;
            if (target is ConstValue.ArrayPointer arrayPointer) target = arrayPointer.Target;

            node.Index.Accept(this);
            ConstValue index = _currentValue!;

            if (target is ConstValue.String text)
            {
                RequireCStringByteView(node.Target);
                _currentValue = ReadCString(text, CStringIndex(index, node.Line), node.Line);
                return;
            }

            if (!ConstValue.TryIntegerValue(index, out var idx))
                throw new ConstEvalException("Index must be an integer", node.Line);

            if (target is ConstValue.BorrowedReference borrowed)
            {
                if (idx != 0)
                    throw new ConstEvalException("Nonzero offsets from a borrowed scalar address are not supported during constant evaluation", node.Line);
                _currentValue = borrowed.Read();
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
            if (TryEvaluateExplicitConversionCast(node)) return;
            ConstValue val = EvaluateValueForTarget(node.Operand, node.TargetType);

            TypeExpression resolved = NumericType(node.TargetType);
            if (val is ConstValue.Boolean && resolved is NamedTypeExpression { Name: "bool" } ||
                val is ConstValue.Integer { Value: 0 } && resolved is
                    (PointerTypeExpression { IsNullable: true } or FunctionPointerTypeExpression { IsNullable: true } or ManagedTypeExpression { IsNullable: true }) ||
                val is (ConstValue.Pointer or ConstValue.ArrayPointer or ConstValue.BorrowedReference) && resolved is PointerTypeExpression &&
                    TypeChecker.TypesMatchPublic(_context.ResolveAlias(_context.GetType(node.Operand)), resolved))
            {
                _currentValue = val;
                return;
            }
            if (val is ConstValue.String && resolved is PointerTypeExpression pointer &&
                _context.ResolveAlias(pointer.Inner) is NamedTypeExpression { Name: "char" })
            {
                _currentValue = val;
                return;
            }
            if (resolved is not NamedTypeExpression target)
                throw new ConstEvalException("Unsupported constant cast", node.Line);
            _currentValue = NumericSemantics.Cast(val, target.Name, node.Line);
        }

        public void Visit(NewExpression node)
        {
            CheckSteps(node.Line);
            if (_callDepth >= MaxCallDepth)
                throw new ConstEvalException($"Compile-time recursion exceeded maximum call depth of {MaxCallDepth}", node.Line);
            _callDepth++;
            try { EvaluateNew(node); }
            finally { _callDepth--; }
        }

        private void EvaluateNew(NewExpression node)
        {
            CheckSteps(node.Line);
            if (node.Destination != null)
                throw new ConstEvalException("Placement construction is not permitted at compile time", node.Line);
            if (node.Kind == AllocationKind.Managed)
            {
                throw new ConstEvalException("Managed allocation ('new^') is not permitted at compile time", node.Line);
            }

            TypeExpression resolvedType = _context.ResolveAlias(node.Type);
            if (resolvedType is not NamedTypeExpression named)
            {
                throw new ConstEvalException("Cannot instantiate anonymous type at compile time", node.Line);
            }

            List<ConstValue> args = new();
            ConstructorDeclaration? resolvedConstructor = _context.GetResolvedConstructor(node);
            foreach (AstNode arg in node.Arguments)
            {
                if (resolvedConstructor != null)
                    args.Add(EvaluateValueForTarget(arg, resolvedConstructor.Parameters[args.Count].Type));
                else { arg.Accept(this); args.Add(_currentValue!); }
            }

            TypeChecker.StructInfo? structInfo = _context.GetStruct(named.Name);
            if (structInfo != null)
            {
                Dictionary<string, ConstValue> fields = new();
                foreach ((string Name, TypeExpression Type) f in structInfo.Fields)
                {
                    fields[f.Name] = new ConstValue.Integer(0);
                }
                ConstructorDeclaration? matchingCtor = _context.GetResolvedConstructor(node);

                ConstValue.Struct instance = new ConstValue.Struct(structInfo.Name, fields);
                if (matchingCtor != null)
                {
                    var callerScopes = _scopes.Reverse().ToArray();
                    bool returned = _hasReturned;
                    ConstValue? returnValue = _returnValue;
                    _scopes.Clear();
                    PushScope();
                    _hasReturned = false;
                    _returnValue = null;
                    try
                    {
                        _scopes.Peek()["this"] = instance;
                        for (int i = 0; i < matchingCtor.Parameters.Count; i++)
                            _scopes.Peek()[matchingCtor.Parameters[i].Name] = Normalize(args[i], matchingCtor.Parameters[i].Type, node.Line);
                        matchingCtor.Body.Accept(this);
                    }
                    finally
                    {
                        _scopes.Clear();
                        foreach (var scope in callerScopes) _scopes.Push(scope);
                        _hasReturned = returned;
                        _returnValue = returnValue;
                    }
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

            TypeChecker.ClassInfo? classInfo = _context.GetClass(named.Name);
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
                ConstValue.ClassInstance instance = new(classInfo.Name, fields);
                ExecuteClassConstructor(classInfo, _context.GetResolvedConstructor(node), args, instance, node.Line);

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

        private void ExecuteClassConstructor(TypeChecker.ClassInfo info, ConstructorDeclaration? ctor,
            List<ConstValue> args, ConstValue.ClassInstance instance, int line)
        {
            CheckSteps(line);
            if (_callDepth >= MaxCallDepth)
                throw new ConstEvalException($"Compile-time recursion exceeded maximum call depth of {MaxCallDepth}", line);
            _callDepth++;
            bool returned = _hasReturned;
            ConstValue? returnValue = _returnValue;
            _hasReturned = false;
            _returnValue = null;
            var callerScopes = _scopes.Reverse().ToArray();
            _scopes.Clear();
            PushScope();
            try
            {
                _scopes.Peek()["this"] = instance;
                if (ctor != null)
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                        _scopes.Peek()[ctor.Parameters[i].Name] = Normalize(args[i], ctor.Parameters[i].Type, line);
                if (info.BaseClass != null && _context.GetClass(info.BaseClass) is TypeChecker.ClassInfo baseInfo)
                {
                    List<ConstValue> baseArgs = new();
                    ConstructorDeclaration? baseCtor = ctor?.BaseArguments != null
                        ? _context.GetResolvedBaseConstructor(ctor)
                        : baseInfo.Constructors.FirstOrDefault(c => c.Parameters.Count == 0);
                    if (ctor?.BaseArguments != null)
                        foreach (AstNode arg in ctor.BaseArguments)
                        {
                            if (baseCtor != null) baseArgs.Add(EvaluateValueForTarget(arg, baseCtor.Parameters[baseArgs.Count].Type));
                            else { arg.Accept(this); baseArgs.Add(_currentValue!); }
                        }
                    ExecuteClassConstructor(baseInfo, baseCtor, baseArgs, instance, line);
                }
                // Initializers are bound in the class scope, outside constructor parameters.
                var parameterScope = _scopes.Pop();
                PushScope();
                _scopes.Peek()["this"] = instance;
                foreach (FieldDeclaration field in info.FieldDeclarations.Where(f => !f.IsStatic))
                {
                    if (field.IsStatic || field.IsConst) continue;
                    if (field.Initializer != null)
                    {
                        instance.Fields[field.Name] = EvaluateValueForTarget(field.Initializer, field.Type);
                    }
                    else instance.Fields[field.Name] = DefaultValue(field.Type, field.Line);
                }
                PopScope();
                _scopes.Push(parameterScope);
                ctor?.Body.Accept(this);
            }
            finally
            {
                _scopes.Clear();
                foreach (var scope in callerScopes) _scopes.Push(scope);
                _hasReturned = returned;
                _returnValue = returnValue;
                _callDepth--;
            }
        }

        private ConstValue DefaultValue(TypeExpression type, int line)
        {
            type = _context.ResolveAlias(type);
            if (type is ArrayTypeExpression array)
                return new ConstValue.Array(Enumerable.Range(0, array.Size ?? throw new ConstEvalException("Unsized array has no compile-time default", line)).Select(_ => DefaultValue(array.ElementType, line)).ToList());
            if (type is NamedTypeExpression named)
            {
                if (named.Name == "bool") return new ConstValue.Boolean(false);
                if (named.Name == "char") return new ConstValue.Char('\0');
                if (_context.GetStruct(named.Name) is TypeChecker.StructInfo str)
                    return new ConstValue.Struct(str.Name, str.Fields.ToDictionary(f => f.Name, f => DefaultValue(f.Type, line)));
                if (_context.GetClass(named.Name) is TypeChecker.ClassInfo cls)
                    return new ConstValue.ClassInstance(cls.Name, cls.Fields.ToDictionary(f => f.Name, f => DefaultValue(f.Type, line)));
            }
            return Normalize(new ConstValue.Integer(0), type, line);
        }

        public void Visit(DestructorCallExpression node)
            => throw new ConstEvalException("Explicit destructor calls are not supported during constant evaluation", node.Line);

        public void Visit(MoveExpression node)
            => throw new ConstEvalException("Resource movement is not supported during constant evaluation", node.Line);

        public void Visit(CallExpression node)
        {
            if (TryEvaluateReinterpretCall(node)) return;
            if (_context is TypeChecker extensionChecker && extensionChecker.GetExtensionCall(node) is { } extensionCall)
            {
                extensionCall.Accept(this);
                return;
            }
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
                method = _context is TypeChecker resolvedChecker && resolvedChecker.GetResolvedCall(node) is MethodDeclaration resolved
                    ? resolved : _context.ResolveFunctionForComptime(funcName);
                if (method == null && TryGetVariable("this", out ConstValue? thisVal) && thisVal is ConstValue.Struct thisStruct)
                {
                    TypeChecker.StructInfo? sInfo = _context.GetStruct(thisStruct.StructName);
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
                    TypeChecker.StructInfo? sInfo = _context.GetStruct(sVal.StructName);
                    if (sInfo != null && sInfo.Methods.TryGetValue(memberAccess.Member, out MethodDeclaration? sm))
                    {
                        method = sm;
                        funcName = $"{sVal.StructName}.{memberAccess.Member}";
                        thisReceiver = sVal;
                    }
                }
            }

            if (_context is TypeChecker checker)
            {
                var selected = checker.GetResolvedCall(node);
                if (selected is MethodDeclaration selectedMethod) method = selectedMethod;
                else if (selected is ExternDeclaration external)
                {
                    string declarationNamespace = checker.DeclarationNamespace(external) ?? "";
                    funcName = declarationNamespace.Length == 0 ? external.Name : declarationNamespace + "::" + external.Name;
                }
            }

            if (method == null)
            {
                throw new ConstEvalException($"Cannot call '{funcName}' at compile time", node.Line);
            }

            if (!method.IsConst)
            {
                string displayName = _context is TypeChecker displayChecker
                    ? displayChecker.DisplayName(funcName ?? method.Name) : funcName ?? method.Name;
                throw new ConstEvalException($"Function '{displayName}' is not marked 'const' and cannot be evaluated at compile time", node.Line);
            }

            if (method.Parameters.Any(parameter => _context.ResolveAlias(parameter.Type) is ReferenceTypeExpression))
                throw new ConstEvalException("ref parameters are not supported during constant evaluation", node.Line);
            List<ConstValue> evaluatedArgs = new();
            foreach (AstNode arg in node.Arguments)
            {
                evaluatedArgs.Add(EvaluateValueForTarget(arg, method.Parameters[evaluatedArgs.Count].Type));
            }

            _callDepth++;
            var callerScopes = _scopes.Reverse().ToArray();
            bool prevReturned = _hasReturned;
            ConstValue? prevReturnVal = _returnValue;
            TypeExpression? prevReturnTarget = _returnTarget;
            _scopes.Clear();
            PushScope();
            _hasReturned = false;
            _returnValue = null;
            _returnTarget = method.ReturnType;
            try
            {
                if (thisReceiver != null) _scopes.Peek()["this"] = thisReceiver;
                for (int i = 0; i < method.Parameters.Count; i++)
                    _scopes.Peek()[method.Parameters[i].Name] = Normalize(evaluatedArgs[i], method.Parameters[i].Type, node.Line);
                method.Body?.Accept(this);
                _currentValue = Normalize(_returnValue ?? new ConstValue.Integer(0), method.ReturnType, node.Line);
            }
            finally
            {
                _hasReturned = prevReturned;
                _returnValue = prevReturnVal;
                _returnTarget = prevReturnTarget;
                _scopes.Clear();
                foreach (var scope in callerScopes) _scopes.Push(scope);
                _callDepth--;
            }
        }

        public void Visit(AssignmentExpression node)
        {
            if (_context.GetPropertyRead(node) != null || _context.GetPropertyWrite(node) != null)
                throw new ConstEvalException("Property access is not yet supported in constant evaluation", node.Line);
            CheckSteps(node.Line);
            var storage = CaptureAssignmentStorage(node.Target);
            ConstValue? previous = node.Operator == TokenKind.Equals ? null : storage.Read();
            ConstValue val = EvaluateValueForTarget(node.Value, _context.GetType(node.Target));

            if (node.Operator != TokenKind.Equals)
            {
                CheckIntegerDivision(node.Operator, _context.GetType(node.Target), ArithmeticOperand(previous!), ArithmeticOperand(val), node.Line);
                ConstValue? result = null;
                string? error = null;
                bool valid;
                if (previous is ConstValue.String text && node.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals)
                {
                    result = OffsetCString(text, val, node.Target, node.Operator == TokenKind.MinusEquals, node.Line);
                    valid = true;
                }
                else valid = node.Operator switch
                {
                    TokenKind.PlusEquals => ConstValue.TryAdd(ArithmeticOperand(previous!), ArithmeticOperand(val), out result),
                    TokenKind.MinusEquals => ConstValue.TrySubtract(ArithmeticOperand(previous!), ArithmeticOperand(val), out result),
                    TokenKind.StarEquals => ConstValue.TryMultiply(ArithmeticOperand(previous!), ArithmeticOperand(val), out result),
                    TokenKind.SlashEquals => ConstValue.TryDivide(ArithmeticOperand(previous!), ArithmeticOperand(val), out result, out error),
                    TokenKind.PercentEquals => ConstValue.TryModulo(ArithmeticOperand(previous!), ArithmeticOperand(val), out result, out error),
                    _ => false
                };
                if (!valid || result == null) throw new ConstEvalException(error ?? "Unsupported compound assignment", node.Line);
                val = result;
            }
            val = Normalize(val, _context.GetType(node.Target), node.Line);

            storage.Write(val);
            _currentValue = val;
        }

        public void Visit(VariableDeclaration node)
        {
            CheckSteps(node.Line);
            if (node.Initializer != null)
            {
                _scopes.Peek()[node.Name] = EvaluateValueForTarget(node.Initializer, _context.GetType(node));
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
                if (_context.RequiresCopy(node.ElementType))
                    throw new ConstEvalException("Nontrivial copying is not supported during constant evaluation", node.Line);
                _scopes.Peek()[node.VariableName] = CopyTransferredValue(arr.Elements[i], node.ElementType);
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
                if (_returnTarget == null) node.Value.Accept(this);
                else _currentValue = EvaluateValueForTarget(node.Value, _returnTarget);
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

        public void Visit(ReferenceTypeExpression node) => throw new ConstEvalException("ref parameters are not supported during constant evaluation", node.Line);

        public void Visit(FunctionPointerTypeExpression node) { }
        public void Visit(NamedTypeExpression node) { }
        public void Visit(NestedTypeExpression node) { }
        public void Visit(PointerTypeExpression node) { }
        public void Visit(ManagedTypeExpression node) { }
        public void Visit(ArrayTypeExpression node) { }
        public void Visit(NamespaceDeclaration node) { }
        public void Visit(NamespaceAccessExpression node)
        {
            if (_context.TryGetConstValue(node, out var constant)) { _currentValue = constant; return; }
            if (_context.GetPropertyRead(node) != null || _context.GetPropertyWrite(node) != null)
                throw new ConstEvalException("Property access is not yet supported in constant evaluation", node.Line);
            CheckSteps(node.Line);
            if (_context.TryGetEnumMember(node, out long value, out TypeExpression? underlying))
                _currentValue = Normalize(new ConstValue.Integer(value), underlying!, node.Line);
            else throw new ConstEvalException("Namespace expression is not a compile-time constant", node.Line);
        }
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
        public void Visit(DefaultExpression node) => _currentValue = _context.TryGetConstValue(node, out var value)
            ? value : DefaultValue(_context.GetType(node), node.Line);
        public void Visit(LambdaExpression node) => throw new ConstEvalException("LambdaExpression is not supported at compile time", node.Line);
        public void Visit(DeferStatement node) => throw new ConstEvalException("DeferStatement is not supported at compile time", node.Line);
        public void Visit(DeleteStatement node) => throw new ConstEvalException("Delete statement is not supported in constant evaluation", node.Line);
        public void Visit(GlobalExpression node) => throw new ConstEvalException("GlobalExpression is not supported at compile time", node.Line);
        public void Visit(AliasDeclaration node) { }
        public void Visit(InterpolatedStringExpression node) => throw new ConstEvalException("InterpolatedStringExpression is not supported at compile time", node.Line);
        public void Visit(ThrowStatement node) => throw new ConstEvalException("Throw statement is not supported at compile time", node.Line);
        public void Visit(TryStatement node) => throw new ConstEvalException("Try statement is not supported at compile time", node.Line);
        public void Visit(CatchClause node) => throw new ConstEvalException("Catch clause is not supported at compile time", node.Line);

        public void Visit(SizeofExpression node) => VisitLayout(node, node.TargetType, false);
        public void Visit(AlignofExpression node) => VisitLayout(node, node.TargetType, true);
        public void Visit(TypeofExpression node) { CheckSteps(node.Line); _currentValue = _context.DescribeType(node.TargetType, node.Line); }

        private void VisitLayout(AstNode node, TypeExpression target, bool alignment)
        {
            CheckSteps(node.Line);
            TypeExpression targetType = _context.ResolveAlias(target);
            if (targetType is NamedTypeExpression { Namespace: null } named &&
                !TypeChecker.IsPrimitive(named.Name) &&
                _context.GetStruct(named.Name) == null &&
                _context.GetClass(named.Name) == null &&
                _context.GetInterface(named.Name) == null &&
                _context.ResolveEnum(named) == null &&
                TryGetVariable(named.Name, out _))
            {
                if (_context.TryLookupVariable(named.Name, out TypeExpression? varType) && varType != null)
                {
                    targetType = _context.ResolveAlias(varType);
                }
            }
            int size = alignment ? _context.GetTypeAlignment(targetType) : _context.GetTypeSize(targetType);
            _currentValue = new ConstValue.Integer(size);
        }

        public void Visit(NameofExpression node)
        {
            CheckSteps(node.Line);
            string name = _context.ExtractName(node.Target);
            _currentValue = new ConstValue.String(name);
        }

        public void Visit(PrefixedStringLiteralExpression node)
        {
            CheckSteps(node.Line);

            throw new ConstEvalException($"Unresolved string prefix '{node.Prefix}'", node.Line);
        }
    }
}
