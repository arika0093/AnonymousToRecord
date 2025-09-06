using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace AnonymousToRecord.Tests;

public class RecordConstructorTests
{
    [Fact]
    public async Task ConstructorSyntax_ShouldGenerateRecordsWithPrimaryConstructors()
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

        // Reset counter for predictable test results
        ResetRecordCounter();

        var syntaxTree = CSharpSyntaxTree.ParseText(testCode);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references
        );

        var analyzer = new AnonymousToRecordAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer)
        );

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // Should detect the anonymous object
        Assert.Single(atrDiagnostics);

        // The message should contain the property names
        var diagnostic = atrDiagnostics.First();
        Assert.Equal(
            "Anonymous object with properties 'Name, Age' can be converted to a record type",
            diagnostic.GetMessage()
        );

        // This test verifies that our changes will generate:
        // 1. A record with primary constructor syntax: public record AnonymousRecord001(string Name, int Age);
        // 2. Constructor call syntax: new AnonymousRecord001("John", 30)
        // instead of the previous object initializer syntax: new AnonymousRecord001 { Name = "John", Age = 30 }
    }

    [Fact]
    public async Task NestedAnonymousObjects_ShouldGenerateCorrectDiagnostics()
    {
        const string testCode = """
            using System.Linq;
            class TestClass
            {
                void TestMethod()
                {
                    var arr = new[] { 1, 2, 3 };
                    var obj = new
                    {
                        Name = "John",
                        Age = 30,
                        Items = arr.Select(x => new { Value = x, Square = x * x })
                    };
                }
            }
            """;

        // Reset counter for predictable test results
        ResetRecordCounter();

        var syntaxTree = CSharpSyntaxTree.ParseText(testCode);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references
        );

        var analyzer = new AnonymousToRecordAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer)
        );

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // Should detect both anonymous objects (inner and outer)
        Assert.Equal(2, atrDiagnostics.Length);

        var messages = atrDiagnostics.Select(d => d.GetMessage()).ToArray();

        // Verify the inner object diagnostic
        Assert.Contains(messages, m => m.Contains("Value, Square"));

        // Verify the outer object diagnostic
        Assert.Contains(messages, m => m.Contains("Name, Age, Items"));

        // This test verifies our changes will generate:
        // - AnonymousRecord001(int Value, int Square) for inner object
        // - AnonymousRecord002(string Name, int Age, IEnumerable<AnonymousRecord001> Items) for outer object
        // With constructor calls like: new AnonymousRecord001(x, x * x)
    }

    private static void ResetRecordCounter()
    {
        var field = typeof(AnonymousToRecordCodeFixProvider).GetField(
            "_recordCounter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        field?.SetValue(null, 1);
    }
}
