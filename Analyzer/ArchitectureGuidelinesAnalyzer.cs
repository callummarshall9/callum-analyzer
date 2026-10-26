using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynArchitectureGuidelines;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArchitectureGuidelinesAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Architecture";

    private static readonly DiagnosticDescriptor NoGlobalAlias = Rule(
        "RAG001", "Avoid global-qualified names", "Replace 'global::' with a using directive and a local type name");
    private static readonly DiagnosticDescriptor NoLocalFunctions = Rule(
        "RAG002", "Avoid local functions", "Move local function '{0}' to an appropriate containing scope");
    private static readonly DiagnosticDescriptor NoConfigureAwaitFalse = Rule(
        "RAG003", "Avoid ConfigureAwait(false)", "Remove ConfigureAwait(false)");
    private static readonly DiagnosticDescriptor OneTypePerFile = Rule(
        "RAG004", "Use one type per file", "This file declares multiple types; move '{0}' to its own file");
    private static readonly DiagnosticDescriptor SortedUsings = Rule(
        "RAG005", "Remove unused or sort using directives", "Remove this unused using directive or sort using directives");
    private static readonly DiagnosticDescriptor NoEmptyCatch = Rule(
        "RAG006", "Do not leave catch blocks empty", "Handle the exception or log it through the injected logging service");
    private static readonly DiagnosticDescriptor CentralLogging = Rule(
        "RAG007", "Use centralized logging", "Use the injected logging service instead of '{0}'");
    private static readonly DiagnosticDescriptor InlineObjectCreation = Rule(
        "RAG008", "Inline object creation", "Inline this object creation instead of wrapping it in a factory method");
    private static readonly DiagnosticDescriptor NoOptionalFallback = Rule(
        "RAG009", "Require constructor dependencies", "Do not use optional constructor parameters or create fallback dependencies");
    private static readonly DiagnosticDescriptor OneContractPerService = Rule(
        "RAG010", "Keep services focused on one contract", "This service exposes multiple distinct return-type contracts");
    private static readonly DiagnosticDescriptor NoExposureDependency = Rule(
        "RAG011", "Services must not depend on exposure types", "Do not inject exposure-layer type '{0}' into a service");
    private static readonly DiagnosticDescriptor NoExposureState = Rule(
        "RAG012", "Keep exposure types free of data", "Exposure types must not declare fields, properties, or primary-constructor parameters");
    private static readonly DiagnosticDescriptor ConstructorInjectionOnly = Rule(
        "RAG013", "Keep service constructors declarative", "Service constructors should only assign injected dependencies and must not create objects or run logic");

    private static readonly ImmutableArray<DiagnosticDescriptor> SupportedRules = ImmutableArray.Create(
        NoGlobalAlias,
        NoLocalFunctions,
        NoConfigureAwaitFalse,
        OneTypePerFile,
        SortedUsings,
        NoEmptyCatch,
        CentralLogging,
        InlineObjectCreation,
        NoOptionalFallback,
        OneContractPerService,
        NoExposureDependency,
        NoExposureState,
        ConstructorInjectionOnly);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SupportedRules;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeGlobalAlias, SyntaxKind.AliasQualifiedName);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeConfigureAwait, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeCompilationUnit, SyntaxKind.CompilationUnit);
        context.RegisterSyntaxNodeAction(AnalyzeCatch, SyntaxKind.CatchClause);
        context.RegisterSyntaxNodeAction(AnalyzeDiagnosticsLogging, SyntaxKind.QualifiedName, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeFactoryMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeOptionalParameter, SyntaxKind.Parameter);
        context.RegisterSyntaxNodeAction(AnalyzeConstructorFallback, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeServiceReturnTypes, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeExposureState, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.RecordDeclaration);
        context.RegisterSymbolAction(AnalyzeServiceDependencies, SymbolKind.NamedType);
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static void AnalyzeGlobalAlias(SyntaxNodeAnalysisContext context)
    {
        var alias = (AliasQualifiedNameSyntax)context.Node;
        if (alias.Alias.Identifier.ValueText == "global")
        {
            context.ReportDiagnostic(Diagnostic.Create(NoGlobalAlias, alias.GetLocation()));
        }
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var function = (LocalFunctionStatementSyntax)context.Node;
        context.ReportDiagnostic(Diagnostic.Create(NoLocalFunctions, function.Identifier.GetLocation(), function.Identifier.ValueText));
    }

    private static void AnalyzeConfigureAwait(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "ConfigureAwait" &&
            invocation.ArgumentList.Arguments.Count == 1 &&
            context.SemanticModel.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression, context.CancellationToken) is { HasValue: true, Value: false })
        {
            context.ReportDiagnostic(Diagnostic.Create(NoConfigureAwaitFalse, invocation.GetLocation()));
        }
    }

    private static void AnalyzeCompilationUnit(SyntaxNodeAnalysisContext context)
    {
        var root = (CompilationUnitSyntax)context.Node;
        var types = root.DescendantNodes()
            .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .ToArray();
        if (types.Length > 1)
        {
            foreach (var type in types.Skip(1))
            {
                var (identifier, name) = type is BaseTypeDeclarationSyntax baseType
                    ? (baseType.Identifier, baseType.Identifier.ValueText)
                    : (((DelegateDeclarationSyntax)type).Identifier, ((DelegateDeclarationSyntax)type).Identifier.ValueText);
                context.ReportDiagnostic(Diagnostic.Create(OneTypePerFile, identifier.GetLocation(), name));
            }
        }

        var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToArray();
        var reportedUsingDirectives = new HashSet<UsingDirectiveSyntax>();
        foreach (var scope in usingDirectives.GroupBy(usingDirective => usingDirective.Parent))
        {
            var scopedUsings = scope.ToArray();
            for (var index = 1; index < scopedUsings.Length; index++)
            {
                if (StringComparer.Ordinal.Compare(scopedUsings[index - 1].Name?.ToString(), scopedUsings[index].Name?.ToString()) > 0 &&
                    reportedUsingDirectives.Add(scopedUsings[index]))
                {
                    context.ReportDiagnostic(Diagnostic.Create(SortedUsings, scopedUsings[index].GetLocation()));
                }
            }
        }

        foreach (var usingDirective in usingDirectives)
        {
            if (!IsUsingDirectiveUsed(usingDirective, root, context.SemanticModel, context.CancellationToken) &&
                reportedUsingDirectives.Add(usingDirective))
            {
                context.ReportDiagnostic(Diagnostic.Create(SortedUsings, usingDirective.GetLocation()));
            }
        }
    }

    private static bool IsUsingDirectiveUsed(
        UsingDirectiveSyntax usingDirective,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var aliasName = usingDirective.Alias?.Name.Identifier.ValueText;
        if (aliasName is not null)
        {
            return root.DescendantNodes()
                .OfType<SimpleNameSyntax>()
                .Any(identifier => !identifier.Ancestors().OfType<UsingDirectiveSyntax>().Any() &&
                    identifier.Identifier.ValueText == aliasName);
        }

        var importedSymbol = usingDirective.Name is null
            ? null
            : semanticModel.GetSymbolInfo(usingDirective.Name, cancellationToken).Symbol;
        if (importedSymbol is null)
        {
            return true;
        }

        var references = root.DescendantNodes()
            .OfType<SimpleNameSyntax>();
        foreach (var reference in references)
        {
            if (reference.Ancestors().OfType<UsingDirectiveSyntax>().Any())
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(reference, cancellationToken).Symbol;
            if (symbol is null)
            {
                continue;
            }

            var containingNamespace = symbol.ContainingNamespace;
            if (importedSymbol is INamespaceSymbol importedNamespace &&
                StringComparer.Ordinal.Equals(
                    containingNamespace?.ToDisplayString(),
                    importedNamespace.ToDisplayString()))
            {
                return true;
            }

            if (importedSymbol is INamedTypeSymbol importedType &&
                (usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                    ? SymbolEqualityComparer.Default.Equals(symbol.ContainingType, importedType)
                    : SymbolEqualityComparer.Default.Equals(symbol, importedType)))
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeCatch(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Block is { Statements.Count: 0 })
        {
            context.ReportDiagnostic(Diagnostic.Create(NoEmptyCatch, catchClause.GetLocation()));
        }
    }

    private static void AnalyzeDiagnosticsLogging(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is InvocationExpressionSyntax invocation)
        {
            var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
            if (method?.ContainingType.ToDisplayString() == "System.Console" && method.Name == "WriteLine")
            {
                context.ReportDiagnostic(Diagnostic.Create(CentralLogging, invocation.GetLocation(), "Console.WriteLine"));
            }
        }
        else if (context.Node is QualifiedNameSyntax qualifiedName &&
            qualifiedName.Parent is not QualifiedNameSyntax &&
            qualifiedName.ToString().StartsWith("System.Diagnostics", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(CentralLogging, qualifiedName.GetLocation(), "System.Diagnostics"));
        }
    }

    private static void AnalyzeFactoryMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        ExpressionSyntax? returnedObject = method.ExpressionBody?.Expression;
        if (returnedObject is null && method.Body is { Statements.Count: 1 } body &&
            body.Statements[0] is ReturnStatementSyntax returnStatement)
        {
            returnedObject = returnStatement.Expression;
        }

        if (returnedObject is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(InlineObjectCreation, method.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeOptionalParameter(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (parameter.Parent?.Parent is ConstructorDeclarationSyntax && parameter.Default is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(NoOptionalFallback, parameter.GetLocation()));
        }
    }

    private static void AnalyzeConstructorFallback(SyntaxNodeAnalysisContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;
        foreach (var coalesce in constructor.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (coalesce.IsKind(SyntaxKind.CoalesceExpression) &&
                coalesce.Right is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(NoOptionalFallback, coalesce.GetLocation()));
            }
        }
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;
        if (!IsService(constructor.Parent, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var createsObjects = constructor.DescendantNodes().Any(node =>
            node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax);
        var hasLogic = constructor.Body?.Statements.Any(statement =>
            !IsDependencyAssignment(statement, constructor, context.SemanticModel, context.CancellationToken)) == true;
        if (createsObjects || hasLogic)
        {
            context.ReportDiagnostic(Diagnostic.Create(ConstructorInjectionOnly, constructor.Identifier.GetLocation()));
        }
    }

    private static bool IsDependencyAssignment(
        StatementSyntax statement,
        ConstructorDeclarationSyntax constructor,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (statement is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return false;
        }

        var constructorSymbol = semanticModel.GetDeclaredSymbol(constructor, cancellationToken);
        var assignedParameter = semanticModel.GetSymbolInfo(assignment.Right, cancellationToken).Symbol;
        var assignedMember = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        return constructorSymbol is not null &&
            assignedParameter is IParameterSymbol parameter &&
            SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, constructorSymbol) &&
            assignedMember is IFieldSymbol or IPropertySymbol &&
            SymbolEqualityComparer.Default.Equals(assignedMember.ContainingType, constructorSymbol.ContainingType);
    }

    private static void AnalyzeServiceReturnTypes(SyntaxNodeAnalysisContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (!IsService(declaration, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (classSymbol is null)
        {
            return;
        }

        var returnTypes = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => !method.IsImplicitlyDeclared && method.MethodKind == MethodKind.Ordinary)
            .Select(method => method.ReturnType)
            .Select(GetServiceContractType)
            .Where(type => type.SpecialType == SpecialType.None && type.TypeKind != TypeKind.Error)
            .Select(type => type.OriginalDefinition.ToDisplayString())
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count();
        if (returnTypes > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(OneContractPerService, declaration.Identifier.GetLocation()));
        }
    }

    private static ITypeSymbol GetServiceContractType(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType &&
            namedType.TypeArguments.Length == 1 &&
            (namedType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>" ||
                namedType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.ValueTask<TResult>"))
        {
            return namedType.TypeArguments[0];
        }
        return returnType;
    }

    private static void AnalyzeExposureState(SyntaxNodeAnalysisContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (!IsExposureType(declaration, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var hasState = declaration is RecordDeclarationSyntax record && record.ParameterList is { Parameters.Count: > 0 } ||
            declaration.Members.Any(member => member is FieldDeclarationSyntax or PropertyDeclarationSyntax or EventFieldDeclarationSyntax);
        if (hasState)
        {
            context.ReportDiagnostic(Diagnostic.Create(NoExposureState, declaration.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeServiceDependencies(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsService(type))
        {
            return;
        }

        foreach (var constructor in type.InstanceConstructors.Where(constructor => !constructor.IsImplicitlyDeclared))
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (IsExposureType(parameter.Type))
                {
                    var location = parameter.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                    if (location is not null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NoExposureDependency, location, parameter.Type.Name));
                    }
                }
            }
        }
    }

    private static bool IsService(SyntaxNode? declaration, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var symbol = declaration is null ? null : semanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
        return symbol is not null && IsService(symbol);
    }

    private static bool IsService(INamedTypeSymbol symbol) =>
        symbol.Name.EndsWith("Service", StringComparison.Ordinal) ||
        symbol.ContainingNamespace.ToDisplayString().IndexOf(".Services", StringComparison.OrdinalIgnoreCase) >= 0 ||
        symbol.ContainingNamespace.Name.Equals("Services", StringComparison.OrdinalIgnoreCase);

    private static bool IsExposureType(TypeDeclarationSyntax declaration, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
        return symbol is not null && IsExposureType(symbol);
    }

    private static bool IsExposureType(ITypeSymbol symbol) =>
        symbol.Name.EndsWith("Exposure", StringComparison.Ordinal) ||
        symbol.ContainingNamespace.ToDisplayString().IndexOf(".Exposures", StringComparison.OrdinalIgnoreCase) >= 0 ||
        symbol.ContainingNamespace.Name.Equals("Exposures", StringComparison.OrdinalIgnoreCase);
}
