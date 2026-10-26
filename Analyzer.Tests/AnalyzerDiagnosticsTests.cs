using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynArchitectureGuidelines;
using Xunit;

namespace RoslynArchitectureGuidelines.Tests;

public sealed class AnalyzerDiagnosticsTests
{
    [Fact]
    public async Task ReportsAllGuidelineDiagnosticsForViolatingSource()
    {
        const string source = """
            using System;
            using System.Diagnostics;
            using System.Text;
            using System.Threading.Tasks;

            namespace Demo.Exposures
            {
                public sealed class Exposure
                {
                    public int Value { get; set; }
                }
            }

            namespace Demo.Services
            {
                public sealed class FirstEntity { }
                public sealed class SecondEntity { }

                public sealed class SampleService
                {
                    private readonly object dependency;

                    public SampleService(object dependency = null, Demo.Exposures.Exposure exposure = null)
                    {
                        this.dependency = dependency ?? new object();
                        Console.WriteLine("created");
                        void Local() { }
                    }

                    public FirstEntity First() => new FirstEntity();
                    public SecondEntity Second() => new SecondEntity();
                    public async Task Run() => await Task.Delay(1).ConfigureAwait(false);
                    public void Log() => Trace.WriteLine("message");
                    public void Catch()
                    {
                        try { }
                        catch { }
                    }

                    public global::System.String GlobalName => "";
                }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureGuidelinesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var diagnosticIds = diagnostics.Select(diagnostic => diagnostic.Id).ToImmutableHashSet(StringComparer.Ordinal);
        for (var ruleNumber = 1; ruleNumber <= 13; ruleNumber++)
        {
            Assert.Contains($"RAG{ruleNumber:000}", diagnosticIds);
        }
    }
}
