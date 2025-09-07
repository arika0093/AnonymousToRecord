# AnonymousToRecord

C# Analyzer that suggests converting anonymous objects to record types for better type safety and reusability.

## Features

- **Automatic Detection**: Identifies anonymous objects in your code that can be converted to record types
- **Code Fix Provider**: Provides a one-click fix to convert anonymous objects to records
- **Batch Processing**: Can handle multiple anonymous objects in a single operation
- **Nested Anonymous Types**: Supports nested anonymous objects and converts them to nested records
- **Generic Type Support**: Handles generic types containing anonymous objects (e.g., `IEnumerable<AnonymousType>`)
- **Smart Naming**: Automatically generates meaningful record names

## Installation

Install via NuGet Package Manager:

```
Install-Package AnonymousToRecord
```

Or via .NET CLI:

```
dotnet add package AnonymousToRecord
```

Or add to your project file:

```xml
<PackageReference Include="AnonymousToRecord" Version="0.1.0-alpha" />
```

## Usage

The analyzer automatically detects anonymous objects in your C# code and suggests converting them to records.

### Before

```csharp
var person = new { Name = "John", Age = 30 };
var people = new List<object>
{
    new { Name = "Alice", Age = 25 },
    new { Name = "Bob", Age = 35 }
};
```

### After (with code fix applied)

```csharp
public record AnonymousRecord001
{
    public required string Name { get; init; }
    public required int Age { get; init; }
}

var person = new AnonymousRecord001 { Name = "John", Age = 30 };
var people = new List<AnonymousRecord001>
{
    new AnonymousRecord001 { Name = "Alice", Age = 25 },
    new AnonymousRecord001 { Name = "Bob", Age = 35 }
};
```

### How to Apply the Fix

1. Place your cursor on the squiggly line under the anonymous object
2. Press `Ctrl+.` (or `Cmd+.` on Mac) to open the Quick Actions menu
3. Select "Convert to record"

## Benefits of Using Records

- **Type Safety**: Records provide compile-time type checking
- **Immutability**: Records are immutable by default with `init` accessors
- **Value Equality**: Records have built-in value-based equality
- **Deconstruction**: Records support pattern matching and deconstruction
- **Reusability**: Records can be reused across your application
- **Performance**: Better performance compared to anonymous objects in some scenarios

## Configuration

The analyzer follows standard Roslyn analyzer configuration. You can configure it in your `.editorconfig`:

```ini
# Disable the analyzer for specific files
[*.generated.cs]
dotnet_analyzer_rule.ATR001.severity = none

# Change severity level
dotnet_analyzer_rule.ATR001.severity = warning
```

## Diagnostic Information

- **Rule ID**: ATR001
- **Category**: Design
- **Default Severity**: Info
- **Title**: Anonymous object can be converted to record

## Examples

### Simple Anonymous Object

```csharp
// Before
var point = new { X = 10, Y = 20 };

// After
public record AnonymousRecord001
{
    public required int X { get; init; }
    public required int Y { get; init; }
}
var point = new AnonymousRecord001 { X = 10, Y = 20 };
```

### Nested Anonymous Objects

```csharp
// Before
var user = new 
{ 
    Name = "John", 
    Address = new { Street = "123 Main St", City = "Anytown" } 
};

// After
public record AnonymousRecord001
{
    public required string Street { get; init; }
    public required string City { get; init; }
}

public record AnonymousRecord002
{
    public required string Name { get; init; }
    public required AnonymousRecord001 Address { get; init; }
}

var user = new AnonymousRecord002 
{ 
    Name = "John", 
    Address = new AnonymousRecord001 { Street = "123 Main St", City = "Anytown" } 
};
```

### Collections with Anonymous Objects

```csharp
// Before
var items = new[]
{
    new { Id = 1, Name = "Item1" },
    new { Id = 2, Name = "Item2" }
};

// After
public record AnonymousRecord001
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}

var items = new[]
{
    new AnonymousRecord001 { Id = 1, Name = "Item1" },
    new AnonymousRecord001 { Id = 2, Name = "Item2" }
};
```

## Requirements

- .NET Standard 2.0 or later
- C# 9.0 or later (for record support)
- Visual Studio 2019 16.8+ or compatible IDE

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Changelog

### 0.1.0-alpha
- Initial release
- Basic anonymous object to record conversion
- Support for nested anonymous objects
- Batch processing capabilities
- Generic type support