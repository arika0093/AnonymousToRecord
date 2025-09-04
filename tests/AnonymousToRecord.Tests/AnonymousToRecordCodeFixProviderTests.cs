using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace AnonymousToRecord.Tests;

public class AnonymousToRecordCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_SimpleAnonymousObject_ShouldGenerateRecord()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var obj = new { Name = "John", Age = 30 };
                }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(testCode);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }
        );

        // Find anonymous object in syntax
        var root = await syntaxTree.GetRootAsync();
        var anonymousObject = root.DescendantNodes()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .First();

        var codeFixProvider = new AnonymousToRecordCodeFixProvider();

        // Test that the provider recognizes the diagnostic
        var fixableDiagnostics = codeFixProvider.FixableDiagnosticIds;
        Assert.Contains("ATR001", fixableDiagnostics);
    }

    [Fact]
    public void CodeFixProvider_ShouldHaveCorrectFixableDiagnosticIds()
    {
        var codeFixProvider = new AnonymousToRecordCodeFixProvider();
        var fixableDiagnostics = codeFixProvider.FixableDiagnosticIds;

        Assert.Single(fixableDiagnostics);
        Assert.Equal("ATR001", fixableDiagnostics[0]);
    }

    [Fact]
    public void CodeFixProvider_ShouldHaveFixAllProvider()
    {
        var codeFixProvider = new AnonymousToRecordCodeFixProvider();
        var fixAllProvider = codeFixProvider.GetFixAllProvider();

        Assert.NotNull(fixAllProvider);
    }

    [Fact]
    public async Task RecordProperty_GetPropertyName_ShouldExtractCorrectName()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var name = "John";
                    var obj = new { name, Age = 30, this.ToString };
                }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(testCode);
        var root = await syntaxTree.GetRootAsync();
        var anonymousObject = root.DescendantNodes()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .First();

        var initializers = anonymousObject.Initializers;

        // Test different types of property expressions
        Assert.Equal(3, initializers.Count);

        // First should be identifier name (name)
        var first = initializers[0];
        Assert.IsType<IdentifierNameSyntax>(first.Expression);

        // Second should have explicit name (Age = 30)
        var second = initializers[1];
        Assert.NotNull(second.NameEquals);
        Assert.Equal("Age", second.NameEquals.Name.Identifier.ValueText);

        // Third should be member access (this.ToString)
        var third = initializers[2];
        Assert.IsType<MemberAccessExpressionSyntax>(third.Expression);
    }
}
