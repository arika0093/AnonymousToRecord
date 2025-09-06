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

namespace AnonymousToRecord;

[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AnonymousToRecordCodeFixProvider)),
    Shared
]
public class AnonymousToRecordCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("ATR001");

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        var diagnostic = context.Diagnostics.FirstOrDefault(d =>
            FixableDiagnosticIds.Contains(d.Id)
        );
        if (diagnostic == null)
            return;

        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var anonymousObject =
            root?.FindNode(diagnosticSpan) as AnonymousObjectCreationExpressionSyntax;

        if (anonymousObject == null)
            return;

        var action = CodeAction.Create(
            title: "Convert to record",
            createChangedDocument: c => ConvertToRecordAsync(context.Document, anonymousObject, c),
            equivalenceKey: "ConvertToRecord"
        );

        context.RegisterCodeFix(action, diagnostic);
    }

    private static async Task<Document> ConvertToRecordAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

        if (root == null || semanticModel == null)
            return document;

        // Find all anonymous objects in the document and process them together
        var allAnonymousObjects = root.DescendantNodes()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .ToList();

        var recordsToCreate =
            new List<(
                string Name,
                RecordProperty[] Properties,
                AnonymousObjectCreationExpressionSyntax Syntax
            )>();
        var objectReplacements =
            new Dictionary<AnonymousObjectCreationExpressionSyntax, ExpressionSyntax>();

        // Process all anonymous objects to determine what records need to be created
        foreach (var anonObj in allAnonymousObjects)
        {
            var properties = GetRecordProperties(anonObj, semanticModel);
            var recordName = GetOrCreateRecordName(anonObj, semanticModel);

            recordsToCreate.Add((recordName, properties, anonObj));

            var objectCreation = SyntaxFactory
                .ObjectCreationExpression(SyntaxFactory.IdentifierName(recordName))
                .WithArgumentList(SyntaxFactory.ArgumentList())
                .WithInitializer(
                    SyntaxFactory.InitializerExpression(
                        SyntaxKind.ObjectInitializerExpression,
                        SyntaxFactory.SeparatedList<ExpressionSyntax>(
                            properties.Select(p =>
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(p.Name),
                                    p.Expression
                                )
                            )
                        )
                    )
                );

            objectReplacements[anonObj] = objectCreation;
        }

        // Sort records by dependency order (records with no anonymous type dependencies first)
        var sortedRecords = SortRecordsByDependencies(recordsToCreate);

        // Create all record declarations
        var recordDeclarations = sortedRecords
            .Select(record => CreateRecordDeclaration(record.Name, record.Properties))
            .ToArray();

        // Find the appropriate location to add records
        var namespaceOrClass = anonymousObject
            .Ancestors()
            .FirstOrDefault(n =>
                n
                    is NamespaceDeclarationSyntax
                        or ClassDeclarationSyntax
                        or FileScopedNamespaceDeclarationSyntax
            );

        SyntaxNode newRoot = root;

        // Add all record declarations
        if (namespaceOrClass is NamespaceDeclarationSyntax namespaceDecl)
        {
            var newNamespace = namespaceDecl.AddMembers(recordDeclarations);
            newRoot = newRoot.ReplaceNode(namespaceDecl, newNamespace);
        }
        else if (namespaceOrClass is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
        {
            var compilationUnit = (CompilationUnitSyntax)newRoot;
            var insertIndex = compilationUnit.Members.IndexOf(fileScopedNamespace) + 1;
            var newMembers = compilationUnit.Members.InsertRange(insertIndex, recordDeclarations);
            var newCompilationUnit = compilationUnit.WithMembers(newMembers);
            newRoot = newCompilationUnit;
        }
        else
        {
            var compilationUnit = (CompilationUnitSyntax)newRoot;
            var newCompilationUnit = compilationUnit.AddMembers(recordDeclarations);
            newRoot = newCompilationUnit;
        }

        // Replace all anonymous object expressions with record constructor calls
        newRoot = newRoot.ReplaceNodes(
            objectReplacements.Keys,
            (original, rewritten) => objectReplacements[original]
        );

        return document.WithSyntaxRoot(newRoot);
    }

    private static List<(
        string Name,
        RecordProperty[] Properties,
        AnonymousObjectCreationExpressionSyntax Syntax
    )> SortRecordsByDependencies(
        List<(
            string Name,
            RecordProperty[] Properties,
            AnonymousObjectCreationExpressionSyntax Syntax
        )> records
    )
    {
        // Simple topological sort - records with anonymous type dependencies come after their dependencies
        var sorted =
            new List<(
                string Name,
                RecordProperty[] Properties,
                AnonymousObjectCreationExpressionSyntax Syntax
            )>();
        var remaining = new List<(
            string Name,
            RecordProperty[] Properties,
            AnonymousObjectCreationExpressionSyntax Syntax
        )>(records);

        while (remaining.Count > 0)
        {
            var canProcess = remaining
                .Where(record =>
                    !record.Properties.Any(prop =>
                        prop.Type.StartsWith("AnonymousRecord")
                        && remaining.Any(other => other.Name == prop.Type)
                    )
                )
                .ToList();

            if (canProcess.Count == 0)
            {
                // If we can't find any records without dependencies, add them in original order
                // This handles circular dependencies by adding them in declaration order
                sorted.AddRange(remaining);
                break;
            }

            sorted.AddRange(canProcess);
            foreach (var processed in canProcess)
            {
                remaining.Remove(processed);
            }
        }

        return sorted;
    }

    private static RecordProperty[] GetRecordProperties(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        SemanticModel semanticModel
    )
    {
        return anonymousObject
            .Initializers.Select(initializer => new RecordProperty
            {
                Name = GetPropertyName(initializer),
                Type = GetPropertyType(initializer, semanticModel),
                Expression = initializer.Expression,
            })
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .ToArray();
    }

    private static string GetPropertyName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        if (initializer.NameEquals != null)
        {
            return initializer.NameEquals.Name.Identifier.ValueText;
        }

        if (initializer.Expression is IdentifierNameSyntax identifierName)
        {
            return identifierName.Identifier.ValueText;
        }

        if (initializer.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.ValueText;
        }

        return "Property";
    }

    private static readonly Dictionary<ITypeSymbol, string> _anonymousTypeNames = new(
        SymbolEqualityComparer.Default
    );

    private static string GetPropertyType(
        AnonymousObjectMemberDeclaratorSyntax initializer,
        SemanticModel semanticModel
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(initializer.Expression);
        var type = typeInfo.Type;

        if (type == null)
            return "object";

        // Check if this is an anonymous type
        if (type.IsAnonymousType)
        {
            // Always assign a record name for anonymous type
            if (!_anonymousTypeNames.TryGetValue(type, out var recordName))
            {
                recordName = $"AnonymousRecord{_recordCounter++:000}";
                _anonymousTypeNames[type] = recordName;
            }
            return recordName;
        }

        // If this is a constructed generic type (e.g. IEnumerable<T>),
        // and T is an anonymous type, replace T with its record name
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var typeArgs = namedType.TypeArguments;
            var typeArgNames = typeArgs.Select(arg =>
            {
                if (arg.IsAnonymousType)
                {
                    if (!_anonymousTypeNames.TryGetValue(arg, out var anonRecordName))
                    {
                        anonRecordName = $"AnonymousRecord{_recordCounter++:000}";
                        _anonymousTypeNames[arg] = anonRecordName;
                    }
                    return anonRecordName;
                }
                return arg.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            });

            // Get the generic type name without type parameters
            var genericName = namedType.ConstructedFrom.Name;
            var namespaceName = namedType.ConstructedFrom.ContainingNamespace?.ToDisplayString();

            if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "System")
            {
                genericName = $"{namespaceName}.{genericName}";
            }

            return $"{genericName}<{string.Join(", ", typeArgNames)}>";
        }

        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static int _recordCounter = 1;

    private static string GetOrCreateRecordName(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        SemanticModel semanticModel
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(anonymousObject);
        var type = typeInfo.Type;

        if (type != null && type.IsAnonymousType)
        {
            if (!_anonymousTypeNames.TryGetValue(type, out var recordName))
            {
                recordName = $"AnonymousRecord{_recordCounter++:000}";
                _anonymousTypeNames[type] = recordName;
            }
            return recordName;
        }

        return $"AnonymousRecord_{_recordCounter++:000}";
    }

    private static RecordDeclarationSyntax CreateRecordDeclaration(
        string recordName,
        RecordProperty[] properties
    )
    {
        var propertyDeclarations = properties.Select(p =>
            SyntaxFactory
                .PropertyDeclaration(SyntaxFactory.ParseTypeName(p.Type), p.Name)
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                        SyntaxFactory.Token(SyntaxKind.RequiredKeyword)
                    )
                )
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.List(
                            new[]
                            {
                                SyntaxFactory
                                    .AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                    .WithSemicolonToken(
                                        SyntaxFactory.Token(SyntaxKind.SemicolonToken)
                                    ),
                                SyntaxFactory
                                    .AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                                    .WithSemicolonToken(
                                        SyntaxFactory.Token(SyntaxKind.SemicolonToken)
                                    ),
                            }
                        )
                    )
                )
        );

        return SyntaxFactory
            .RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), recordName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(propertyDeclarations))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));
    }

    private class RecordProperty
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public ExpressionSyntax Expression { get; set; } = null!;
    }
}
