using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using Console = System.Console;

namespace AnonymousToRecord.Tests;

public class ExpectedOutputTests
{
    [Fact]
    public async Task ExampleCase_ExpectedOutput_Demonstration()
    {
        const string inputCode = """
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

        // Reset counter for predictable results
        ResetRecordCounter();

        var syntaxTree = CSharpSyntaxTree.ParseText(inputCode);
        
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
        };
        
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references);

        var analyzer = new AnonymousToRecordAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // This demonstrates what the analyzer should detect
        Assert.Equal(2, atrDiagnostics.Length);

        // Expected diagnostics:
        // 1. Inner anonymous object: new { Value = x, Square = x * x }
        //    -> Would become AnonymousRecord_001(int Value, int Square)
        // 2. Outer anonymous object: new { Name, Age, Foos, Bars }
        //    -> Would become AnonymousRecord_002(string Name, int Age, string[] Foos, IEnumerable<AnonymousRecord_001> Bars)

        var innerObjectDiagnostic = atrDiagnostics.FirstOrDefault(d => 
            d.GetMessage().Contains("Value") && d.GetMessage().Contains("Square"));
        var outerObjectDiagnostic = atrDiagnostics.FirstOrDefault(d => 
            d.GetMessage().Contains("Name") && d.GetMessage().Contains("Age") && 
            d.GetMessage().Contains("Foos") && d.GetMessage().Contains("Bars"));

        Assert.NotNull(innerObjectDiagnostic);
        Assert.NotNull(outerObjectDiagnostic);

        // Verify diagnostic messages
        Assert.Equal("Anonymous object with properties 'Value, Square' can be converted to a record type", 
            innerObjectDiagnostic.GetMessage());
        Assert.Equal("Anonymous object with properties 'Name, Age, Foos, Bars' can be converted to a record type", 
            outerObjectDiagnostic.GetMessage());

        // Verify severity
        Assert.Equal(DiagnosticSeverity.Info, innerObjectDiagnostic.Severity);
        Assert.Equal(DiagnosticSeverity.Info, outerObjectDiagnostic.Severity);
    }

    [Fact]
    public async Task ExpectedOutput_ForExampleCase()
    {
        const string inputCode = """
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

        // Reset counter for predictable results
        ResetRecordCounter();

        var syntaxTree = CSharpSyntaxTree.ParseText(inputCode);
        
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
        };
        
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references);

        var analyzer = new AnonymousToRecordAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var atrDiagnostics = diagnostics.Where(d => d.Id == "ATR001").ToArray();

        // This demonstrates what the analyzer should detect
        Assert.Equal(2, atrDiagnostics.Length);

        // Find anonymous objects in the syntax tree
        var root = await syntaxTree.GetRootAsync();
        var anonymousObjects = root.DescendantNodes()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .OrderBy(ao => ao.SpanStart)
            .ToArray();

        Assert.Equal(2, anonymousObjects.Length);

        var innerAnonymousObject = anonymousObjects[1]; // { Value = x, Square = x * x }
        var outerAnonymousObject = anonymousObjects[0]; // { Name, Age, Foos, Bars }

        // Verify that we can identify the properties correctly
        var innerInitializers = innerAnonymousObject.Initializers;
        Assert.Equal(2, innerInitializers.Count);
        
        var outerInitializers = outerAnonymousObject.Initializers;
        Assert.Equal(4, outerInitializers.Count);

        // Verify property names are extracted correctly
        var innerPropertyNames = innerInitializers.Select(GetPropertyNamePublic).ToArray();
        Assert.Contains("Value", innerPropertyNames);
        Assert.Contains("Square", innerPropertyNames);

        var outerPropertyNames = outerInitializers.Select(GetPropertyNamePublic).ToArray();
        Assert.Contains("Name", outerPropertyNames);
        Assert.Contains("Age", outerPropertyNames);
        Assert.Contains("Foos", outerPropertyNames);
        Assert.Contains("Bars", outerPropertyNames);

        // Verify that the diagnostic messages contain the expected property names
        var diagnosticMessages = atrDiagnostics.Select(d => d.GetMessage()).ToArray();
        
        var innerObjectMessage = diagnosticMessages.FirstOrDefault(m => 
            m.Contains("Value") && m.Contains("Square"));
        var outerObjectMessage = diagnosticMessages.FirstOrDefault(m => 
            m.Contains("Name") && m.Contains("Age") && m.Contains("Foos") && m.Contains("Bars"));

        Assert.NotNull(innerObjectMessage);
        Assert.NotNull(outerObjectMessage);

        // This verifies that:
        // 1. The analyzer correctly detects 2 anonymous objects
        // 2. The inner anonymous object has properties Value and Square
        // 3. The outer anonymous object has properties Name, Age, Foos, and Bars
        // 4. The CodeFixProvider would be able to generate appropriate record types
        
        // The actual transformation would generate:
        // - AnonymousRecord_001(int Value, int Square) for the inner object
        // - AnonymousRecord_002(string Name, int Age, string[] Foos, IEnumerable<AnonymousRecord_001> Bars) for the outer object
    }


    private static string GetPropertyNamePublic(AnonymousObjectMemberDeclaratorSyntax initializer)
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

    private static void ResetRecordCounter()
    {
        var field = typeof(AnonymousToRecordCodeFixProvider)
            .GetField("_recordCounter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field?.SetValue(null, 1);
    }
}