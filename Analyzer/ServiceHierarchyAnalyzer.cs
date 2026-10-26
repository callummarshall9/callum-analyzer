// ---------------------------------------------------------------
// Copyright (c) SqlExecutionEngine contributors
// ---------------------------------------------------------------
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynArchitectureGuidelines;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServiceHierarchyAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Architecture";

    private static readonly DiagnosticDescriptor InvalidServiceDependency = Rule(
        "RAG017",
        "Follow the service hierarchy",
        "{0} services may depend on {1} services, not {2}");
    private static readonly DiagnosticDescriptor InvalidServiceDependencyCount = Rule(
        "RAG018",
        "Respect service dependency limits",
        "{0} services should have {1} service dependencies, but found {2}");
    private static readonly DiagnosticDescriptor MixedServiceLevels = Rule(
        "RAG019",
        "Keep service dependencies at one level",
        "{0} services must not mix dependencies from different service levels");
    private static readonly DiagnosticDescriptor InvalidBrokerDependency = Rule(
        "RAG020",
        "Keep broker dependencies within service limits",
        "{0} services may not depend on {1}");
    private static readonly DiagnosticDescriptor MismatchedProcessingService = Rule(
        "RAG021",
        "Match processing services to their foundation service",
        "Processing service '{0}' must depend on its corresponding foundation service, not '{1}'");
    private static readonly DiagnosticDescriptor MixedAggregationLevels = Rule(
        "RAG022",
        "Aggregate services from one variation",
        "Aggregation services must depend on services from one variation");
    private static readonly DiagnosticDescriptor NoStaticServiceMethods = Rule(
        "RAG023",
        "Keep service methods instance-based",
        "Service methods must be instance methods to support unit testing");

    private static readonly ImmutableArray<DiagnosticDescriptor> SupportedRules = ImmutableArray.Create(
        InvalidServiceDependency,
        InvalidServiceDependencyCount,
        MixedServiceLevels,
        InvalidBrokerDependency,
        MismatchedProcessingService,
        MixedAggregationLevels,
        NoStaticServiceMethods);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SupportedRules;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeServiceDependencies, SymbolKind.NamedType);
        context.RegisterSymbolAction(AnalyzeStaticServiceMethods, SymbolKind.Method);
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static void AnalyzeServiceDependencies(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class ||
            type.IsStatic ||
            GetServiceLevel(type.Name) is not { } serviceLevel)
        {
            return;
        }

        var constructors = type.InstanceConstructors
            .Where(constructor => !constructor.IsImplicitlyDeclared)
            .ToArray();
        if (constructors.Length == 0)
        {
            constructors = type.InstanceConstructors.ToArray();
        }
        if (constructors.Length == 0)
        {
            return;
        }

        var largestConstructor = constructors.Max(constructor => constructor.Parameters.Length);
        foreach (var constructor in constructors.Where(constructor =>
            constructor.Parameters.Length == largestConstructor))
        {
            AnalyzeConstructorDependencies(type, serviceLevel, constructor, context);
        }
    }

    private static void AnalyzeStaticServiceMethods(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.IsStatic &&
            !method.IsImplicitlyDeclared &&
            method.MethodKind == MethodKind.Ordinary &&
            GetServiceLevel(method.ContainingType.Name) is not null)
        {
            var location = method.Locations.FirstOrDefault(candidate => candidate.IsInSource);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(NoStaticServiceMethods, location));
            }
        }
    }

    private static void AnalyzeConstructorDependencies(
        INamedTypeSymbol type,
        ServiceLevel serviceLevel,
        IMethodSymbol constructor,
        SymbolAnalysisContext context)
    {
        var dependencies = constructor.Parameters
            .Select(parameter => (Parameter: parameter, Level: GetServiceLevel(parameter.Type.Name), IsBroker: IsBroker(parameter.Type)))
            .ToArray();
        var serviceDependencies = dependencies
            .Where(dependency => dependency.Level is not null)
            .ToArray();
        var brokerDependencies = dependencies
            .Where(dependency => dependency.IsBroker && !IsUtilityBroker(dependency.Parameter.Type))
            .ToArray();
        var location = constructor.Locations.FirstOrDefault(candidate => candidate.IsInSource)
            ?? type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (location is null)
        {
            return;
        }

        AnalyzeAllowedServiceLevels(serviceLevel, serviceDependencies, location, context);
        AnalyzeDependencyCount(serviceLevel, serviceDependencies.Length, location, context);
        AnalyzeMixedLevels(serviceLevel, serviceDependencies, location, context);
        AnalyzeBrokerDependencies(serviceLevel, brokerDependencies, location, context);
        AnalyzeProcessingEntity(type, serviceLevel, serviceDependencies, context);
    }

    private static void AnalyzeAllowedServiceLevels(
        ServiceLevel serviceLevel,
        (IParameterSymbol Parameter, ServiceLevel? Level, bool IsBroker)[] dependencies,
        Location location,
        SymbolAnalysisContext context)
    {
        foreach (var dependency in dependencies)
        {
            if (dependency.Level is not { } dependencyLevel ||
                IsAllowedDependency(serviceLevel, dependencyLevel))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                InvalidServiceDependency,
                dependency.Parameter.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? location,
                ToDisplayName(serviceLevel),
                GetExpectedServiceLevels(serviceLevel),
                ToDisplayName(dependencyLevel)));
        }
    }

    private static void AnalyzeDependencyCount(
        ServiceLevel serviceLevel,
        int serviceDependencyCount,
        Location location,
        SymbolAnalysisContext context)
    {
        var expectation = serviceLevel switch
        {
            ServiceLevel.Processing => "exactly one Foundation",
            ServiceLevel.Orchestration or ServiceLevel.Coordination or
                ServiceLevel.Management or ServiceLevel.UberManagement => "two or three lower-level",
            _ => null
        };
        if (expectation is null)
        {
            return;
        }

        var isValid = serviceLevel == ServiceLevel.Processing
            ? serviceDependencyCount == 1
            : serviceDependencyCount is 2 or 3;
        if (!isValid)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidServiceDependencyCount,
                location,
                ToDisplayName(serviceLevel),
                expectation,
                serviceDependencyCount));
        }
    }

    private static void AnalyzeMixedLevels(
        ServiceLevel serviceLevel,
        (IParameterSymbol Parameter, ServiceLevel? Level, bool IsBroker)[] dependencies,
        Location location,
        SymbolAnalysisContext context)
    {
        if (serviceLevel is ServiceLevel.Orchestration)
        {
            var serviceLevels = dependencies
                .Where(dependency => dependency.Level is ServiceLevel.Foundation or ServiceLevel.Processing)
                .Select(dependency => dependency.Level)
                .Distinct()
                .Count();
            if (serviceLevels > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(MixedServiceLevels, location, ToDisplayName(serviceLevel)));
            }
        }

        if (serviceLevel == ServiceLevel.Aggregation &&
            dependencies.Where(dependency => dependency.Level is not null)
                .Select(dependency => dependency.Level)
                .Distinct()
                .Count() > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(MixedAggregationLevels, location));
        }
    }

    private static void AnalyzeBrokerDependencies(
        ServiceLevel serviceLevel,
        (IParameterSymbol Parameter, ServiceLevel? Level, bool IsBroker)[] brokerDependencies,
        Location location,
        SymbolAnalysisContext context)
    {
        if (serviceLevel == ServiceLevel.Foundation)
        {
            if (brokerDependencies.Length > 1)
            {
                foreach (var dependency in brokerDependencies.Skip(1))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidBrokerDependency,
                        dependency.Parameter.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? location,
                        ToDisplayName(serviceLevel),
                        $"more than one entity broker (found {brokerDependencies.Length})"));
                }
            }

            return;
        }

        foreach (var dependency in brokerDependencies)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidBrokerDependency,
                dependency.Parameter.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? location,
                ToDisplayName(serviceLevel),
                "non-utility brokers"));
        }
    }

    private static void AnalyzeProcessingEntity(
        INamedTypeSymbol type,
        ServiceLevel serviceLevel,
        (IParameterSymbol Parameter, ServiceLevel? Level, bool IsBroker)[] dependencies,
        SymbolAnalysisContext context)
    {
        if (serviceLevel != ServiceLevel.Processing)
        {
            return;
        }

        var processingEntity = GetEntityName(type.Name, ServiceLevel.Processing);
        foreach (var dependency in dependencies.Where(dependency => dependency.Level == ServiceLevel.Foundation))
        {
            var foundationEntity = GetEntityName(dependency.Parameter.Type.Name, ServiceLevel.Foundation);
            if (StringComparer.OrdinalIgnoreCase.Equals(processingEntity, foundationEntity))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                MismatchedProcessingService,
                dependency.Parameter.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? type.Locations[0],
                processingEntity,
                foundationEntity));
        }
    }

    private static bool IsAllowedDependency(ServiceLevel serviceLevel, ServiceLevel dependencyLevel) =>
        serviceLevel switch
        {
            ServiceLevel.Foundation => false,
            ServiceLevel.Processing => dependencyLevel == ServiceLevel.Foundation,
            ServiceLevel.Orchestration => dependencyLevel is ServiceLevel.Foundation or ServiceLevel.Processing,
            ServiceLevel.Coordination => dependencyLevel == ServiceLevel.Orchestration,
            ServiceLevel.Management => dependencyLevel == ServiceLevel.Coordination,
            ServiceLevel.UberManagement => dependencyLevel == ServiceLevel.Management,
            ServiceLevel.Aggregation => true,
            _ => false
        };

    private static string GetExpectedServiceLevels(ServiceLevel serviceLevel) =>
        serviceLevel switch
        {
            ServiceLevel.Foundation => "no other service level",
            ServiceLevel.Processing => "Foundation",
            ServiceLevel.Orchestration => "Foundation or Processing",
            ServiceLevel.Coordination => "Orchestration",
            ServiceLevel.Management => "Coordination",
            ServiceLevel.UberManagement => "Management",
            ServiceLevel.Aggregation => "any one service level",
            _ => "no other service level"
        };

    private static bool IsBroker(ITypeSymbol type) =>
        GetIdentifierName(type.Name).EndsWith("Broker", StringComparison.Ordinal);

    private static bool IsUtilityBroker(ITypeSymbol type) =>
        IsBroker(type) &&
        type.ContainingNamespace.ToDisplayString()
            .Split('.')
            .Any(name => name.Equals("UtilityBrokers", StringComparison.OrdinalIgnoreCase));

    private static ServiceLevel? GetServiceLevel(string typeName)
    {
        var name = GetIdentifierName(typeName);
        if (name.EndsWith("UberManagementService", StringComparison.Ordinal))
        {
            return ServiceLevel.UberManagement;
        }
        if (name.EndsWith("ManagementService", StringComparison.Ordinal))
        {
            return ServiceLevel.Management;
        }
        if (name.EndsWith("CoordinationService", StringComparison.Ordinal))
        {
            return ServiceLevel.Coordination;
        }
        if (name.EndsWith("OrchestrationService", StringComparison.Ordinal))
        {
            return ServiceLevel.Orchestration;
        }
        if (name.EndsWith("AggregationService", StringComparison.Ordinal))
        {
            return ServiceLevel.Aggregation;
        }
        if (name.EndsWith("ProcessingService", StringComparison.Ordinal))
        {
            return ServiceLevel.Processing;
        }
        if (name.EndsWith("Service", StringComparison.Ordinal))
        {
            return ServiceLevel.Foundation;
        }

        return null;
    }

    private static string GetEntityName(string typeName, ServiceLevel serviceLevel)
    {
        var name = GetIdentifierName(typeName);
        var suffix = serviceLevel switch
        {
            ServiceLevel.Processing => "ProcessingService",
            ServiceLevel.Foundation => "Service",
            _ => string.Empty
        };
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - suffix.Length)
            : name;
    }

    private static string GetIdentifierName(string typeName) =>
        typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1])
            ? typeName.Substring(1)
            : typeName;

    private enum ServiceLevel
    {
        Foundation,
        Processing,
        Orchestration,
        Coordination,
        Management,
        UberManagement,
        Aggregation
    }

    private static string ToDisplayName(ServiceLevel serviceLevel) =>
        serviceLevel switch
        {
            ServiceLevel.Foundation => "Foundation",
            ServiceLevel.Processing => "Processing",
            ServiceLevel.Orchestration => "Orchestration",
            ServiceLevel.Coordination => "Coordination",
            ServiceLevel.Management => "Management",
            ServiceLevel.UberManagement => "Uber Management",
            ServiceLevel.Aggregation => "Aggregation",
            _ => serviceLevel.ToString()
        };
}
