using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace AnonymousToRecord.Tests;

public class ExampleCaseCodeFixTests
{
    [Fact]
    public async Task ExampleCase_ShouldGenerateSequentialRecordNames()
    {
        const string testCode = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace AnonymousToRecord.Try;

            public class Class1
            {
                public void Test()
                {
                    List<int> arr = [1, 2, 3];
                    var obj = new
                    {
                        Name = "John",
                        Age = 30,
                        Foos = new[] { "A", "B", "C" },
                        Bars = arr.Select(x => new { Value = x, Square = x * x }),
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
            MetadataReference.CreateFromFile(
                typeof(System.Collections.Generic.List<>).Assembly.Location
            ),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references
        );

        var analyzer = new AnonymousToRecordAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // Should detect both the outer anonymous object and the inner one
        Assert.Equal(2, atrDiagnostics.Length);

        // Expected generated records:
        // 1. AnonymousRecord001 for the inner object: new { Value = x, Square = x * x }
        // 2. AnonymousRecord002 for the outer object: new { Name, Age, Foos, Bars }

        // Check messages contain expected property names
        var messages = atrDiagnostics.Select(d => d.GetMessage()).ToArray();

        var outerObjectMessage = messages.FirstOrDefault(m =>
            m.Contains("Name") && m.Contains("Age") && m.Contains("Foos") && m.Contains("Bars")
        );
        var innerObjectMessage = messages.FirstOrDefault(m =>
            m.Contains("Value") && m.Contains("Square")
        );

        Assert.NotNull(outerObjectMessage);
        Assert.NotNull(innerObjectMessage);

        // Verify they are Info level diagnostics
        Assert.All(atrDiagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
    }

    [Fact]
    public async Task MultipleAnonymousObjects_ShouldGetSequentialNames()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var obj1 = new { Name = "John", Age = 30 };
                    var obj2 = new { City = "Tokyo", Country = "Japan" };
                    var obj3 = new { X = 1, Y = 2, Z = 3 };
                }
            }
            """;

        // Reset counter for predictable test results
        ResetRecordCounter();

        var syntaxTree = CSharpSyntaxTree.ParseText(testCode);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }
        );

        var analyzer = new AnonymousToRecordAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer)
        );

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // Should detect all three anonymous objects
        Assert.Equal(3, atrDiagnostics.Length);

        // Expected generated records would be:
        // AnonymousRecord001, AnonymousRecord002, AnonymousRecord_003

        var messages = atrDiagnostics.Select(d => d.GetMessage()).ToArray();

        Assert.Contains(messages, m => m.Contains("Name, Age"));
        Assert.Contains(messages, m => m.Contains("City, Country"));
        Assert.Contains(messages, m => m.Contains("X, Y, Z"));
    }

    private static void ResetRecordCounter()
    {
        // Use reflection to reset the static counter for testing
        var field = typeof(AnonymousToRecordCodeFixProvider).GetField(
            "_recordCounter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        field?.SetValue(null, 1);
    }
}
