using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace AnonymousToRecord.Tests;

public class NestedAnonymousTypeTests
{
    [Fact]
    public async Task NestedAnonymousType_ShouldGenerateProperRecordNames()
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
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer)
        );

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // Should detect both anonymous objects
        Assert.Equal(2, atrDiagnostics.Length);

        // Check that the diagnostic messages mention proper type names
        var messages = atrDiagnostics.Select(d => d.GetMessage()).ToArray();

        // Ensure no diagnostic messages contain the problematic format
        Assert.All(
            messages,
            m =>
            {
                Assert.DoesNotContain("<<anonymous type:", m);
                Assert.DoesNotContain(">>", m);
            }
        );
    }

    private static void ResetRecordCounter()
    {
        // Use reflection to reset the static counter for testing
        var field = typeof(AnonymousToRecordCodeFixProvider).GetField(
            "_recordCounter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        field?.SetValue(null, 1);

        // Also reset the anonymous type names dictionary
        var dictField = typeof(AnonymousToRecordCodeFixProvider).GetField(
            "_anonymousTypeNames",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        if (dictField?.GetValue(null) is System.Collections.IDictionary dict)
        {
            dict.Clear();
        }
    }
}
