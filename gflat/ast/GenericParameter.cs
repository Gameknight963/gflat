using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class GenericParameter
    {
        public string Name { get; }
        public TypeExpression? Constraint { get; }
        public int Line { get; }

        public GenericParameter(string name, TypeExpression? constraint, int line)
        {
            Name = name;
            Constraint = constraint;
            Line = line;
        }
    }
}
