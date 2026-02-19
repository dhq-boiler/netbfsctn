using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Netbfsctn.Core.Encryption;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.Source.Rewriters;

public class StringEncryptionRewriter
    : IObfuscationTechnique<(List<SyntaxTree> Trees, CSharpCompilation Compilation)>
{
    public string Name => "文字列暗号化 (Source)";

    public void Apply(
        (List<SyntaxTree> Trees, CSharpCompilation Compilation) state,
        ObfuscationContext context,
        ObfuscationResult result)
    {
        var encryptor = new XorStringEncryptor();
        var hasStrings = false;

        for (var i = 0; i < state.Trees.Count; i++)
        {
            var tree = state.Trees[i];
            var rewriter = new StringRewriterVisitor(encryptor, context, result);
            var newRoot = rewriter.Visit(tree.GetRoot());
            if (rewriter.Modified)
            {
                state.Trees[i] = newRoot.SyntaxTree;
                hasStrings = true;
            }
        }

        // ヘルパークラスのソースを追加
        if (hasStrings)
        {
            var helperSource = GenerateHelperClass();
            var helperTree = CSharpSyntaxTree.ParseText(helperSource);
            state.Trees.Add(helperTree);
        }
    }

    private static string GenerateHelperClass()
    {
        return """
            using System.Text;

            internal static class __StringHelper
            {
                internal static string D(byte[] d, byte[] k)
                {
                    var r = new byte[d.Length];
                    for (var i = 0; i < d.Length; i++)
                        r[i] = (byte)(d[i] ^ k[i % k.Length]);
                    return Encoding.UTF8.GetString(r);
                }
            }
            """;
    }

    private class StringRewriterVisitor : CSharpSyntaxRewriter
    {
        private readonly XorStringEncryptor _encryptor;
        private readonly ObfuscationContext _context;
        private readonly ObfuscationResult _result;

        public bool Modified { get; private set; }

        public StringRewriterVisitor(
            XorStringEncryptor encryptor,
            ObfuscationContext context,
            ObfuscationResult result)
        {
            _encryptor = encryptor;
            _context = context;
            _result = result;
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.Kind() != SyntaxKind.StringLiteralExpression)
                return base.VisitLiteralExpression(node);

            var value = node.Token.ValueText;
            if (string.IsNullOrEmpty(value))
                return base.VisitLiteralExpression(node);

            var key = _encryptor.GenerateKey();
            var encrypted = _encryptor.Encrypt(value, key);

            var encryptedArrayExpr = CreateByteArrayExpression(encrypted);
            var keyArrayExpr = CreateByteArrayExpression(key);

            var callExpr = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("__StringHelper"),
                    SyntaxFactory.IdentifierName("D")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(new[]
                    {
                        SyntaxFactory.Argument(encryptedArrayExpr),
                        SyntaxFactory.Argument(keyArrayExpr)
                    })));

            Modified = true;
            _result.EncryptedStrings++;
            _context.Logger.Verbose($"暗号化: \"{Truncate(value, 30)}\"");

            return callExpr.WithTriviaFrom(node);
        }

        private static ExpressionSyntax CreateByteArrayExpression(byte[] data)
        {
            var elements = data.Select(b =>
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(b)));

            return SyntaxFactory.ImplicitArrayCreationExpression(
                SyntaxFactory.InitializerExpression(
                    SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SeparatedList<ExpressionSyntax>(elements)));
        }

        private static string Truncate(string s, int maxLen)
            => s.Length <= maxLen ? s : s[..maxLen] + "...";
    }
}
