using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

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
        "RAG012", "Keep exposure types free of data", "Exposure types may only retain private readonly injected service fields");
    private static readonly DiagnosticDescriptor ConstructorInjectionOnly = Rule(
        "RAG013", "Keep service constructors declarative", "Service constructors should only assign injected dependencies and must not create objects or run logic");
    private static readonly DiagnosticDescriptor ExtensionMethodPassThrough = Rule(
        "RAG014", "Keep extension methods as pass-throughs", "Extension methods should directly forward a single call without adding logic");
    private static readonly DiagnosticDescriptor BusinessLogicInServicesOrBrokers = Rule(
        "RAG015", "Keep business logic in services or brokers", "Methods outside services and brokers should directly forward a single call");
    private static readonly DiagnosticDescriptor ExposurePassThrough = Rule(
        "RAG016", "Keep exposures as service pass-throughs", "Exposure types must inject a service and directly forward calls to it");
    private static readonly DiagnosticDescriptor LayerFolder = Rule(
        "RAG024", "Place files in an architecture layer", "Place this file in an Exposures, Models, Services, or Brokers folder");
    private static readonly DiagnosticDescriptor ModelDataOnly = Rule(
        "RAG025", "Keep models as data carriers", "Models should contain only auto-properties and no constructors or logic");
    private static readonly DiagnosticDescriptor EnumInModels = Rule(
        "RAG026", "Place enums under Models", "Enums should be declared in a folder under Models");

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
        ConstructorInjectionOnly,
        ExtensionMethodPassThrough,
        BusinessLogicInServicesOrBrokers,
        ExposurePassThrough,
        LayerFolder,
        ModelDataOnly,
        EnumInModels);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SupportedRules;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeGlobalAlias, SyntaxKind.AliasQualifiedName);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeConfigureAwait, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeCompilationUnit, SyntaxKind.CompilationUnit);
        context.RegisterSyntaxNodeAction(AnalyzeModelType, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.RecordDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeEnumLocation, SyntaxKind.EnumDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeCatch, SyntaxKind.CatchClause);
        context.RegisterSyntaxNodeAction(AnalyzeDiagnosticsLogging, SyntaxKind.QualifiedName, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeFactoryMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeOptionalParameter, SyntaxKind.Parameter);
        context.RegisterSyntaxNodeAction(AnalyzeConstructorFallback, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeServiceReturnTypes, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeExposureState, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.RecordDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethodLogic, SyntaxKind.MethodDeclaration);
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
        var filePath = root.SyntaxTree.FilePath;
        if (!string.IsNullOrEmpty(filePath))
        {
            var pathSegments = filePath.Split('/', '\\');
            if (!pathSegments[pathSegments.Length - 1].Equals("Program.cs", StringComparison.OrdinalIgnoreCase) &&
                !pathSegments.Any(segment =>
                    segment.Equals("Exposures", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Models", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Services", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Brokers", StringComparison.OrdinalIgnoreCase)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    LayerFolder,
                    Location.Create(root.SyntaxTree, new TextSpan(0, 0))));
            }

        }

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

    private static void AnalyzeModelType(SyntaxNodeAnalysisContext context)
    {
        var type = (TypeDeclarationSyntax)context.Node;
        var filePath = type.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath) ||
            !filePath.Split('/', '\\').Any(segment =>
                segment.Equals("Models", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        foreach (var member in type.Members)
        {
            var isDataProperty = member is PropertyDeclarationSyntax property &&
                property.ExpressionBody is null &&
                property.AccessorList is { Accessors.Count: > 0 } accessors &&
                accessors.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null);
            if (!isDataProperty)
            {
                context.ReportDiagnostic(Diagnostic.Create(ModelDataOnly, member.GetLocation()));
            }
        }
    }

    private static void AnalyzeEnumLocation(SyntaxNodeAnalysisContext context)
    {
        var declaration = (EnumDeclarationSyntax)context.Node;
        var filePath = declaration.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath) ||
            filePath.Split('/', '\\').Any(segment =>
                segment.Equals("Models", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(EnumInModels, declaration.Identifier.GetLocation()));
    }

    internal static bool IsUsingDirectiveUsed(
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

        var type = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        var hasInvalidState = declaration is RecordDeclarationSyntax record &&
                record.ParameterList?.Parameters.Any(parameter =>
                    type is null || !IsService(context.SemanticModel.GetTypeInfo(parameter.Type!, context.CancellationToken).Type)) == true ||
            declaration.Members.Any(member => member switch
            {
                FieldDeclarationSyntax field => !IsInjectedServiceField(field, type, context.SemanticModel, context.CancellationToken),
                PropertyDeclarationSyntax or EventFieldDeclarationSyntax => true,
                _ => false
            });
        if (hasInvalidState)
        {
            context.ReportDiagnostic(Diagnostic.Create(NoExposureState, declaration.Identifier.GetLocation()));
        }

        if (declaration is not InterfaceDeclarationSyntax &&
            type is not null &&
            !HasInjectedServiceParameter(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(ExposurePassThrough, declaration.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeMethodLogic(SyntaxNodeAnalysisContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        var method = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (method is null ||
            declaration.Body is null && declaration.ExpressionBody is null)
        {
            return;
        }

        if (!HasSingleInvocationBody(declaration))
        {
            if (method.IsExtensionMethod)
            {
                context.ReportDiagnostic(Diagnostic.Create(ExtensionMethodPassThrough, declaration.Identifier.GetLocation()));
            }
            else if (!IsService(method.ContainingType) &&
                !IsBroker(method.ContainingType) &&
                !IsExposureType(method.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(BusinessLogicInServicesOrBrokers, declaration.Identifier.GetLocation()));
            }

            if (IsExposureType(method.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(ExposurePassThrough, declaration.Identifier.GetLocation()));
            }

            return;
        }

        if (method.IsExtensionMethod)
        {
            return;
        }

        if (IsExposureType(method.ContainingType) &&
            !IsInjectedServiceCall(declaration, method.ContainingType, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(ExposurePassThrough, declaration.Identifier.GetLocation()));
        }

    }

    private static bool HasSingleInvocationBody(MethodDeclarationSyntax method) =>
        GetSingleInvocation(method) is not null;

    private static bool IsInjectedServiceCall(
        MethodDeclarationSyntax method,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var invocation = GetSingleInvocation(method);
        if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess ||
            semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not ISymbol receiver ||
            semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol calledMethod ||
            !IsService(calledMethod.ContainingType) ||
            !IsService(receiver switch
            {
                IFieldSymbol field => field.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null
            }))
        {
            return false;
        }

        if (receiver is IParameterSymbol constructorParameter)
        {
            return constructorParameter.ContainingSymbol is IMethodSymbol constructor &&
                constructor.MethodKind == MethodKind.Constructor &&
                SymbolEqualityComparer.Default.Equals(constructor.ContainingType, containingType);
        }

        return receiver is IFieldSymbol serviceField &&
            IsInjectedServiceField(serviceField, containingType);
    }

    private static InvocationExpressionSyntax? GetSingleInvocation(MethodDeclarationSyntax method)
    {
        ExpressionSyntax? expression = method.ExpressionBody?.Expression;
        if (expression is null && method.Body is { Statements.Count: 1 } body)
        {
            expression = body.Statements[0] switch
            {
                ReturnStatementSyntax returnStatement => returnStatement.Expression,
                ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
                _ => null
            };
        }

        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            expression = awaitExpression.Expression;
        }

        return expression as InvocationExpressionSyntax;
    }

    private static bool HasInjectedServiceParameter(INamedTypeSymbol type) =>
        type.InstanceConstructors
            .SelectMany(constructor => constructor.Parameters)
            .Any(parameter => IsService(parameter.Type));

    private static bool IsInjectedServiceField(
        FieldDeclarationSyntax field,
        INamedTypeSymbol? containingType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (containingType is null ||
            !field.Modifiers.Any(SyntaxKind.PrivateKeyword) ||
            !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
        {
            return false;
        }

        return field.Declaration.Variables.All(variable =>
            semanticModel.GetDeclaredSymbol(variable, cancellationToken) is IFieldSymbol symbol &&
            IsInjectedServiceField(symbol, containingType));
    }

    private static bool IsInjectedServiceField(IFieldSymbol field, INamedTypeSymbol containingType) =>
        IsService(field.Type) &&
        field.IsReadOnly &&
        !field.IsStatic &&
        SymbolEqualityComparer.Default.Equals(field.ContainingType, containingType) &&
        HasInjectedServiceParameter(containingType);

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

    private static bool IsService(ITypeSymbol? symbol) =>
        symbol is INamedTypeSymbol namedType && IsService(namedType);

    private static bool IsBroker(INamedTypeSymbol symbol) =>
        symbol.Name.EndsWith("Broker", StringComparison.Ordinal) ||
        symbol.ContainingNamespace.ToDisplayString().IndexOf(".Brokers", StringComparison.OrdinalIgnoreCase) >= 0 ||
        symbol.ContainingNamespace.Name.Equals("Brokers", StringComparison.OrdinalIgnoreCase);

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
