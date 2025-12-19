using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace vrb.Utils;

public static class DebugObjectConverter
{
    public static object? ToDebugObject(object? obj)
    {
        return Convert(obj, null, 0);
    }

    private static object? Convert(object? obj, HashSet<object>? visited, int depth)
    {
        if (obj == null) return null;
        if (depth > 50) return "[Max Depth Reached]";

        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime))
            return obj;

        if (obj is Type || type.Name == "IdentityId" || type.IsEnum)
            return obj.ToString();

        if (!type.IsValueType)
        {
            visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (visited.Contains(obj)) return "[Cycle]";
            visited.Add(obj);
        }

        if (obj is IDictionary dictionary)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var item in dictionary)
            {
                object? key = null;
                object? value = null;

                if (item is DictionaryEntry de)
                {
                    key = de.Key;
                    value = de.Value;
                }
                else if (item != null)
                {
                    var itemType = item.GetType();
                    if (itemType.IsGenericType && itemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    {
                        key = itemType.GetProperty("Key")?.GetValue(item);
                        value = itemType.GetProperty("Value")?.GetValue(item);
                    }
                    else
                    {
                        key = $"entry_{dict.Count}";
                        value = item;
                    }
                }

                dict[key?.ToString() ?? "null"] = Convert(value, visited, depth + 1);
            }
            return dict;
        }

        if (obj is IEnumerable enumerable)
        {
            var list = new List<object?>();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count++ > 1000) { list.Add("[Truncated...]"); break; }
                list.Add(Convert(item, visited, depth + 1));
            }
            return list;
        }

        var resultDict = new Dictionary<string, object?>();
        resultDict["$type"] = type.FullName;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType.Name.Contains("Span")) continue;
            if (prop.GetCustomAttributes().Any(a => a.GetType().Name == "NoSerializeAttribute")) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            try
            {
                resultDict[prop.Name] = Convert(prop.GetValue(obj), visited, depth + 1);
            }
            catch (Exception ex)
            {
                resultDict[prop.Name] = $"[Error: {ex.Message}]";
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType.Name.Contains("Span")) continue;
            if (field.GetCustomAttributes().Any(a => a.GetType().Name == "NoSerializeAttribute")) continue;

            try
            {
                resultDict[field.Name] = Convert(field.GetValue(obj), visited, depth + 1);
            }
            catch (Exception ex)
            {
                resultDict[field.Name] = $"[Error: {ex.Message}]";
            }
        }

        return resultDict;
    }

    private class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
