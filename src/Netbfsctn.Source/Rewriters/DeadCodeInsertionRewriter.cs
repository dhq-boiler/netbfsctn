using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.Source.Rewriters;

public class DeadCodeInsertionRewriter
    : IObfuscationTechnique<(List<SyntaxTree> Trees, CSharpCompilation Compilation)>
{
    public string Name => "デッドコード挿入 (Source)";

    public void Apply(
        (List<SyntaxTree> Trees, CSharpCompilation Compilation) state,
        ObfuscationContext context,
        ObfuscationResult result)
    {
        for (var i = 0; i < state.Trees.Count; i++)
        {
            var tree = state.Trees[i];
            var rewriter = new DeadCodeVisitor(context, result);
            var newRoot = rewriter.Visit(tree.GetRoot());
            state.Trees[i] = newRoot.SyntaxTree;
        }
    }

    private class DeadCodeVisitor : CSharpSyntaxRewriter
    {
        private readonly ObfuscationContext _context;
        private readonly ObfuscationResult _result;
        private int _dummyCounter;

        public DeadCodeVisitor(ObfuscationContext context, ObfuscationResult result)
        {
            _context = context;
            _result = result;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;

            if (visited.Body == null)
                return visited;
            if (visited.Body.Statements.Count < 1)
                return visited;

            // メソッド先頭に不透明述語付きデッドコードブロックを挿入
            var deadCode = CreateDeadCodeBlock();
            var newStatements = visited.Body.Statements.Insert(0, deadCode);

            _result.InsertedDeadCodeBlocks++;
            _context.Logger.Verbose($"デッドコード挿入: {node.Identifier.Text}");

            return visited.WithBody(visited.Body.WithStatements(newStatements));
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            // ダミーメソッドを追加
            var dummyMethod = CreateDummyMethod();
            var newMembers = visited.Members.Add(dummyMethod);

            _result.InsertedDeadCodeBlocks++;
            _context.Logger.Verbose($"ダミーメソッド追加: {node.Identifier.Text}");

            return visited.WithMembers(newMembers);
        }

        private StatementSyntax CreateDeadCodeBlock()
        {
            // if ((int.MaxValue * 0) != 0) { var x = 42; Console.WriteLine(x); }
            return SyntaxFactory.ParseStatement(
                "if ((int.MaxValue * 0) != 0) { var __x = 42; System.Console.WriteLine(__x); }\n");
        }

        private MemberDeclarationSyntax CreateDummyMethod()
        {
            var name = _context.NameGenerator.Next();
            _dummyCounter++;

            return SyntaxFactory.ParseMemberDeclaration(
                $$"""
                private static int {{name}}()
                {
                    var a = {{_dummyCounter * 17}};
                    var b = {{_dummyCounter * 31}};
                    return a + b;
                }
                """)!;
        }
    }
}
