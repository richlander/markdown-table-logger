using System;
using System.Threading.Tasks;

namespace BrokenProject;

class Program
{
    static void Main(string[] args)
    {
        // This should cause CS0103 - undefined variable
        Console.WriteLine(undefinedVariable);
        
        // This should cause CS1061 - missing member
        var str = "hello";
        str.NonExistentMethod();
        
        // This should cause CS0246 - missing type
        UndefinedType obj = new UndefinedType();
        
        // This should cause CS1501 - wrong number of arguments
        Console.WriteLine("test", "extra", "args");
        
        // This should reference Newtonsoft.Json types without using statement
        var json = JsonConvert.SerializeObject(new { test = "value" });
    }
    
    // This should cause CS1998 - async without await
    async Task BadAsyncMethod()
    {
        Console.WriteLine("No await here");
    }
}