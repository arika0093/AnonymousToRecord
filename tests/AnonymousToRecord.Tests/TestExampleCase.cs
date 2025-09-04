using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AnonymousToRecord.Tests;

public class TestExampleCase
{
    [Fact]
    public async Task TestRealExampleFile()
    {
        const string exampleCode = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace AnonymousToRecord.Try;

            public class Class1
            {
            	public void Test()
            	{
            		List<int> arr = new List<int> { 1, 2, 3 };
            		var obj = new {
            			Name = "John",
            			Age = 30,
            			Foos = new[] { "A", "B", "C" },
            			Bars = arr.Select(x => new {
            				Value = x,
            				Square = x * x
            			})
            		};
            	}
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(exampleCode);
        
        var references = new[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
        };
        
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references);

        var analyzer = new AnonymousToRecordAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // Should detect both the outer anonymous object and the inner one
        Assert.Equal(2, atrDiagnostics.Length);
        
        // Check messages contain expected property names
        var messages = atrDiagnostics.Select(d => d.GetMessage()).ToArray();
        
        var outerObjectMessage = messages.FirstOrDefault(m => m.Contains("Name") && m.Contains("Age") && m.Contains("Foos") && m.Contains("Bars"));
        var innerObjectMessage = messages.FirstOrDefault(m => m.Contains("Value") && m.Contains("Square"));
        
        Assert.NotNull(outerObjectMessage);
        Assert.NotNull(innerObjectMessage);
        
        // Verify they are Info level diagnostics
        Assert.All(atrDiagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
    }
}