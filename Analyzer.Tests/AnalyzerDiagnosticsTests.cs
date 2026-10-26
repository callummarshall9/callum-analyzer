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

            namespace Demo.Helpers
            {
                public sealed class Helper
                {
                    public int Add(int value) => value + 1;
                }
            }

            public static class StringExtensions
            {
                public static bool IsLong(this string value) => value.Length > 10;
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
        for (var ruleNumber = 1; ruleNumber <= 16; ruleNumber++)
        {
            Assert.Contains($"RAG{ruleNumber:000}", diagnosticIds);
        }
    }

    [Fact]
    public async Task ReportsFilesOutsideArchitectureLayerFoldersExceptProgram()
    {
        var files = new[]
        {
            ("src/Exposures/Exposure.cs", "public sealed class Exposure { }"),
            ("src/Models/Widget.cs", "public sealed class Widget { }"),
            ("src/Services/WidgetService.cs", "public sealed class WidgetService { }"),
            ("src/Brokers/WidgetBroker.cs", "public sealed class WidgetBroker { }"),
            ("src/Helpers/WidgetHelper.cs", "public sealed class WidgetHelper { }"),
            ("src/Program.cs", "public static class Program { }")
        };
        var syntaxTrees = files.Select(file =>
            CSharpSyntaxTree.ParseText(
                file.Item2,
                new CSharpParseOptions(LanguageVersion.Latest),
                file.Item1));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureGuidelinesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "RAG024"));

        Assert.Equal("src/Helpers/WidgetHelper.cs", diagnostic.Location.SourceTree!.FilePath);
    }

    [Fact]
    public async Task ModelsAllowDataPropertiesButReportConstructorsAndLogic()
    {
        var files = new[]
        {
            ("src/Models/Widget.cs", """
                public sealed class Widget
                {
                    public int Id { get; set; }
                    public string Name { get; init; }
                    public Widget() { }
                    public int GetId() => Id;
                    public int Calculated => Id + 1;
                    public string Value;
                }
                """),
            ("src/Models/WidgetRecord.cs", """
                public record struct WidgetRecord(int Id)
                {
                    public string Name { get; init; }
                }
                """)
        };
        var syntaxTrees = files.Select(file =>
            CSharpSyntaxTree.ParseText(
                file.Item2,
                new CSharpParseOptions(LanguageVersion.Latest),
                file.Item1));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureGuidelinesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var modelDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == "RAG025").ToArray();

        Assert.Equal(4, modelDiagnostics.Length);
        Assert.All(modelDiagnostics, diagnostic =>
            Assert.Equal("src/Models/Widget.cs", diagnostic.Location.SourceTree!.FilePath));
    }

    [Fact]
    public async Task ReportsEnumsOutsideModelsAndAllowsNestedModelFolders()
    {
        var files = new[]
        {
            ("src/Shared/Status.cs", "public enum Status { Active, Inactive }"),
            ("src/Models/Enums/OrderStatus.cs", "public enum OrderStatus { New, Complete }"),
            ("src/Models/OrderType.cs", "public enum OrderType { Standard, Express }")
        };
        var syntaxTrees = files.Select(file =>
            CSharpSyntaxTree.ParseText(
                file.Item2,
                new CSharpParseOptions(LanguageVersion.Latest),
                file.Item1));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureGuidelinesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "RAG026"));

        Assert.Equal("src/Shared/Status.cs", diagnostic.Location.SourceTree!.FilePath);
    }

    [Fact]
    public async Task DoesNotReportGenericUsingAsUnused()
    {
        const string source = """
            using System.Collections.Immutable;
            using System.Text.RegularExpressions;

            public sealed class Sample
            {
                public ImmutableArray<int> Values => ImmutableArray<int>.Empty;
                public bool Matches(string input) => Regex.IsMatch(input, "pattern");
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

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "RAG005");
    }

    [Fact]
    public async Task AllowsServiceBusinessLogicAndDirectExposurePassThrough()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Demo.Services
            {
                public interface IWidgetService
                {
                    Task<int> GetAsync();
                }

                public sealed class WidgetService : IWidgetService
                {
                    public Task<int> GetAsync() => Task.FromResult(1);
                    public int Calculate() => 1 + 1;
                }
            }

            namespace Demo.Brokers
            {
                public sealed class WidgetBroker
                {
                    public int Calculate() => 1 + 1;
                }
            }

            namespace Demo.Exposures
            {
                public sealed class WidgetExposure
                {
                    private readonly Demo.Services.IWidgetService service;

                    public WidgetExposure(Demo.Services.IWidgetService service)
                    {
                        this.service = service;
                    }

                    public Task<int> GetAsync() => service.GetAsync();
                }
            }

            public static class WidgetServiceExtensions
            {
                public static Task<int> Forward(this Demo.Services.IWidgetService service) => service.GetAsync();
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

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "RAG012" or "RAG014" or "RAG015" or "RAG016");
    }

    [Fact]
    public async Task ReportsStandardServiceHierarchyAndDependencyViolations()
    {
        const string source = """
            namespace Demo.Services
            {
                public interface IWidgetService { }
                public interface IOtherService { }
                public interface IWidgetProcessingService { }
                public interface IWidgetOrchestrationService { }
                public interface IWidgetCoordinationService { }

                public sealed class SingleOrchestrationService
                {
                    public SingleOrchestrationService(IWidgetService service) { }
                }

                public sealed class WrongCoordinationService
                {
                    public WrongCoordinationService(IWidgetService service) { }
                }

                public sealed class MixedOrchestrationService
                {
                    public MixedOrchestrationService(IWidgetService foundation, IWidgetProcessingService processing) { }
                }

                public sealed class BrokerProcessingService
                {
                    public BrokerProcessingService(IWidgetService service, Demo.Brokers.IStorageBroker broker) { }
                }

                public sealed class WrongEntityProcessingService
                {
                    public WrongEntityProcessingService(IOtherService service) { }
                }

                public sealed class TwoBrokerService
                {
                    public TwoBrokerService(Demo.Brokers.IStorageBroker storage, Demo.Brokers.IEmailBroker email) { }
                }

                public sealed class MixedAggregationService
                {
                    public MixedAggregationService(IWidgetOrchestrationService orchestration, IWidgetCoordinationService coordination) { }
                }
            }

            namespace Demo.Brokers
            {
                public interface IStorageBroker { }
                public interface IEmailBroker { }
            }
            """;

        var diagnostics = await GetServiceHierarchyDiagnostics(source);

        foreach (var ruleNumber in Enumerable.Range(17, 6))
        {
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == $"RAG{ruleNumber:000}");
        }
    }

    [Fact]
    public async Task AllowsStandardServiceDependencyPatterns()
    {
        const string source = """
            namespace Demo.Services
            {
                public interface IWidgetService { }
                public interface IOtherService { }
                public interface IWidgetProcessingService { }
                public interface IWidgetOrchestrationService { }
                public interface IOtherOrchestrationService { }
                public interface IWidgetCoordinationService { }
                public interface IOtherCoordinationService { }
                public interface IWidgetManagementService { }
                public interface IOtherManagementService { }
                public interface IStorageBroker { }
            }

            namespace Demo.UtilityBrokers
            {
                public interface ILoggingBroker { }
            }

            namespace Demo.Services.Implementations
            {
                public sealed class WidgetService
                {
                    public WidgetService(Demo.Services.IStorageBroker broker, Demo.UtilityBrokers.ILoggingBroker logging) { }
                }

                public sealed class WidgetProcessingService
                {
                    public WidgetProcessingService(Demo.Services.IWidgetService service, Demo.UtilityBrokers.ILoggingBroker logging) { }
                }

                public sealed class WidgetOrchestrationService
                {
                    public WidgetOrchestrationService(
                        Demo.Services.IWidgetService first,
                        Demo.Services.IOtherService second,
                        Demo.UtilityBrokers.ILoggingBroker logging) { }
                }

                public sealed class WidgetCoordinationService
                {
                    public WidgetCoordinationService(
                        Demo.Services.IWidgetOrchestrationService first,
                        Demo.Services.IOtherOrchestrationService second) { }
                }

                public sealed class WidgetManagementService
                {
                    public WidgetManagementService(
                        Demo.Services.IWidgetCoordinationService first,
                        Demo.Services.IOtherCoordinationService second) { }
                }

                public sealed class WidgetUberManagementService
                {
                    public WidgetUberManagementService(
                        Demo.Services.IWidgetManagementService first,
                        Demo.Services.IOtherManagementService second) { }
                }

                public sealed class WidgetAggregationService
                {
                    public WidgetAggregationService(
                        Demo.Services.IWidgetOrchestrationService first,
                        Demo.Services.IOtherOrchestrationService second,
                        Demo.UtilityBrokers.ILoggingBroker logging) { }
                }
            }
            """;

        var diagnostics = await GetServiceHierarchyDiagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsStaticMethodsOnServicesOnly()
    {
        const string source = """
            namespace Demo.Services
            {
                public sealed class WidgetService
                {
                    public static int Calculate() => 1;
                    public int Get() => 1;
                }
            }

            namespace Demo.Helpers
            {
                public static class MathHelper
                {
                    public static int Calculate() => 1;
                }
            }
            """;

        var diagnostics = await GetServiceHierarchyDiagnostics(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("RAG023", diagnostic.Id);
        Assert.Contains(
            "Calculate",
            diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan),
            StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetServiceHierarchyDiagnostics(string source)
    {
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
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ServiceHierarchyAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
