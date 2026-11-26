using System.Collections;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace Raider.Plugin.Game;

/// <summary>
/// Reads and calls Raid's interop proxies by member name (see ADR 0004).
/// <para>
/// Interop proxies expose IL2CPP fields as properties, so <see cref="Get"/> treats both alike. Every miss throws
/// <see cref="GameMemberMissingException"/> naming the member, so a game update that renames something shows up as
/// a clear Chore failure rather than a crash.
/// </para>
/// </summary>
internal static class Il2CppReflection
{
    private const BindingFlags AnyDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static Type? _il2CppActionType;
    private static MethodInfo? _convertDelegate;

    /// <summary>Reads a property or field, looking through base types.</summary>
    public static object? Get(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, AnyDeclared);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                return Unwrap(() => property.GetValue(target));
            }

            var field = type.GetField(name, AnyDeclared);
            if (field != null)
            {
                return field.GetValue(target);
            }
        }

        throw new GameMemberMissingException($"{target.GetType().FullName}.{name}");
    }

    /// <summary>Reads a member that must not be null.</summary>
    public static object GetRequired(object target, string name) =>
        Get(target, name) ?? throw new GameStateException($"{target.GetType().Name}.{name} is null");

    public static T GetValue<T>(object target, string name) => (T)Convert.ChangeType(Get(target, name), typeof(T))!;

    /// <summary>Calls a method, picking the overload whose parameter types accept the arguments.</summary>
    public static object? Call(object target, string name, params object?[] args)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            foreach (var method in type.GetMethods(AnyDeclared))
            {
                if (method.Name == name && Accepts(method.GetParameters(), args))
                {
                    return Unwrap(() => method.Invoke(target, args));
                }
            }
        }

        throw new GameMemberMissingException($"{target.GetType().FullName}.{name}({args.Length} args)");
    }

    /// <summary>Creates an instance of a game type with the constructor that accepts the arguments.</summary>
    public static object New(Type type, params object?[] args)
    {
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = constructor.GetParameters();
            // Interop proxies also have a (IntPtr pointer) constructor that wraps an existing object
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(IntPtr))
            {
                continue;
            }

            if (Accepts(parameters, args))
            {
                return Unwrap(() => constructor.Invoke(args))!;
            }
        }

        throw new GameMemberMissingException($"{type.FullName}..ctor({args.Length} args)");
    }

    /// <summary>
    /// Enumerates an IL2CPP collection: lists by index, anything else (dictionaries, key collections) through its
    /// IL2CPP enumerator. Dictionary entries come back as KeyValuePair proxies with Key and Value.
    /// </summary>
    public static IEnumerable<object> Items(object? collection)
    {
        if (collection == null)
        {
            yield break;
        }

        if (collection is IEnumerable managed and not string)
        {
            foreach (var item in managed)
            {
                if (item != null)
                {
                    yield return item;
                }
            }

            yield break;
        }

        var type = collection.GetType();
        // A dictionary keyed by int also has get_Item(int), but that takes a key, not a position,
        // so only real lists may be walked by index
        var isDictionary = type.GetMethod("ContainsKey") != null;
        var indexer = isDictionary ? null : type.GetMethod("get_Item", new[] { typeof(int) });
        if (indexer != null && type.GetProperty("Count") is { } countProperty)
        {
            var count = Convert.ToInt32(countProperty.GetValue(collection));
            for (var i = 0; i < count; i++)
            {
                var item = Unwrap(() => indexer.Invoke(collection, new object[] { i }));
                if (item != null)
                {
                    yield return item;
                }
            }

            yield break;
        }

        var enumerator = Call(collection, "GetEnumerator")
            ?? throw new GameStateException($"{type.Name}.GetEnumerator() returned null");
        while (Convert.ToBoolean(Call(enumerator, "MoveNext")))
        {
            var current = Get(enumerator, "Current");
            if (current != null)
            {
                yield return current;
            }
        }
    }

    /// <summary>Wraps a managed callback as an Il2CppSystem.Action the game can call.</summary>
    public static object ToIl2CppAction(Action action)
    {
        if (_convertDelegate == null)
        {
            _il2CppActionType = Type.GetType("Il2CppSystem.Action, Il2Cppmscorlib", throwOnError: true)!;
            _convertDelegate = typeof(DelegateSupport).GetMethod(nameof(DelegateSupport.ConvertDelegate))!
                .MakeGenericMethod(_il2CppActionType);
        }

        return Unwrap(() => _convertDelegate.Invoke(null, new object[] { action }))
            ?? throw new GameStateException("Could not convert a callback to Il2CppSystem.Action");
    }

    /// <summary>Whether an Il2CppSystem.Nullable proxy holds a value.</summary>
    public static bool HasValue(object? nullable) =>
        nullable != null && Convert.ToBoolean(Get(nullable, "hasValue"));

    private static bool Accepts(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length != args.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            if (args[i] == null ? parameterType.IsValueType : !parameterType.IsInstanceOfType(args[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reflection wraps the game's own exceptions; surface the IL2CPP one.</summary>
    private static object? Unwrap(Func<object?> invoke)
    {
        try
        {
            return invoke();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new GameCallException(ex.InnerException.Message, ex.InnerException);
        }
    }
}

/// <summary>A member Raider expects on a game type is not there, most likely after a game update.</summary>
internal sealed class GameMemberMissingException : Exception
{
    public GameMemberMissingException(string member)
        : base($"Raid has no {member} (the game may have been updated)")
    {
    }
}

/// <summary>The game's state is not what Raider expects (for example, a model object is missing).</summary>
internal sealed class GameStateException : Exception
{
    public GameStateException(string message) : base(message)
    {
    }
}

/// <summary>The game's own code threw while Raider called it.</summary>
internal sealed class GameCallException : Exception
{
    public GameCallException(string message, Exception inner) : base(message, inner)
    {
    }
}
