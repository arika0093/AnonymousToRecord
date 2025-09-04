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

        var recordProperties = GetRecordProperties(anonymousObject, semanticModel);
        var recordName = GenerateRecordName(recordProperties);

        var recordDeclaration = CreateRecordDeclaration(recordName, recordProperties);

        var namespaceOrClass = anonymousObject
            .Ancestors()
            .FirstOrDefault(n =>
                n
                    is NamespaceDeclarationSyntax
                        or ClassDeclarationSyntax
                        or FileScopedNamespaceDeclarationSyntax
            );

        SyntaxNode newRoot;

        if (namespaceOrClass is NamespaceDeclarationSyntax namespaceDecl)
        {
            var newNamespace = namespaceDecl.AddMembers(recordDeclaration);
            newRoot = root.ReplaceNode(namespaceDecl, newNamespace);
        }
        else if (namespaceOrClass is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
        {
            var compilationUnit = (CompilationUnitSyntax)root;
            var insertIndex = compilationUnit.Members.IndexOf(fileScopedNamespace) + 1;
            var newMembers = compilationUnit.Members.Insert(insertIndex, recordDeclaration);
            var newCompilationUnit = compilationUnit.WithMembers(newMembers);
            newRoot = newCompilationUnit;
        }
        else
        {
            var compilationUnit = (CompilationUnitSyntax)root;
            var newCompilationUnit = compilationUnit.AddMembers(recordDeclaration);
            newRoot = newCompilationUnit;
        }

        var objectCreation = SyntaxFactory
            .ObjectCreationExpression(SyntaxFactory.IdentifierName(recordName))
            .WithArgumentList(
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(
                        recordProperties.Select(p => SyntaxFactory.Argument(p.Expression))
                    )
                )
            );

        newRoot = newRoot.ReplaceNode(anonymousObject, objectCreation);

        return document.WithSyntaxRoot(newRoot);
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

    private static string GetPropertyType(
        AnonymousObjectMemberDeclaratorSyntax initializer,
        SemanticModel semanticModel
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(initializer.Expression);
        var type = typeInfo.Type;

        if (type == null)
            return "object";

        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string GenerateRecordName(RecordProperty[] properties)
    {
        if (properties.Length == 0)
            return "GeneratedRecord";

        var firstProperty = properties[0].Name;
        var capitalizedFirst = char.ToUpper(firstProperty[0]) + firstProperty.Substring(1);

        return properties.Length == 1
            ? $"{capitalizedFirst}Record"
            : $"{capitalizedFirst}AndOthersRecord";
    }

    private static RecordDeclarationSyntax CreateRecordDeclaration(
        string recordName,
        RecordProperty[] properties
    )
    {
        var parameters = properties.Select(p =>
            SyntaxFactory
                .Parameter(SyntaxFactory.Identifier(p.Name))
                .WithType(SyntaxFactory.ParseTypeName(p.Type))
        );

        var parameterList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));

        return SyntaxFactory
            .RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), recordName)
            .WithParameterList(parameterList)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }

    private class RecordProperty
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public ExpressionSyntax Expression { get; set; } = null!;
    }
}
