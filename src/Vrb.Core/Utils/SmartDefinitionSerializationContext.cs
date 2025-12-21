using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.Logging;
using Vrb.Core.Utils;
using Vrb.Infrastructure;

namespace Vrb.Utils;

/// <summary>
/// Factory for creating a custom DefinitionSerializationContext that uses our loaded definitions
/// to resolve GUIDs to their concrete types instead of failing on abstract types.
/// 
/// This uses Reflection.Emit to dynamically generate a subclass of DefinitionSerializationContext
/// that overrides TryLocateDefinition with our smart lookup logic.
/// </summary>
internal static class SmartDefinitionSerializationContextFactory
{
    private static Type? _generatedType;
    private static readonly object _lock = new();

    /// <summary>
    /// Creates an instance of our dynamically-generated smart context.
    /// Falls back to DummyDefinitionSerializationContext if generation fails.
    /// </summary>
    public static object? Create(ILogger? logger = null)
    {
        var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
        if (vrageLibrary == null)
        {
            logger?.LogWarning("VRage.Library not loaded");
            return null;
        }

        // Ensure definitions are loaded
        var _ = DefinitionsHelper.Instance;
        if (!DefinitionsHelper.Instance.IsLoaded)
        {
            logger?.LogWarning("Definitions not loaded, falling back to DummyDefinitionSerializationContext");
            return CreateDummyContext(vrageLibrary);
        }

        try
        {
            // Try to create our smart context
            var contextType = GetOrCreateSmartContextType(vrageLibrary, logger);
            if (contextType == null)
            {
                logger?.LogWarning("Failed to create smart context type, falling back to DummyDefinitionSerializationContext");
                return CreateDummyContext(vrageLibrary);
            }

            return Activator.CreateInstance(contextType);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Error creating smart context, falling back to DummyDefinitionSerializationContext");
            return CreateDummyContext(vrageLibrary);
        }
    }

    private static object? CreateDummyContext(Assembly vrageLibrary)
    {
        var dummyType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
        return dummyType != null ? Activator.CreateInstance(dummyType) : null;
    }

    private static Type? GetOrCreateSmartContextType(Assembly vrageLibrary, ILogger? logger)
    {
        if (_generatedType != null) return _generatedType;

        lock (_lock)
        {
            if (_generatedType != null) return _generatedType;

            try
            {
                _generatedType = GenerateSmartContextType(vrageLibrary, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to generate smart context type");
            }

            return _generatedType;
        }
    }

    private static Type? GenerateSmartContextType(Assembly vrageLibrary, ILogger? logger)
    {
        // Get the base types we need
        var definitionSerializationContextType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.DefinitionSerializationContext");
        var definitionType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.Definition");

        if (definitionSerializationContextType == null || definitionType == null)
        {
            logger?.LogWarning("Required types not found");
            return null;
        }

        // Get our helper method that calls CreatePlaceholder via reflection
        var createSmartPlaceholderMethod = typeof(DefinitionLookupHelper).GetMethod("CreateSmartPlaceholder",
            BindingFlags.Public | BindingFlags.Static);

        if (createSmartPlaceholderMethod == null)
        {
            logger?.LogWarning("CreateSmartPlaceholder helper method not found");
            return null;
        }

        logger?.LogDebug("Generating SmartDefinitionSerializationContext type...");

        // Create a dynamic assembly and module
        var assemblyName = new AssemblyName("SmartDefinitionSerializationContextAssembly");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

        // Create the type that extends DefinitionSerializationContext
        var typeBuilder = moduleBuilder.DefineType(
            "Vrb.Generated.SmartDefinitionSerializationContext",
            TypeAttributes.Public | TypeAttributes.Class,
            definitionSerializationContextType);

        // Get the TryLocateDefinition method to override
        var tryLocateMethod = definitionSerializationContextType.GetMethod("TryLocateDefinition",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Guid), typeof(Type), definitionType.MakeByRefType() },
            null);

        if (tryLocateMethod == null)
        {
            logger?.LogWarning("TryLocateDefinition method not found");
            return null;
        }

        // Define the override for TryLocateDefinition
        var methodBuilder = typeBuilder.DefineMethod(
            "TryLocateDefinition",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(bool),
            new[] { typeof(Guid), typeof(Type), definitionType.MakeByRefType() });

