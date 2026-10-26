using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynArchitectureGuidelines;
using Xunit;

namespace RoslynArchitectureGuidelines.Tests;

public sealed class AnalyzerCodeFixTests
{
    [Fact]
    public async Task RemovesGlobalQualifier()
    {
        const string source = "public sealed class Sample { public string Value => global::System.String.Empty; }";

        var fixedSource = await ApplyFixAsync(source, "RAG001", "Remove global:: qualifier");

        Assert.Contains("System.String.Empty", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("global::", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovesConfigureAwaitFalse()
    {
        const string source = """
            using System.Threading.Tasks;
            public sealed class Sample
            {
                public Task Run() => Task.Delay(1).ConfigureAwait(false);
            }
            """;

        var fixedSource = await ApplyFixAsync(source, "RAG003", "Remove ConfigureAwait(false)");

        Assert.DoesNotContain("ConfigureAwait", fixedSource, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(1)", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SortsUsingDirectives()
    {
        const string source = """
            using System.Threading.Tasks;
            using System;
            public sealed class Sample
            {
                public Task Run() => Task.CompletedTask;
                public void Write() => Console.WriteLine("value");
            }
            """;

        var fixedSource = await ApplyFixAsync(source, "RAG005", "Sort using directives");

        Assert.True(
            fixedSource.IndexOf("using System;", StringComparison.Ordinal) <
            fixedSource.IndexOf("using System.Threading.Tasks;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RethrowsFromEmptyCatch()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    try { }
                    catch { }
                }
            }
            """;

        var fixedSource = await ApplyFixAsync(source, "RAG006", "Rethrow the caught exception");

        Assert.Contains("catch", fixedSource, StringComparison.Ordinal);
        Assert.Contains("throw;", fixedSource, StringComparison.Ordinal);
    }

    private static async Task<string> ApplyFixAsync(string source, string diagnosticId, string actionTitle)
    {
        using var workspace = new AdhocWorkspace();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path));
        var project = workspace.AddProject("CodeFixTest", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(references);
        var document = workspace.AddDocument(project.Id, "Sample.cs", SourceText.From(source));
        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureGuidelinesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var targetDiagnostics = diagnostics.Where(candidate => candidate.Id == diagnosticId).ToArray();
        Assert.True(
            targetDiagnostics.Length == 1,
            $"Expected one {diagnosticId}; found: {string.Join(", ", diagnostics.Select(candidate => candidate.Id))}");
        var diagnostic = targetDiagnostics[0];
        var actions = new List<CodeAction>();
        var provider = new ArchitectureGuidelinesCodeFixProvider();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None));

        var actionToApply = Assert.Single(actions, action => action.Title == actionTitle);
        var operations = await actionToApply.GetOperationsAsync(CancellationToken.None);
        var changedSolution = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id);
        Assert.NotNull(changedDocument);
        return (await changedDocument!.GetTextAsync()).ToString();
    }
}
