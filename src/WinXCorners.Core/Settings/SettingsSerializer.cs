using System.Text.Json;
using System.Text.Json.Serialization;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Settings;

/// <summary>
/// Converts <see cref="ApplicationSettings"/> to and from the settings.json format.
/// </summary>
/// <remarks>
/// Output keeps the v1.4.0 contract exactly (same property names, enums as numbers, indented) so a user
/// can downgrade without losing their file. Input is lenient about formatting a person might produce by
/// hand-editing, but it never repairs values: callers must run <see cref="SettingsSanitizer"/> afterwards.
/// </remarks>
public static class SettingsSerializer
{
    // JsonSerializerOptions is immutable once used and safe to share; this is not mutable state.
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Indented JSON with the v1.4.0 property names and enum values written as numbers.</summary>
    public static string Serialize(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, Options);
    }

    /// <summary>
    /// Parses settings.json content. Accepts comments, trailing commas, unknown properties, a leading
    /// byte-order mark and enum values given as numbers or (case-insensitive) names. The result is
    /// <b>not</b> sanitized and may contain nulls, out-of-range numbers and undefined enum values.
    /// </summary>
    /// <exception cref="JsonException">The text is not valid JSON for this type, or its root is <c>null</c>.</exception>
    public static ApplicationSettings Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // Notepad and friends may save with a BOM; as a char it is not JSON whitespace.
        var text = json.StartsWith('﻿') ? json[1..] : json;
        return JsonSerializer.Deserialize<ApplicationSettings>(text, Options)
            ?? throw new JsonException("The settings file contains null instead of an object.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new LenientEnumConverterFactory());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Reads enums from numbers or names (any case), always writes the number.</summary>
    private sealed class LenientEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;
        }
    }

    private sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number when reader.TryGetInt32(out var number):
                    return (TEnum)Enum.ToObject(typeof(TEnum), number);
                case JsonTokenType.String:
                    // A String token never yields null.
                    var name = reader.GetString()!;
                    // Only plain names: Enum.TryParse would also accept "7" or "Ctrl, Shift" as text.
                    if (Enum.GetNames<TEnum>().Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        return Enum.Parse<TEnum>(name.Trim(), ignoreCase: true);
                    }

                    var shown = name.Length > 40 ? name[..40] + "..." : name;
                    throw new JsonException($"\"{shown}\" is not a valid {typeof(TEnum).Name} value.");
                default:
                    throw new JsonException($"Expected a number or name for {typeof(TEnum).Name}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