        // Mark the out parameter
        methodBuilder.DefineParameter(3, ParameterAttributes.Out, "definition");

        // Generate the IL
        // The logic is:
        //   object result = DefinitionLookupHelper.CreateSmartPlaceholder(id, hintType);
        //   if (result == null) { definition = null; return false; }
        //   definition = (Definition)result;
        //   return true;
        var il = methodBuilder.GetILGenerator();

        // Local variables
        var resultLocal = il.DeclareLocal(typeof(object));  // Result from CreateSmartPlaceholder

        // Call CreateSmartPlaceholder(id, hintType)
        il.Emit(OpCodes.Ldarg_1);  // id (Guid)
        il.Emit(OpCodes.Ldarg_2);  // hintType (Type)
        il.Emit(OpCodes.Call, createSmartPlaceholderMethod);
        il.Emit(OpCodes.Stloc, resultLocal);

        // If result is null, return false
        var successLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brtrue_S, successLabel);

        // Return false and set definition = null
        il.Emit(OpCodes.Ldarg_3);  // out definition
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_0); // false
        il.Emit(OpCodes.Ret);

        // Success path: set definition and return true
        il.MarkLabel(successLabel);
        il.Emit(OpCodes.Ldarg_3);              // out definition
        il.Emit(OpCodes.Ldloc, resultLocal);   // result
        il.Emit(OpCodes.Castclass, definitionType);  // Cast to Definition
        il.Emit(OpCodes.Stind_Ref);

        // Return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Create the type
        var generatedType = typeBuilder.CreateType();
        logger?.LogDebug("Successfully generated SmartDefinitionSerializationContext type");

        return generatedType;
    }
}

/// <summary>
/// Static helper methods that can be called from IL-generated code.
/// </summary>
public static class DefinitionLookupHelper
{
    private static MethodInfo? _createPlaceholderMethod;
    private static readonly object _lock = new();

    /// <summary>
    /// Creates a definition placeholder using the game's DefinitionHelper.CreatePlaceholder,
    /// but with our smart type lookup to use concrete types instead of abstract ones.
    /// </summary>
    /// <param name="guid">The definition GUID.</param>
    /// <param name="hintType">The type hint from serialization (may be abstract).</param>
    /// <returns>The created Definition placeholder, or null if it cannot be created.</returns>
    public static object? CreateSmartPlaceholder(Guid guid, Type hintType)
    {
        // Determine the concrete type to use
        var concreteType = GetConcreteType(guid, hintType);
        if (concreteType == null)
        {
            return null;
        }

        // Get the CreatePlaceholder method via reflection
        var createMethod = GetCreatePlaceholderMethod();
        if (createMethod == null)
        {
            return null;
        }

        try
        {
            // Call CreatePlaceholder with the concrete type
            return createMethod.Invoke(null, new object[] { concreteType, guid });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the concrete type to use for creating a definition placeholder.
    /// Uses our DefinitionsHelper to look up the actual type for the GUID.
    /// </summary>
    private static Type? GetConcreteType(Guid guid, Type hintType)
    {
        // Try to get concrete type from our loaded definitions
        if (DefinitionsHelper.Instance.TryGetDefinitionType(guid, out var concreteType))
        {
            // Verify the concrete type is compatible with the hint type
            if (hintType.IsAssignableFrom(concreteType) && !concreteType.IsAbstract)
            {
                return concreteType;
            }
        }

        // Fallback: if hintType is not abstract, use it directly
        if (!hintType.IsAbstract)
        {
            return hintType;
        }

        // Can't determine a valid concrete type
        return null;
    }

    private static MethodInfo? GetCreatePlaceholderMethod()
    {
        if (_createPlaceholderMethod != null) return _createPlaceholderMethod;

        lock (_lock)
        {
            if (_createPlaceholderMethod != null) return _createPlaceholderMethod;

            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            if (vrageLibrary == null) return null;

            var definitionHelperType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.DefinitionHelper");
            if (definitionHelperType == null) return null;

            _createPlaceholderMethod = definitionHelperType.GetMethod("CreatePlaceholder",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Type), typeof(Guid) },
                null);

            return _createPlaceholderMethod;
        }
    }
}

