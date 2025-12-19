using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Reflection;

namespace vrb.Utils;

public static class JsonExtensions
{
    public static void IgnoreRefStructs(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind == JsonTypeInfoKind.Object)
        {
            for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                var property = typeInfo.Properties[i];
                if (property.PropertyType.IsByRefLike || property.PropertyType.Name.Contains("Span")) // Simple check for Span<T>
                {
                    typeInfo.Properties.RemoveAt(i);
                }
            }
        }
    }
}

