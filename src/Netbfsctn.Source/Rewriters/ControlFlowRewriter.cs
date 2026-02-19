using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.Source.Rewriters;

public class ControlFlowRewriter
    : IObfuscationTechnique<(List<SyntaxTree> Trees, CSharpCompilation Compilation)>
{
    public string Name => "制御フロー難読化 (Source)";

    public void Apply(
        (List<SyntaxTree> Trees, CSharpCompilation Compilation) state,
        ObfuscationContext context,
        ObfuscationResult result)
    {
        for (var i = 0; i < state.Trees.Count; i++)
        {
            var tree = state.Trees[i];
            var rewriter = new ControlFlowVisitor(context, result);
            var newRoot = rewriter.Visit(tree.GetRoot());
            state.Trees[i] = newRoot.SyntaxTree;
        }
    }

    private class ControlFlowVisitor : CSharpSyntaxRewriter
    {
        private readonly ObfuscationContext _context;
        private readonly ObfuscationResult _result;

        public ControlFlowVisitor(ObfuscationContext context, ObfuscationResult result)
        {
            _context = context;
            _result = result;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Body == null)
                return base.VisitMethodDeclaration(node);

            var statements = node.Body.Statements;
            if (statements.Count < 3)
                return base.VisitMethodDeclaration(node);

            _context.Logger.Verbose($"制御フロー変換: {node.Identifier.Text}");
            _result.ObfuscatedMethods++;

            // ステートマシン変換
            // 各ステートメントにステート番号を割り当て、while+switch で回す
            var stateVarName = "__s";
            var runVarName = "__r";

            var switchSections = new List<SwitchSectionSyntax>();

            for (var idx = 0; idx < statements.Count; idx++)
            {
                var stmt = statements[idx];
                var isLast = idx == statements.Count - 1;

                var caseStatements = new List<StatementSyntax>();
                caseStatements.Add(stmt);

                if (isLast)
                {
                    // 最後: ループ終了
                    caseStatements.Add(ParseStatement($"{runVarName} = false;"));
                }
                else
                {
                    // 次のステートへ遷移
                    caseStatements.Add(ParseStatement($"{stateVarName} = {idx + 1};"));
                }
                caseStatements.Add(SyntaxFactory.BreakStatement());

                var section = SyntaxFactory.SwitchSection(
                    SyntaxFactory.SingletonList<SwitchLabelSyntax>(
                        SyntaxFactory.CaseSwitchLabel(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.NumericLiteralExpression,
                                SyntaxFactory.Literal(idx)))),
                    SyntaxFactory.List(caseStatements));

                switchSections.Add(section);
            }

            var switchStmt = SyntaxFactory.SwitchStatement(
                SyntaxFactory.IdentifierName(stateVarName),
                SyntaxFactory.List(switchSections));

            var whileBody = SyntaxFactory.Block(switchStmt);
            var whileStmt = SyntaxFactory.WhileStatement(
                SyntaxFactory.IdentifierName(runVarName),
                whileBody);

            var newStatements = new List<StatementSyntax>
            {
                ParseStatement($"var {stateVarName} = 0;"),
                ParseStatement($"var {runVarName} = true;"),
                whileStmt
            };

            var newBody = node.Body.WithStatements(SyntaxFactory.List(newStatements));
            return node.WithBody(newBody);
        }

        private static StatementSyntax ParseStatement(string code)
            => SyntaxFactory.ParseStatement(code);
    }
}
