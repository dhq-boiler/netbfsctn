using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.Source.Rewriters;

public class NameObfuscationRewriter
    : IObfuscationTechnique<(List<SyntaxTree> Trees, CSharpCompilation Compilation)>
{
    public string Name => "名前難読化 (Source)";

    public void Apply(
        (List<SyntaxTree> Trees, CSharpCompilation Compilation) state,
        ObfuscationContext context,
        ObfuscationResult result)
    {
        // パス1: シンボルマップ構築
        var symbolMap = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

        foreach (var tree in state.Trees)
        {
            var model = state.Compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var decl in root.DescendantNodes())
            {
                ISymbol? symbol = decl switch
                {
                    MethodDeclarationSyntax m => model.GetDeclaredSymbol(m),
                    VariableDeclaratorSyntax v => model.GetDeclaredSymbol(v),
                    ParameterSyntax p => model.GetDeclaredSymbol(p),
                    LocalDeclarationStatementSyntax => null,
                    ClassDeclarationSyntax c => model.GetDeclaredSymbol(c),
                    _ => null
                };

                if (symbol == null) continue;
                if (symbolMap.ContainsKey(symbol)) continue;
                if (!ShouldRename(symbol)) continue;

                var newName = context.NameGenerator.Next();
                symbolMap[symbol] = newName;
                context.Logger.Verbose($"シンボル: {symbol.Name} -> {newName}");
                result.RenamedSymbols++;
            }
        }

        // パス2: 全ツリーを書き換え
        for (var i = 0; i < state.Trees.Count; i++)
        {
            var tree = state.Trees[i];
            var model = state.Compilation.GetSemanticModel(tree);
            var rewriter = new NameRewriterVisitor(model, symbolMap);
            var newRoot = rewriter.Visit(tree.GetRoot());
            state.Trees[i] = newRoot.SyntaxTree;
        }
    }

    private static bool ShouldRename(ISymbol symbol)
    {
        // public メンバーはスキップ
        if (symbol.DeclaredAccessibility == Accessibility.Public)
            return false;

        // Main メソッドはスキップ
        if (symbol is IMethodSymbol { Name: "Main" })
            return false;

        // コンストラクタはスキップ
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor })
            return false;

        // virtual/override はスキップ
        if (symbol is IMethodSymbol { IsVirtual: true } or IMethodSymbol { IsOverride: true })
            return false;

        // インターフェース実装はスキップ
        if (symbol is IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            if (containingType != null)
            {
                foreach (var iface in containingType.AllInterfaces)
                {
                    foreach (var member in iface.GetMembers())
                    {
                        var impl = containingType.FindImplementationForInterfaceMember(member);
                        if (SymbolEqualityComparer.Default.Equals(impl, symbol))
                            return false;
                    }
                }
            }
        }

        // 対象: private/internal メソッド、変数、パラメータ、private クラス
        return symbol is IMethodSymbol
            or ILocalSymbol
            or IParameterSymbol
            or IFieldSymbol
            or INamedTypeSymbol { DeclaredAccessibility: not Accessibility.Public };
    }

    private class NameRewriterVisitor : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        private readonly Dictionary<ISymbol, string> _symbolMap;

        public NameRewriterVisitor(SemanticModel model, Dictionary<ISymbol, string> symbolMap)
        {
            _model = model;
            _symbolMap = symbolMap;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol != null && _symbolMap.TryGetValue(symbol, out var newName))
            {
                return node.WithIdentifier(
                    SyntaxFactory.Identifier(newName)
                        .WithTriviaFrom(node.Identifier));
            }
            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            var result = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            if (symbol != null && _symbolMap.TryGetValue(symbol, out var newName))
            {
                result = result.WithIdentifier(
                    SyntaxFactory.Identifier(newName)
                        .WithTriviaFrom(result.Identifier));
            }
            return result;
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            var result = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node)!;
            if (symbol != null && _symbolMap.TryGetValue(symbol, out var newName))
            {
                result = result.WithIdentifier(
                    SyntaxFactory.Identifier(newName)
                        .WithTriviaFrom(result.Identifier));
            }
            return result;
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            var result = (ParameterSyntax)base.VisitParameter(node)!;
            if (symbol != null && _symbolMap.TryGetValue(symbol, out var newName))
            {
                result = result.WithIdentifier(
                    SyntaxFactory.Identifier(newName)
                        .WithTriviaFrom(result.Identifier));
            }
            return result;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            var result = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            if (symbol != null && _symbolMap.TryGetValue(symbol, out var newName))
            {
                result = result.WithIdentifier(
                    SyntaxFactory.Identifier(newName)
                        .WithTriviaFrom(result.Identifier));
            }
            return result;
        }
    }
}
