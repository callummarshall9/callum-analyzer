using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynArchitectureGuidelines;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ArchitectureGuidelinesCodeFixProvider)), Shared]
public sealed class ArchitectureGuidelinesCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableArray<string> FixableIds = ImmutableArray.Create(
        "RAG001",
        "RAG003",
        "RAG005",
        "RAG006");

    public override ImmutableArray<string> FixableDiagnosticIds => FixableIds;

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        switch (diagnostic.Id)
        {
            case "RAG001":
                var alias = node.FirstAncestorOrSelf<AliasQualifiedNameSyntax>();
                if (alias?.Alias.Identifier.ValueText == "global")
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Remove global:: qualifier",
                            cancellationToken => ReplaceNodeAsync(context.Document, root, alias, alias.Name.WithTriviaFrom(alias), cancellationToken),
                            equivalenceKey: "RemoveGlobalQualifier"),
                        diagnostic);
                }
                break;

            case "RAG003":
                var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation?.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.ValueText == "ConfigureAwait" &&
                    invocation.ArgumentList.Arguments.Count == 1)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Remove ConfigureAwait(false)",
                            cancellationToken => ReplaceNodeAsync(context.Document, root, invocation, memberAccess.Expression.WithTriviaFrom(invocation), cancellationToken),
                            equivalenceKey: "RemoveConfigureAwaitFalse"),
                        diagnostic);
                }
                break;

            case "RAG005":
                var usingDirective = node.FirstAncestorOrSelf<UsingDirectiveSyntax>();
                if (usingDirective is not null)
                {
                    var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                    if (semanticModel is not null &&
                        !ArchitectureGuidelinesAnalyzer.IsUsingDirectiveUsed(
                            usingDirective,
                            (CompilationUnitSyntax)root,
                            semanticModel,
                            context.CancellationToken))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove unused using directive",
                                cancellationToken => RemoveNodeAsync(context.Document, root, usingDirective, cancellationToken),
                                equivalenceKey: "RemoveUnusedUsing"),
                            diagnostic);
                    }
                    else if (usingDirective.Parent is SyntaxNode usingParent)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Sort using directives",
                                cancellationToken => SortUsingsAsync(context.Document, root, usingParent, cancellationToken),
                                equivalenceKey: "SortUsingDirectives"),
                            diagnostic);
                    }
                }
                break;

            case "RAG006":
                var catchClause = node.FirstAncestorOrSelf<CatchClauseSyntax>();
                if (catchClause is { Block.Statements.Count: 0 })
                {
                    var updatedCatch = catchClause.WithBlock(
                        catchClause.Block!.WithStatements(
                            SyntaxFactory.SingletonList<StatementSyntax>(
                                SyntaxFactory.ThrowStatement())));
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Rethrow the caught exception",
                            cancellationToken => ReplaceNodeAsync(context.Document, root, catchClause, updatedCatch, cancellationToken),
                            equivalenceKey: "RethrowCaughtException"),
                        diagnostic);
                }
                break;
        }
    }

    private static Task<Document> ReplaceNodeAsync(
        Document document,
        SyntaxNode root,
        SyntaxNode oldNode,
        SyntaxNode newNode,
        CancellationToken cancellationToken) =>
        Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(oldNode, newNode)));

    private static Task<Document> RemoveNodeAsync(
        Document document,
        SyntaxNode root,
        SyntaxNode node,
        CancellationToken cancellationToken) =>
        Task.FromResult(document.WithSyntaxRoot(root.RemoveNode(node, SyntaxRemoveOptions.KeepExteriorTrivia)!));

    private static Task<Document> SortUsingsAsync(
        Document document,
        SyntaxNode root,
        SyntaxNode parent,
        CancellationToken cancellationToken)
    {
        var usings = parent.ChildNodes().OfType<UsingDirectiveSyntax>()
            .OrderBy(directive => directive.Name?.ToString(), StringComparer.Ordinal)
            .ToArray();
        var sortedList = SyntaxFactory.List(usings);
        var updatedParent = parent switch
        {
            CompilationUnitSyntax compilationUnit => compilationUnit.WithUsings(sortedList),
            BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.WithUsings(sortedList),
            _ => parent
        };
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(parent, updatedParent)));
    }
}
