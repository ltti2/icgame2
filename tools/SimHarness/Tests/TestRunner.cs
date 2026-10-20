using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public static class TestRunner
{
    public static int Main(string[] args)
    {
        string filter = args.Length > 0 ? args[0] : null;
        int passed = 0, failed = 0;
        var fixtures = typeof(IceCoast.Tests.TestGames).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<TestFixtureAttribute>() != null).OrderBy(t => t.Name);

        foreach (var type in fixtures)
        {
            var setup = type.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);
            foreach (var test in type.GetMethods().Where(m => m.GetCustomAttribute<TestAttribute>() != null))
            {
                string name = $"{type.Name}.{test.Name}";
                if (filter != null && !name.Contains(filter)) continue;
                var sw = Stopwatch.StartNew();
                try
                {
                    var instance = Activator.CreateInstance(type);
                    setup?.Invoke(instance, null);
                    test.Invoke(instance, null);
                    passed++;
                    Console.WriteLine($"  PASS  {name} ({sw.ElapsedMilliseconds} ms)");
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine($"  FAIL  {name}: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}");
                    if (!(e.InnerException is AssertionException)) Console.WriteLine(e.InnerException?.StackTrace);
                }
            }
        }
        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
