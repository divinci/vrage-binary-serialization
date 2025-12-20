using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Vrb.Utils;

public static class DebugObjectConverter
{
    public static object? ToDebugObject(object? obj)
    {
        var context = new ConversionContext();
        return Convert(obj, context, 0);
    }

    private class ConversionContext
    {
        public Dictionary<object, string> ReferenceIds { get; } = new(ReferenceEqualityComparer.Instance);
        public int NextId { get; set; } = 1;
    }

    private static object? Convert(object? obj, ConversionContext context, int depth)
    {
        if (obj == null) return null;
        if (depth > 200) return "[Max Depth Reached]"; // Increased depth limit

        var type = obj.GetType();
        // Primitives pass through
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime))
            return obj;

        if (obj is Type || type.Name == "IdentityId" || type.IsEnum)
            return obj.ToString();

        // Handle References
        if (!type.IsValueType)
        {
            if (context.ReferenceIds.TryGetValue(obj, out var existingId))
            {
                // Return Reference
                return new Dictionary<string, object?> { { "$ref", existingId } };
            }
            
            // Register New ID
            var newId = context.NextId++.ToString();
            context.ReferenceIds[obj] = newId;

            // Note: We need to inject this ID into the result container.
            // We'll do it after creating the container.
        }

        // --- Collections ---
        
        if (obj is IDictionary dictionary)
        {
            var dict = new Dictionary<string, object?>();
            
            // Add ID if applicable
            if (context.ReferenceIds.TryGetValue(obj, out var id))
            {
                dict["$id"] = id;
            }

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
                }

                if (key != null)
                {
                    dict[key.ToString() ?? "null"] = Convert(value, context, depth + 1);
                }
            }
            return dict;
        }

        if (obj is IEnumerable enumerable)
        {
            var list = new List<object?>();
            // var count = 0; // Truncation disabled for full validation
            foreach (var item in enumerable)
            {
                // if (count++ > 10000) { list.Add("[Truncated...]"); break; }
                list.Add(Convert(item, context, depth + 1));
            }

            // Wrap in object if we need to preserve ID
            if (context.ReferenceIds.TryGetValue(obj, out var id))
            {
                return new Dictionary<string, object?> 
                { 
                    { "$id", id },
                    { "$values", list }
                };
            }

            return list;
        }

        // --- Complex Objects ---

        var resultDict = new Dictionary<string, object?>();
        if (context.ReferenceIds.TryGetValue(obj, out var objId))
        {
            resultDict["$id"] = objId;
        }
        resultDict["$type"] = type.FullName;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType.Name.Contains("Span")) continue;
            if (prop.GetCustomAttributes().Any(a => a.GetType().Name == "NoSerializeAttribute")) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            try
            {
                resultDict[prop.Name] = Convert(prop.GetValue(obj), context, depth + 1);
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
                resultDict[field.Name] = Convert(field.GetValue(obj), context, depth + 1);
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