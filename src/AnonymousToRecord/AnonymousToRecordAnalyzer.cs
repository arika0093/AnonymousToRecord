using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace AnonymousToRecord;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AnonymousToRecordAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor AnonymousObjectRule = new DiagnosticDescriptor(
        "ATR001",
        "Anonymous object can be converted to record",
        "Anonymous object with properties '{0}' can be converted to a record type",
        "Design",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Anonymous objects can be replaced with record types for better type safety and reusability.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AnonymousObjectRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAnonymousObject, SyntaxKind.AnonymousObjectCreationExpression);
    }

    private static void AnalyzeAnonymousObject(SyntaxNodeAnalysisContext context)
    {
        var anonymousObject = (AnonymousObjectCreationExpressionSyntax)context.Node;

        if (anonymousObject.Initializers.Count == 0)
            return;

        var properties = anonymousObject.Initializers
            .Select(initializer => GetPropertyName(initializer))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();

        if (properties.Length == 0)
            return;

        var propertiesText = string.Join(", ", properties);

        var diagnostic = Diagnostic.Create(
            AnonymousObjectRule,
            anonymousObject.GetLocation(),
            propertiesText);

        context.ReportDiagnostic(diagnostic);
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

        return string.Empty;
    }
}