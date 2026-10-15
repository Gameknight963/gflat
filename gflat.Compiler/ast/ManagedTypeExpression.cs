using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ManagedTypeExpression : TypeExpression
    {
        public TypeExpression Inner { get; }
        public TypeExpression? Collector { get; }
        public bool UsesDefaultCollector { get; internal set; }
        public bool IsNullable { get; }
        public bool IsReadOnly { get; }

        public ManagedTypeExpression(TypeExpression inner, bool isNullable, int line, bool isReadOnly = false, TypeExpression? collector = null) : base(line)
        {
            Collector = collector;
            UsesDefaultCollector = collector == null;
            Inner = isReadOnly ? TypeQualifiers.ReadOnly(inner) : inner;
            IsNullable = isNullable;
            IsReadOnly = isReadOnly || inner.IsReadOnlyValue;
        }

        public ManagedTypeExpression WithCollector(TypeExpression collector) =>
            new(Inner, IsNullable, Line, IsReadOnly, collector) { UsesDefaultCollector = UsesDefaultCollector, Span = Span };

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
