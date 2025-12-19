using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using vrb.Infrastructure;

namespace vrb.Utils;

public static class ObjectGraphHydrator
{
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        MaxDepth = 1024 
    };

    public static object? Hydrate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var context = new HydrationContext();
        return HydrateElement(doc.RootElement, context);
    }

    private static object? HydrateElement(JsonElement element, HydrationContext context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) return l; // Prefer Long/Int
                return element.GetDouble(); // Fallback
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Array:
                return HydrateList(element, context);
            case JsonValueKind.Object:
                return HydrateObject(element, context);
            default:
                throw new NotSupportedException($"Unsupported JsonValueKind: {element.ValueKind}");
        }
    }

    private static object? HydrateObject(JsonElement element, HydrationContext context)
    {
        // 1. Check for Reference ($ref)
        if (element.TryGetProperty("$ref", out var refProp))
        {
            var refId = refProp.GetString();
            if (refId != null && context.References.TryGetValue(refId, out var existing))
            {
                return existing;
            }
            throw new JsonException($"Reference $ref='{refId}' not found.");
        }

        // 2. Resolve Type
        string? typeName = null;
        if (element.TryGetProperty("$type", out var typeProp))
        {
            typeName = typeProp.GetString();
        }

        // If no type, it might be a raw Dictionary or dynamic object?
        // For now, assume everything relevant has $type.
        if (typeName == null)
        {
            // Check if it's a List wrapped by STJ with $values
            if (element.TryGetProperty("$values", out var valuesProp) && valuesProp.ValueKind == JsonValueKind.Array)
            {
                // It's a list but we don't know the type. Wait, if it has $id it might be referenced.
                // But without $type we can't create specific List<T>. Return List<object?>.
                var list = HydrateList(valuesProp, context);
            // Register ID if present
            if (element.TryGetProperty("$id", out var idPropList))
            {
                var id = idPropList.GetString();
                if (id != null) context.References[id] = list!;
            }
                return list;
            }

            // Fallback: Return Dictionary<string, object>
            var dict = new Dictionary<string, object?>();
            
            // Register ID if present (Dicts can be referenced too)
            if (element.TryGetProperty("$id", out var idPropDict))
            {
                var id = idPropDict.GetString();
                if (id != null) context.References[id] = dict;
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name == "$id") continue;
                dict[prop.Name] = HydrateElement(prop.Value, context);
            }
            return dict;
        }

        // 3. Create Instance
        Type? type = ResolveType(typeName);
        if (type == null)
        {
            throw new TypeLoadException($"Could not resolve type: {typeName}");
        }

        object instance;
        if (type == typeof(string)) return element.ToString(); // Should be handled by ValueKind.String
        if (type.IsArray)
        {
            throw new NotSupportedException("Direct array with $type not supported yet (use List logic).");
        }

        try 
        {
            // Use RuntimeHelpers to bypass constructors (deserialization style)
            instance = RuntimeHelpers.GetUninitializedObject(type);
        }
        catch 
        {
            // Fallback for strings/primitives wrapped in object?
            instance = Activator.CreateInstance(type)!; 
        }

        // 4. Register ID ($id) BEFORE populating to handle cycles
        if (element.TryGetProperty("$id", out var idProp))
        {
            var id = idProp.GetString();
            if (id != null)
            {
                context.References[id] = instance;
            }
        }

        // 5. Populate Fields/Properties
        PopulateObject(instance, type, element, context);

        return instance;
    }

    private static void PopulateObject(object instance, Type type, JsonElement element, HydrationContext context)
    {
        foreach (var jsonProp in element.EnumerateObject())
        {
            var name = jsonProp.Name;
            if (name == "$id" || name == "$type" || name == "$values") continue; // Metadata

            // Try Field first (serialization usually prefers fields)
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var value = HydrateElement(jsonProp.Value, context);
                // Convert value to target type
                var converted = ConvertValue(value, field.FieldType);
                field.SetValue(instance, converted);
                continue;
            }

            // Try Property
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                var value = HydrateElement(jsonProp.Value, context);
                var converted = ConvertValue(value, prop.PropertyType);
                prop.SetValue(instance, converted);
            }
        }
    }

    private static object? HydrateList(JsonElement element, HydrationContext context)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(HydrateElement(item, context));
        }
        return list; // Return generic list, ConvertValue will adapt it
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return null;
        
        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType)) return value;

        // Handle numeric conversions (Int64 -> Int32, Double -> Single)
        if (value is long l)
        {
            if (targetType == typeof(int)) return (int)l;
            if (targetType == typeof(byte)) return (byte)l;
            if (targetType == typeof(short)) return (short)l;
            if (targetType == typeof(ushort)) return (ushort)l;
            if (targetType == typeof(uint)) return (uint)l;
            if (targetType == typeof(ulong)) return (ulong)l;
            if (targetType == typeof(sbyte)) return (sbyte)l;
            if (targetType == typeof(double)) return (double)l;
            if (targetType == typeof(float)) return (float)l;
            if (targetType == typeof(decimal)) return (decimal)l;
        }

        if (value is int i2)
        {
            if (targetType == typeof(long)) return (long)i2;
            if (targetType == typeof(short)) return (short)i2;
            if (targetType == typeof(ushort)) return (ushort)i2;
            if (targetType == typeof(byte)) return (byte)i2;
            if (targetType == typeof(uint)) return (uint)i2;
            if (targetType == typeof(double)) return (double)i2;
            if (targetType == typeof(float)) return (float)i2;
            if (targetType == typeof(decimal)) return (decimal)i2;
        }

        if (value is double d)
        {
             if (targetType == typeof(float)) return (float)d;
             if (targetType == typeof(decimal)) return (decimal)d;
             if (targetType == typeof(int)) return (int)d;
             if (targetType == typeof(long)) return (long)d;
        }

        // Handle Enums (from int or string)
        if (targetType.IsEnum)
        {
            if (value is string s) return Enum.Parse(targetType, s);
            if (value is long l3) return Enum.ToObject(targetType, l3);
            if (value is int i) return Enum.ToObject(targetType, i);
        }

        // Handle Guid
        if (targetType == typeof(Guid) && value is string sGuid)
        {
            return Guid.Parse(sGuid);
        }

        // Handle Nullable<T>
        if (Nullable.GetUnderlyingType(targetType) is Type underlyingType)
        {
            return ConvertValue(value, underlyingType);
        }

        // Handle List/Array conversions
        if (value is List<object?> genericList)
        {
            if (targetType.IsArray)
            {
                var elemType = targetType.GetElementType()!;
                var array = Array.CreateInstance(elemType, genericList.Count);
                for (int i = 0; i < genericList.Count; i++)
                {
                    array.SetValue(ConvertValue(genericList[i], elemType), i);
                }
                return array;
            }
            if (targetType.IsGenericType && (targetType.GetGenericTypeDefinition() == typeof(List<>) || targetType.GetGenericTypeDefinition() == typeof(IList<>)))
            {
                var elemType = targetType.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(elemType);
                var list = Activator.CreateInstance(listType) as IList;
                foreach (var item in genericList)
                {
                    list!.Add(ConvertValue(item, elemType));
                }
                return list;
            }
            
            // Generic IList implementation support (e.g. KeyedList<K, V>)
            var iListInterface = targetType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
            if (iListInterface != null)
            {
                var elemType = iListInterface.GetGenericArguments()[0];
                try 
                {
                    var list = Activator.CreateInstance(targetType) as IList;
                    if (list != null)
                    {
                        foreach (var item in genericList)
                        {
                            list.Add(ConvertValue(item, elemType));
                        }
                        return list;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ObjectGraphHydrator] Failed to convert to generic IList: {ex}");
                    // If we can't instantiate or populate, fall through
                }
            }

            if (targetType == typeof(HashSet<string>)) 
            {
                return new HashSet<string>(genericList.OfType<string>());
            }
        }

        return value; 
    }

    private static Type? ResolveType(string typeName)
    {
        // Cache this lookup in production
        foreach (var asm in GameAssemblyManager.LoadedAssemblies)
        {
            var type = asm.GetType(typeName);
            if (type != null) return type;
        }
        return Type.GetType(typeName); 
    }

    private class HydrationContext
    {
        public Dictionary<string, object> References { get; } = new();

        public HydrationContext()
        {
        }
    }
}
