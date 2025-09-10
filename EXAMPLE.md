# dotnet-cli-output build log

Command: /usr/local/share/dotnet/sdk/10.0.100-rc.1.25451.107/dotnet.dll build test-project --noconsolelogger
Time: 2025-09-09T21:49:56
Duration: 0.5s

This document contains build results in markdown tables. A peephole view for each error is also provided to aid comprehension of the problem.

## Projects

| Project | Errors | Warnings |
|---------|--------|----------|
| BrokenProject | 5 | 1 |

## Build Errors

| File | Line | Col | Code | Anchor | Lines |
|------|------|-----|------|--------|-------|
| test-project/Program.cs | 11 | 27 | CS0103 | #testprojectprogramcs1127 | 27-37 |
| test-project/Program.cs | 15 | 13 | CS1061 | #testprojectprogramcs1513 | 39-48 |
| test-project/Program.cs | 18 | 9 | CS0246 | #testprojectprogramcs189 | 50-59 |
| test-project/Program.cs | 18 | 33 | CS0246 | #testprojectprogramcs1833 | 61-70 |
| test-project/Program.cs | 24 | 20 | CS0103 | #testprojectprogramcs2420 | 72-82 |
| test-project/Program.cs | 28 | 16 | CS1998 | #testprojectprogramcs2816 | 84-96 |

### test-project/Program.cs:11:27 (lines 7-15):

```csharp
{
    static void Main(string[] args)
    {
        Console.WriteLine(undefinedVariable); // ← CS0103
        
        var str = "hello";
        str.NonExistentMethod();
```

### test-project/Program.cs:15:13 (lines 11-19):

```csharp
        Console.WriteLine(undefinedVariable);
        
        var str = "hello";
        str.NonExistentMethod(); // ← CS1061
        
        UndefinedType obj = new UndefinedType();
```

### test-project/Program.cs:18:9 (lines 14-22):

```csharp
        var str = "hello";
        str.NonExistentMethod();
        
        UndefinedType obj = new UndefinedType(); // ← CS0246
        
        Console.WriteLine("test", "extra", "args");
```

### test-project/Program.cs:18:33 (lines 14-22):

```csharp
        var str = "hello";
        str.NonExistentMethod();
        
        UndefinedType obj = new UndefinedType(); // ← CS0246
        
        Console.WriteLine("test", "extra", "args");
```

### test-project/Program.cs:24:20 (lines 21-28):

```csharp
        Console.WriteLine("test", "extra", "args");
        
        // This should reference Newtonsoft.Json types without using statement
        var json = JsonConvert.SerializeObject(new { test = "value" }); // ← CS0103
    }
    
    async Task BadAsyncMethod()
```

### test-project/Program.cs:28:16 (lines 24-32):

```csharp
        var json = JsonConvert.SerializeObject(new { test = "value" });
    }
    
    async Task BadAsyncMethod() // ← CS1998
    {
        Console.WriteLine("No await here");
    }
}
```



