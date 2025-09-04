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
