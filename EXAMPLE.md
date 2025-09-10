# dotnet-cli-output build log

Command: /usr/local/share/dotnet/sdk/10.0.100-rc.1.25451.107/dotnet.dll build test-project --noconsolelogger
Time: 2025-09-09T22:57:50
Duration: 0.5s

This document contains build results in markdown tables. A peephole view for each error is also provided to aid comprehension of the problem. The first two sections are intended to be read sequentially. The third section can be read using `tail`/`head` or with `sed` using the `Anchor` and `Lines` ranges in the "Build Errors" table, enabling random access.

## Projects

| Project | Errors | Warnings |
|---------|--------|----------|
| BrokenProject | 5 | 1 |

## Build Errors

| File | Line | Col | Code | Anchor | Lines |
|------|------|-----|------|--------|-------|
| test-project/Program.cs | 11 | 27 | CS0103 | #test-projectprogramcs1127 | 29-50 |
| test-project/Program.cs | 15 | 13 | CS1061 | #test-projectprogramcs1513 | 52-71 |
| test-project/Program.cs | 18 | 9 | CS0246 | #test-projectprogramcs189 | 73-91 |
| test-project/Program.cs | 18 | 33 | CS0246 | #test-projectprogramcs1833 | 93-111 |
| test-project/Program.cs | 24 | 20 | CS0103 | #test-projectprogramcs2420 | 113-135 |
| test-project/Program.cs | 28 | 16 | CS1998 | #test-projectprogramcs2816 | 137-158 |

## Error Details

### test-project/Program.cs:11:27

- File: test-project/Program.cs
- Lines: 7-15
- Error: CS0103
- Message: The name 'undefinedVariable' does not exist in the current context

```csharp
{
    static void Main(string[] args)
    {
        Console.WriteLine(undefinedVariable); // ← CS0103
        
        var str = "hello";
        str.NonExistentMethod();
```

**Referenced symbols:**
- `Console` - .NET Libraries (System.Console)
- `WriteLine` - .NET Libraries
- `undefinedVariable` - undefined symbol


### test-project/Program.cs:15:13

- File: test-project/Program.cs
- Lines: 11-19
- Error: CS1061
- Message: 'string' does not contain a definition for 'NonExistentMethod' and no accessible extension method 'NonExistentMethod' accepting a first argument of type 'string' could be found (are you missing a using directive or an assembly reference?)

```csharp
        Console.WriteLine(undefinedVariable);
        
        var str = "hello";
        str.NonExistentMethod(); // ← CS1061
        
        UndefinedType obj = new UndefinedType();
```

**Referenced symbols:**
- `NonExistentMethod` - undefined symbol
- `str` - test-project/Program.cs:14,13


### test-project/Program.cs:18:9

- File: test-project/Program.cs
- Lines: 14-22
- Error: CS0246
- Message: The type or namespace name 'UndefinedType' could not be found (are you missing a using directive or an assembly reference?)

```csharp
        var str = "hello";
        str.NonExistentMethod();
        
        UndefinedType obj = new UndefinedType(); // ← CS0246
        
        Console.WriteLine("test", "extra", "args");
```

**Referenced symbols:**
- `UndefinedType` - undefined symbol


### test-project/Program.cs:18:33

- File: test-project/Program.cs
- Lines: 14-22
- Error: CS0246
- Message: The type or namespace name 'UndefinedType' could not be found (are you missing a using directive or an assembly reference?)

```csharp
        var str = "hello";
        str.NonExistentMethod();
        
        UndefinedType obj = new UndefinedType(); // ← CS0246
        
        Console.WriteLine("test", "extra", "args");
```

**Referenced symbols:**
- `UndefinedType` - undefined symbol


### test-project/Program.cs:24:20

- File: test-project/Program.cs
- Lines: 21-28
- Error: CS0103
- Message: The name 'JsonConvert' does not exist in the current context

```csharp
        Console.WriteLine("test", "extra", "args");
        
        // This should reference Newtonsoft.Json types without using statement
        var json = JsonConvert.SerializeObject(new { test = "value" }); // ← CS0103
    }
    
    async Task BadAsyncMethod()
```

**Referenced symbols:**
- `JsonConvert` - external package (Newtonsoft.Json)
- `SerializeObject` - external package (Newtonsoft.Json)
- `test` - undefined symbol
- `var` - .NET Libraries


### test-project/Program.cs:28:16

- File: test-project/Program.cs
- Lines: 24-32
- Error: CS1998
- Message: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.

```csharp
        var json = JsonConvert.SerializeObject(new { test = "value" });
    }
    
    async Task BadAsyncMethod() // ← CS1998
    {
        Console.WriteLine("No await here");
    }
}
```

**Referenced symbols:**
- `Task` - .NET Libraries (System.Threading.Tasks)




