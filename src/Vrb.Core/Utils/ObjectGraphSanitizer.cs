using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace vrb.Utils;

public static class ObjectGraphSanitizer
{
    public static object? Sanitize(object? root)
    {
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        return SanitizeRecursive(root, visited, 0);
    }

    private static object? SanitizeRecursive(object? obj, Dictionary<object, object> visited, int depth)
    {
        if (obj == null) return null;
        if (depth > 10) return "[Max Depth Reached]";

        var type = obj.GetType();

        // 1. Pass through Primitives and Strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(TimeSpan))
        {
            return obj;
        }

        // 2. Handle Enums
        if (type.IsEnum) return obj;

        // 3. Ignore Ref Structs / Pointers
        if (type.IsByRefLike || type.IsPointer)
        {
            return null;
        }

        // 4. Check for cycles/visited (Only for Ref Types)
        // Value types are boxed, so ReferenceEquality fails on every access. 
        // We rely on depth limit to stop recursion for Value Types.
        if (!type.IsValueType && visited.TryGetValue(obj, out var sanitized))
        {
            return sanitized;
        }

        // 5. Handle Arrays / Collections
        if (obj is Array array)
        {
             var list = new List<object?>(array.Length);
             if (!type.IsValueType) visited[obj] = list; // Only track Ref types
             
             foreach (var item in array)
             {
                 list.Add(SanitizeRecursive(item, visited, depth + 1));
             }
             return list;
        }
        else if (obj is IEnumerable enumerable)
        {
            var list = new List<object?>();
            if (!type.IsValueType) visited[obj] = list; // Only track Ref types

            foreach (var item in enumerable)
            {
                list.Add(SanitizeRecursive(item, visited, depth + 1));
            }
            return list;
        }

        // 6. Handle Complex Objects -> Dictionary<string, object?>
        var dict = new Dictionary<string, object?>();
        if (!type.IsValueType) visited[obj] = dict; // Only track Ref types

        // Add Type Metadata
        dict["$type"] = type.FullName;

        // Fields
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ShouldSkip(field.FieldType)) continue;
            try
            {
                dict[field.Name] = SanitizeRecursive(field.GetValue(obj), visited, depth + 1);
            }
            catch { dict[field.Name] = null; }
        }

        // Properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
             if (prop.GetIndexParameters().Length > 0) continue; // Skip Indexers
             if (!prop.CanRead) continue;
             if (ShouldSkip(prop.PropertyType)) continue;

             try
             {
                 dict[prop.Name] = SanitizeRecursive(prop.GetValue(obj), visited, depth + 1);
             }
             catch { dict[prop.Name] = null; }
        }

        return dict;
    }

    private static bool ShouldSkip(Type type)
    {
        if (type.IsByRefLike) return true;
        if (type.IsPointer) return true;
        if (type.Name.Contains("Span")) return true; // Safety net
        if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
        return false;
    }

    private class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
