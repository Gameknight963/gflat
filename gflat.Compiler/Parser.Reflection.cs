using gflat.ast;

namespace gflat;

public partial class Parser
{
    private AstNode ParseSpliceOperand()
    {
        Expect(TokenKind.OpenBracket);
        Expect(TokenKind.Colon);
        AstNode value = ParseExpression();
        Expect(TokenKind.Colon);
        Expect(TokenKind.CloseBracket);
        return value;
    }
}
