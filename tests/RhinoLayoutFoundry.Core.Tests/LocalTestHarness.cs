#if LOCAL_TEST_HARNESS
using System.Collections;
using System.Reflection;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class FactAttribute : Attribute;

    internal static class Assert
    {
        public static void True(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Expected true but found false.");
            }
        }

        public static void True(bool condition, string? message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message ?? "Expected true but found false.");
            }
        }

        public static void False(bool condition)
        {
            if (condition)
            {
                throw new InvalidOperationException("Expected false but found true.");
            }
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (expected is IEnumerable expectedValues && actual is IEnumerable actualValues &&
                expected is not string && actual is not string)
            {
                if (!expectedValues.Cast<object?>().SequenceEqual(actualValues.Cast<object?>()))
                {
                    throw new InvalidOperationException("The sequences are not equal.");
                }

                return;
            }

            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"Expected '{expected}' but found '{actual}'.");
            }
        }

        public static void Empty(IEnumerable values)
        {
            var enumerator = values.GetEnumerator();
            try
            {
                if (enumerator.MoveNext())
                {
                    throw new InvalidOperationException("Expected an empty sequence.");
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        public static void Single(IEnumerable values)
        {
            var count = values.Cast<object>().Take(2).Count();
            if (count != 1)
            {
                throw new InvalidOperationException($"Expected one item but found {count}.");
            }
        }

        public static void Contains(string expected, string actual, StringComparison comparison)
        {
            if (!actual.Contains(expected, comparison))
            {
                throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
            }
        }

        public static void Contains<T>(T expected, IEnumerable<T> values)
        {
            if (!values.Contains(expected))
            {
                throw new InvalidOperationException($"Expected the sequence to contain '{expected}'.");
            }
        }

        public static void Contains<T>(IEnumerable<T> values, Func<T, bool> predicate)
        {
            if (!values.Any(predicate))
            {
                throw new InvalidOperationException("No sequence item matched the predicate.");
            }
        }

        public static TException Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }

            throw new InvalidOperationException($"Expected exception '{typeof(TException).Name}'.");
        }
    }
}

internal static class Program
{
    private static int Main()
    {
        var failures = new List<string>();
        var tests = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<Xunit.FactAttribute>() is not null)
                .Select(method => (Type: type, Method: method)))
            .OrderBy(test => test.Type.FullName, StringComparer.Ordinal)
            .ThenBy(test => test.Method.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var test in tests)
        {
            try
            {
                var instance = Activator.CreateInstance(test.Type)
                    ?? throw new InvalidOperationException($"Could not create '{test.Type.FullName}'.");
                test.Method.Invoke(instance, null);
                Console.WriteLine($"PASS {test.Type.Name}.{test.Method.Name}");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                failures.Add($"FAIL {test.Type.Name}.{test.Method.Name}: {exception.InnerException.Message}");
            }
            catch (Exception exception)
            {
                failures.Add($"FAIL {test.Type.Name}.{test.Method.Name}: {exception.Message}");
            }
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        Console.WriteLine($"Executed {tests.Length} tests; {failures.Count} failed.");
        return failures.Count == 0 ? 0 : 1;
    }
}
#endif
