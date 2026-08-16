using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace WorkAgents.Core.Authoring;

/// <summary>
/// 埋め込まれた schemas/*.schema.json を GUI 用の <see cref="UiSchemaDocument"/> に読み解く (案A/B)。
/// フィールド定義を GUI 側に書き写さずに済ませるため、スキーマを唯一の真実として扱う。
/// x-ui / x-source は JSON Schema の未知キーなので、VS Code などの検証には影響しない。
/// </summary>
public static class UiSchemaCatalog
{
    private static readonly ConcurrentDictionary<string, UiSchemaDocument> Cache = new(StringComparer.Ordinal);

    /// <summary>読み込めるスキーマ ID。</summary>
    public static IReadOnlyList<string> Ids { get; } = ["agent", "team", "graph", "workspace", "workflow"];

    /// <summary>スキーマ ID (agent / team / graph / workspace / workflow) で取得する。</summary>
    public static UiSchemaDocument Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Cache.GetOrAdd(id, Load);
    }

    private static UiSchemaDocument Load(string id)
    {
        var assembly = typeof(UiSchemaCatalog).Assembly;
        var resourceName = ResourceNameFor(assembly, id)
            ?? throw new InvalidOperationException($"schema resource not found: {id}.schema.json");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"schema resource could not be opened: {resourceName}");
        using var document = JsonDocument.Parse(stream);
        return Parse(id, document.RootElement);
    }

    private static string? ResourceNameFor(Assembly assembly, string id)
    {
        var suffix = $".{id}.schema.json";
        return assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));
    }

    internal static UiSchemaDocument Parse(string id, JsonElement root)
    {
        var rootUi = UiOf(root);

        // definitions を先に読む。nodes/edges の $ref がここを指すため。
        var definitions = new Dictionary<string, UiField>(StringComparer.Ordinal);
        if (root.TryGetProperty("definitions", out var definitionsElement) &&
            definitionsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var definition in definitionsElement.EnumerateObject())
            {
                definitions[definition.Name] = ParseField(
                    definition.Name,
                    definition.Name,
                    definition.Value,
                    required: false,
                    definitions: definitions);
            }
        }

        var fields = ParseProperties(root, prefix: string.Empty, definitions);

        return new UiSchemaDocument
        {
            Id = id,
            Title = Text(root, "title") ?? id,
            Description = Text(root, "description"),
            Groups = StringArray(rootUi, "groups"),
            Fields = fields,
            Definitions = definitions,
            Deprecated = Bool(rootUi, "deprecated") ?? false,
            DeprecationNote = Text(rootUi, "deprecationNote"),
        };
    }

    private static IReadOnlyList<UiField> ParseProperties(
        JsonElement schema,
        string prefix,
        IReadOnlyDictionary<string, UiField> definitions)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<UiField>();
        }

        var required = new HashSet<string>(StringArray(schema, "required"), StringComparer.Ordinal);
        var fields = new List<UiField>();
        foreach (var property in properties.EnumerateObject())
        {
            var path = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
            fields.Add(ParseField(property.Name, path, property.Value, required.Contains(property.Name), definitions));
        }
        return fields.OrderBy(field => field.Order).ToArray();
    }

    private static UiField ParseField(
        string name,
        string path,
        JsonElement schema,
        bool required,
        IReadOnlyDictionary<string, UiField> definitions)
    {
        // $ref は definitions 内で解決する。名前とパスだけ差し替えて中身を共有する。
        if (Text(schema, "$ref") is { } reference)
        {
            var target = reference.Split('/').Last();
            if (definitions.TryGetValue(target, out var resolved))
            {
                return resolved with { Name = name, Path = path, Required = required };
            }
        }

        var ui = UiOf(schema);
        var type = DetermineType(schema);
        var enumValues = EnumValues(schema);
        var widget = Text(ui, "widget") ?? DefaultWidget(type, enumValues.Count > 0 || Text(schema, "x-source") is not null);

        var children = Array.Empty<UiField>();
        UiField? item = null;

        if (type == UiFieldType.Object)
        {
            children = (UiField[])ParseProperties(schema, path, definitions);
        }
        else if (type == UiFieldType.Array && schema.TryGetProperty("items", out var items))
        {
            item = ParseField(name, $"{path}[]", items, required: false, definitions);
        }
        else if (type == UiFieldType.Map &&
                 schema.TryGetProperty("additionalProperties", out var additional) &&
                 additional.ValueKind == JsonValueKind.Object)
        {
            item = ParseField(name, $"{path}[]", additional, required: false, definitions);
        }

        return new UiField
        {
            Name = name,
            Path = path,
            Type = type,
            Description = Text(schema, "description"),
            EnumValues = enumValues,
            EnumLabels = StringMap(ui, "enumLabels"),
            Source = Text(schema, "x-source"),
            Widget = widget,
            Order = Int(ui, "order") ?? 500,
            Group = Text(ui, "group"),
            Required = required || (Bool(ui, "required") ?? false),
            Advanced = Bool(ui, "advanced") ?? false,
            Deprecated = Bool(ui, "deprecated") ?? false,
            DefaultHint = Text(ui, "defaultHint"),
            Placeholder = Text(ui, "placeholder"),
            Unit = Text(ui, "unit"),
            Step = Double(ui, "step"),
            Minimum = Double(schema, "minimum"),
            Maximum = Double(schema, "maximum"),
            SupportsReferences = Bool(ui, "supportsReferences") ?? false,
            ShowWhen = ShowWhen(ui),
            Fields = children,
            Item = item,
            ItemLabel = Text(ui, "itemLabel"),
        };
    }

    private static UiFieldType DetermineType(JsonElement schema)
    {
        if (schema.TryGetProperty("const", out _))
        {
            return UiFieldType.Constant;
        }

        var type = Text(schema, "type");
        if (type is null)
        {
            // type 省略でも additionalProperties があれば辞書として扱う。
            return schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.Object
                ? UiFieldType.Map
                : UiFieldType.String;
        }

        if (string.Equals(type, "object", StringComparison.Ordinal))
        {
            var hasProperties = schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object;
            var hasFreeKeys = schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.Object;
            return !hasProperties && hasFreeKeys ? UiFieldType.Map : UiFieldType.Object;
        }

        return type switch
        {
            "array" => UiFieldType.Array,
            "boolean" => UiFieldType.Boolean,
            "integer" => UiFieldType.Integer,
            "number" => UiFieldType.Number,
            _ => UiFieldType.String,
        };
    }

    private static string DefaultWidget(UiFieldType type, bool isChoice) => type switch
    {
        UiFieldType.Constant => "hidden",
        UiFieldType.Boolean => "switch",
        UiFieldType.Integer or UiFieldType.Number => "number",
        UiFieldType.Array => "list",
        UiFieldType.Object or UiFieldType.Map => "group",
        _ => isChoice ? "select" : "text",
    };

    private static IReadOnlyList<string> EnumValues(JsonElement schema)
    {
        if (!schema.TryGetProperty("enum", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        return values.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ShowWhen(JsonElement ui)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (ui.ValueKind != JsonValueKind.Object ||
            !ui.TryGetProperty("showWhen", out var showWhen) ||
            showWhen.ValueKind != JsonValueKind.Object)
        {
            return result;
        }
        foreach (var condition in showWhen.EnumerateObject())
        {
            if (condition.Value.ValueKind == JsonValueKind.Array)
            {
                result[condition.Name] = condition.Value.EnumerateArray()
                    .Select(value => value.GetString())
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .ToArray();
            }
        }
        return result;
    }

    // ------------------------------------------------------------- JSON 補助

    private static JsonElement UiOf(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("x-ui", out var ui)
            ? ui
            : default;

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int? Int(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var result)
            ? result
            : null;

    private static double? Double(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetDouble(out var result)
            ? result
            : null;

    private static IReadOnlyList<string> StringArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        return value.EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> StringMap(JsonElement element, string name)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return result;
        }
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[property.Name] = property.Value.GetString()!;
            }
        }
        return result;
    }
}
