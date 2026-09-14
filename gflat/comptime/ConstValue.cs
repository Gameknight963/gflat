using System;
using System.Collections.Generic;
using System.Globalization;

namespace gflat.comptime
{
    public abstract record ConstValue
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

        public sealed record String(string Value) : ConstValue
        {
            public override string ToString() => Value;
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

        public static bool TryAdd(ConstValue left, ConstValue right, out ConstValue? result)
        {
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
            if (left is String ls && right is String rs)
            {
                result = new String(ls.Value + rs.Value);
                return true;
            }
            if (left is String ls2)
            {
                result = new String(ls2.Value + right.ToString());
                return true;
            }
            if (right is String rs2)
            {
                result = new String(left.ToString() + rs2.Value);
                return true;
            }
            return false;
        }

        public static bool TrySubtract(ConstValue left, ConstValue right, out ConstValue? result)
        {
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
                if (rf.Value == 0.0)
                {
                    error = "Division by zero";
                    return false;
                }
                result = new Float(lf.Value / rf.Value);
                return true;
            }
            return false;
        }

        public static bool TryModulo(ConstValue left, ConstValue right, out ConstValue? result, out string? error)
        {
            result = null;
            error = null;
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
                result = Math.Abs(lf.Value - rf.Value) < double.Epsilon;
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
            if (left is String ls && right is String rs)
            {
                result = ls.Value == rs.Value;
                return true;
            }
            result = false;
            return false;
        }

        public static bool TryCompare(ConstValue left, ConstValue right, out int comparison)
        {
            comparison = 0;
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
                comparison = lc.Value.CompareTo(rc.Value);
                return true;
            }
            if (left is String ls && right is String rs)
            {
                comparison = string.Compare(ls.Value, rs.Value, StringComparison.Ordinal);
                return true;
            }
            return false;
        }

        public static bool TryBitwiseAnd(ConstValue left, ConstValue right, out ConstValue? result)
        {
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
