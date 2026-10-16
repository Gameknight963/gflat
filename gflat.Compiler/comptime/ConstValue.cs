using System;
using System.Collections.Generic;
using System.Globalization;
using gflat.ast;

namespace gflat.comptime
{
    public abstract partial record ConstValue
    {
        public sealed record Integer(long Value) : ConstValue
        {
            public override string ToString() => Value.ToString();
        }

        public sealed record UInteger(ulong Value) : ConstValue
        {
            public override string ToString() => Value.ToString();
        }

        public sealed record Float(double Value) : ConstValue
        {
            public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        }

        public sealed record Boolean(bool Value) : ConstValue
        {
            public override string ToString() => Value ? "true" : "false";
        }

        public sealed record Char(char Value) : ConstValue
        {
            public override string ToString() => Value.ToString();
        }

        // The source spelling describes immutable UTF-8 storage, including its
        // final null byte. Offset is an address within that storage, in bytes.
        public sealed record String(string Value, int Offset = 0) : ConstValue
        {
            public override string ToString() => Offset == 0 ? Value : $"{Value} (byte offset {Offset})";

            // Data remaining in this view, excluding the allocation's final
            // terminator. An address one past the allocation has no byte view.
            public byte[] BytesFromOffset(int line = 0)
            {
                byte[] bytes = StringLiteralEncoding.Bytes(Value, line);
                if (Offset < 0 || Offset > bytes.Length)
                    throw new ConstEvalException("C-string pointer is outside readable constant storage", line);
                return bytes.AsSpan(Offset).ToArray();
            }
        }

        public sealed record Struct(string StructName, Dictionary<string, ConstValue> Fields) : ConstValue
        {
            public override string ToString() => $"{StructName} {{ {string.Join(", ", Fields)} }}";
        }

        public sealed record ClassInstance(string ClassName, Dictionary<string, ConstValue> Fields) : ConstValue
        {
            public override string ToString() => $"{ClassName} {{ {string.Join(", ", Fields)} }}";
        }

        public sealed record Pointer(string TypeName, ConstValue Target, string? GlobalName = null) : ConstValue
        {
            public override string ToString() => $"&{Target}";
        }

        public sealed record Array(List<ConstValue> Elements) : ConstValue
        {
            public override string ToString() => $"[{string.Join(", ", Elements)}]";
        }

        // Address of the first element of nonempty immutable static storage.
        // ElementType describes the emitted array, independently of the view
        // through which its address is used. Empty arrays have no such address.
        public sealed record ArrayPointer(TypeExpression ElementType, Array Target) : ConstValue;

        public static bool TryAdd(ConstValue left, ConstValue right, out ConstValue? result)
        {
            if (TryWideArithmetic(TokenKind.Plus, left, right, out result)) return true;
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value + ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                result = new UInteger(lui.Value + rui.Value);
                return true;
            }
            if (left is Float lf && right is Float rf)
            {
                result = new Float(lf.Value + rf.Value);
                return true;
            }
            return false;
        }

