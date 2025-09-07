using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AnonymousToRecord;

/// <summary>
/// Analyzer that identifies anonymous objects that can be converted to record types.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AnonymousToRecordAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic descriptor for anonymous objects that can be converted to records.
    /// </summary>
    public static readonly DiagnosticDescriptor AnonymousObjectRule = new DiagnosticDescriptor(
        "ATR001",
        "Anonymous object can be converted to record",
        "Anonymous object with properties '{0}' can be converted to a record type",
        "Design",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Anonymous objects can be replaced with record types for better type safety and reusability."
    );

    /// <summary>
    /// Gets the set of supported diagnostic descriptors for this analyzer.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AnonymousObjectRule);

    /// <summary>
    /// Initializes the analyzer by registering for syntax node analysis.
    /// </summary>
    /// <param name="context">The analysis context.</param>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeAnonymousObject,
            SyntaxKind.AnonymousObjectCreationExpression
        );
    }

    /// <summary>
    /// Analyzes anonymous object creation expressions to determine if they can be converted to records.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    private static void AnalyzeAnonymousObject(SyntaxNodeAnalysisContext context)
    {
        var anonymousObject = (AnonymousObjectCreationExpressionSyntax)context.Node;

        if (anonymousObject.Initializers.Count == 0)
            return;

        var properties = anonymousObject
            .Initializers.Select(initializer => GetPropertyName(initializer))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();

        if (properties.Length == 0)
            return;

        var propertiesText = string.Join(", ", properties);

        var diagnostic = Diagnostic.Create(
            AnonymousObjectRule,
            anonymousObject.GetLocation(),
            propertiesText
        );

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Extracts the property name from an anonymous object member declarator.
    /// </summary>
    /// <param name="initializer">The anonymous object member declarator.</param>
    /// <returns>The property name, or empty string if it cannot be determined.</returns>
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
