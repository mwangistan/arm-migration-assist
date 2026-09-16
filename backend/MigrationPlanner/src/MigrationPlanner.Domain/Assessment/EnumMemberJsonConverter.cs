using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MigrationPlanner.Domain.Assessment;

/// <summary>
/// STJ converter that maps enum values to and from the string set declared by
/// <see cref="EnumMemberAttribute"/> so kebab-case schema tokens (e.g. "emulation-only")
/// bind cleanly to PascalCase .NET enum members.
/// </summary>
public sealed class EnumMemberJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, TEnum> FromStringMap = BuildFromString();
    private static readonly IReadOnlyDictionary<TEnum, string> ToStringMap = BuildToString();

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string for enum {typeof(TEnum).Name}, found {reader.TokenType}.");
        }

        var value = reader.GetString();
        if (value is null || !FromStringMap.TryGetValue(value, out var result))
        {
            throw new JsonException($"Value '{value}' is not valid for enum {typeof(TEnum).Name}.");
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!ToStringMap.TryGetValue(value, out var text))
        {
            throw new JsonException($"Enum {typeof(TEnum).Name} value {value} has no EnumMember mapping.");
        }

        writer.WriteStringValue(text);
    }

    private static Dictionary<string, TEnum> BuildFromString()
    {
        var map = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        foreach (var (name, member) in Members())
        {
            map[name] = member;
        }

        return map;
    }

    private static Dictionary<TEnum, string> BuildToString()
    {
        var map = new Dictionary<TEnum, string>();
        foreach (var (name, member) in Members())
        {
            map[member] = name;
        }

        return map;
    }

    private static IEnumerable<(string Name, TEnum Value)> Members()
    {
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = field.GetCustomAttribute<EnumMemberAttribute>();
            var name = attr?.Value ?? field.Name;
            yield return (name, (TEnum)field.GetValue(null)!);
        }
    }
}

/// <summary>Cache of converter instances keyed by enum type.</summary>
public static class EnumMemberJsonConverterFactory
{
    private static readonly ConcurrentDictionary<Type, JsonConverter> Cache = new();

    public static JsonConverter For<TEnum>() where TEnum : struct, Enum =>
        Cache.GetOrAdd(typeof(TEnum), _ => new EnumMemberJsonConverter<TEnum>());
}