        public static bool TrySubtract(ConstValue left, ConstValue right, out ConstValue? result)
        {
            if (TryWideArithmetic(TokenKind.Minus, left, right, out result)) return true;
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value - ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                result = new UInteger(lui.Value - rui.Value);
                return true;
            }
            if (left is Float lf && right is Float rf)
            {
                result = new Float(lf.Value - rf.Value);
                return true;
            }
            return false;
        }

        public static bool TryMultiply(ConstValue left, ConstValue right, out ConstValue? result)
        {
            if (TryWideArithmetic(TokenKind.Star, left, right, out result)) return true;
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value * ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                result = new UInteger(lui.Value * rui.Value);
                return true;
            }
            if (left is Float lf && right is Float rf)
            {
                result = new Float(lf.Value * rf.Value);
                return true;
            }
            return false;
        }

        public static bool TryDivide(ConstValue left, ConstValue right, out ConstValue? result, out string? error)
        {
            result = null;
            error = null;
            if (TryWideOperands(left, right, out var a, out var b))
            {
                if (b.IsZero) { error = "Division by zero"; return false; }
                result = new WideInteger(a / b);
                return true;
            }
            if (left is Integer li && right is Integer ri)
            {
                if (ri.Value == 0)
                {
                    error = "Division by zero";
                    return false;
                }
                result = new Integer(li.Value / ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                if (rui.Value == 0)
                {
                    error = "Division by zero";
                    return false;
                }
                result = new UInteger(lui.Value / rui.Value);
                return true;
            }
            if (left is Float lf && right is Float rf)
            {
                result = new Float(lf.Value / rf.Value);
                return true;
            }
            return false;
        }

        public static bool TryModulo(ConstValue left, ConstValue right, out ConstValue? result, out string? error)
        {
            result = null;
            error = null;
            if (TryWideOperands(left, right, out var a, out var b))
            {
                if (b.IsZero) { error = "Modulo by zero"; return false; }
                result = new WideInteger(a % b);
                return true;
            }
            if (left is Float lf && right is Float rf)
            {
                result = new Float(lf.Value % rf.Value);
                return true;
            }
            if (left is Integer li && right is Integer ri)
            {
                if (ri.Value == 0)
                {
                    error = "Modulo by zero";
                    return false;
                }
                result = new Integer(li.Value % ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                if (rui.Value == 0)
                {
                    error = "Modulo by zero";
                    return false;
                }
                result = new UInteger(lui.Value % rui.Value);
                return true;
            }
            return false;
        }

        public static bool TryEqual(ConstValue left, ConstValue right, out bool result)
        {
            if (TryWideOperands(left, right, out var wideLeft, out var wideRight))
            {
                result = wideLeft == wideRight;
                return true;
            }
            if (left is ArrayPointer a && right is ArrayPointer b)
            {
                result = Equals(a, b);
                return true;
            }
            if (left is ArrayPointer && right is Integer { Value: 0 } || left is Integer { Value: 0 } && right is ArrayPointer)
            {
                result = false;
                return true;
            }
            if (left is Integer li && right is Integer ri)
            {
                result = li.Value == ri.Value;
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                result = lui.Value == rui.Value;
                return true;
            }
            if (left is Float lf && right is Float rf)
            {
                result = lf.Value == rf.Value;
                return true;
            }
            if (left is Boolean lb && right is Boolean rb)
            {
                result = lb.Value == rb.Value;
                return true;
            }
            if (left is Char lc && right is Char rc)
            {
                result = lc.Value == rc.Value;
                return true;
            }
            result = false;
            return false;
        }

        public static bool TryCompare(ConstValue left, ConstValue right, out int comparison)
        {
            comparison = 0;
            if (TryWideOperands(left, right, out var a, out var b))
            {
                comparison = a.CompareTo(b);
                return true;
            }
            if (left is Integer li && right is Integer ri)
            {
                comparison = li.Value.CompareTo(ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                comparison = lui.Value.CompareTo(rui.Value);
                return true;
            }
            if (left is Float lf && right is Float rf)
            {
                comparison = lf.Value.CompareTo(rf.Value);
                return true;
            }
            if (left is Char lc && right is Char rc)
            {
                comparison = unchecked((sbyte)lc.Value).CompareTo(unchecked((sbyte)rc.Value));
                return true;
            }
            return false;
        }

        public static bool TryBitwiseAnd(ConstValue left, ConstValue right, out ConstValue? result)
        {
            if (TryWideArithmetic(TokenKind.Ampersand, left, right, out result)) return true;
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value & ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                result = new UInteger(lui.Value & rui.Value);
                return true;
            }
            if (left is Boolean lb && right is Boolean rb)
            {
                result = new Boolean(lb.Value & rb.Value);
                return true;
            }
            return false;
        }

        public static bool TryBitwiseOr(ConstValue left, ConstValue right, out ConstValue? result)
        {
            if (TryWideArithmetic(TokenKind.Pipe, left, right, out result)) return true;
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value | ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                result = new UInteger(lui.Value | rui.Value);
                return true;
            }
            if (left is Boolean lb && right is Boolean rb)
            {
                result = new Boolean(lb.Value | rb.Value);
                return true;
            }
            return false;
        }

        public static bool TryBitwiseXor(ConstValue left, ConstValue right, out ConstValue? result)
        {
            if (TryWideArithmetic(TokenKind.Caret, left, right, out result)) return true;
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value ^ ri.Value);
                return true;
            }
            if (left is UInteger lui && right is UInteger rui)
            {
                result = new UInteger(lui.Value ^ rui.Value);
                return true;
            }
            if (left is Boolean lb && right is Boolean rb)
            {
                result = new Boolean(lb.Value ^ rb.Value);
                return true;
            }
            return false;
        }

        public static bool TryShiftLeft(ConstValue left, ConstValue right, out ConstValue? result)
        {
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value << (int)ri.Value);
                return true;
            }
            if (left is UInteger lui && right is Integer ri2)
            {
                result = new UInteger(lui.Value << (int)ri2.Value);
                return true;
            }
            return false;
        }

        public static bool TryShiftRight(ConstValue left, ConstValue right, out ConstValue? result)
        {
            result = null;
            if (left is Integer li && right is Integer ri)
            {
                result = new Integer(li.Value >> (int)ri.Value);
                return true;
            }
            if (left is UInteger lui && right is Integer ri2)
            {
                result = new UInteger(lui.Value >> (int)ri2.Value);
                return true;
            }
            return false;
        }

        public static bool TryNegate(ConstValue operand, out ConstValue? result)
        {
            result = null;
            if (operand is WideInteger wide)
            {
                result = new WideInteger(-wide.Value);
                return true;
            }
            if (operand is Integer li)
            {
                result = new Integer(-li.Value);
                return true;
            }
            if (operand is Float lf)
            {
                result = new Float(-lf.Value);
                return true;
            }
            return false;
        }

        public static bool TryLogicalNot(ConstValue operand, out ConstValue? result)
        {
            result = null;
            if (operand is Boolean b)
            {
                result = new Boolean(!b.Value);
                return true;
            }
            return false;
        }

        public static bool TryBitwiseNot(ConstValue operand, out ConstValue? result)
        {
            result = null;
            if (operand is Integer li)
            {
                result = new Integer(~li.Value);
                return true;
            }
            if (operand is UInteger lui)
            {
                result = new UInteger(~lui.Value);
                return true;
            }
            return false;
        }
    }
}
