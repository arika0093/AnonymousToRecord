using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AnonymousToRecord.Tests;

public class AnonymousToRecordAnalyzerTests
{
    private static async Task<Diagnostic[]> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        
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
        return diagnostics.Where(d => d.Id == "ATR001").ToArray();
    }

    [Fact]
    public async Task AnalyzeAnonymousObject_ShouldReportDiagnostic()
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

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Single(diagnostics);
        Assert.Equal("ATR001", diagnostics[0].Id);
        Assert.Contains("Name, Age", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task AnalyzeAnonymousObject_WithSingleProperty_ShouldReportDiagnostic()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var obj = new { Name = "John" };
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Single(diagnostics);
        Assert.Equal("ATR001", diagnostics[0].Id);
        Assert.Contains("Name", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task AnalyzeAnonymousObject_WithExpressionProperty_ShouldReportDiagnostic()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var name = "John";
                    var obj = new { name, Age = 30 };
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Single(diagnostics);
        Assert.Equal("ATR001", diagnostics[0].Id);
        Assert.Contains("name, Age", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task AnalyzeEmptyAnonymousObject_ShouldNotReportDiagnostic()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var obj = new { };
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AnalyzeRegularObject_ShouldNotReportDiagnostic()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var obj = new System.Object();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AnalyzeNestedAnonymousObject_ShouldReportMultipleDiagnostics()
    {
        const string testCode = """
            class TestClass
            {
                void TestMethod()
                {
                    var obj = new { Name = "John", Address = new { Street = "123 Main", City = "NY" } };
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("ATR001", d.Id));
    }

    [Fact]
    public async Task AnalyzeAnonymousObject_WithArrayProperties_ShouldReportDiagnostic()
    {
        const string testCode = """
            using System.Collections.Generic;
            using System.Linq;
            
            class TestClass
            {
                void TestMethod()
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

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("ATR001", d.Id));
        
        // Check that both outer and inner anonymous objects are detected
        var outerObject = diagnostics.FirstOrDefault(d => d.GetMessage().Contains("Name, Age, Foos, Bars"));
        var innerObject = diagnostics.FirstOrDefault(d => d.GetMessage().Contains("Value, Square"));
        
        Assert.NotNull(outerObject);
        Assert.NotNull(innerObject);
    }

    [Fact]
    public async Task AnalyzeAnonymousObject_WithComplexArrayTypes_ShouldReportDiagnostic()
    {
        const string testCode = """
            using System.Collections.Generic;
            
            class TestClass
            {
                void TestMethod()
                {
                    var obj = new {
                        StringArray = new[] { "hello", "world" },
                        IntList = new List<int> { 1, 2, 3 },
                        DoubleArray = new double[] { 1.5, 2.7, 3.14 },
                        BoolFlags = new bool[] { true, false, true }
                    };
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(testCode);

        Assert.Single(diagnostics);
        Assert.Equal("ATR001", diagnostics[0].Id);
        Assert.Contains("StringArray, IntList, DoubleArray, BoolFlags", diagnostics[0].GetMessage());
    }
}