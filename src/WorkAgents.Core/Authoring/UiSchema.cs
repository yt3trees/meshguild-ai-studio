namespace WorkAgents.Core.Authoring;

/// <summary>フォーム上での値の扱い。JSON Schema の type を GUI 側の都合に寄せたもの。</summary>
public enum UiFieldType
{
    String,
    Number,
    Integer,
    Boolean,
    Object,
    Array,

    /// <summary>additionalProperties でキーが自由な辞書 (subgraphs、layout)。</summary>
    Map,

    /// <summary>const 指定。書き手が触る余地がない。</summary>
    Constant,
}

/// <summary>
/// フォームに出す項目 1 つ分のメタデータ (案A/B)。
/// ラベルと説明はスキーマの description、出し分けは x-ui / x-source から来る。
/// GUI 側にフィールド定義を二重持ちさせないための型。
/// </summary>
public sealed record UiField
{
    /// <summary>JSON のプロパティ名。YAML のキー名でもある。</summary>
    public required string Name { get; init; }

    /// <summary>ルートからのドット区切りパス。配列要素は <c>members[].agent</c> のように表す。</summary>
    public required string Path { get; init; }

    public UiFieldType Type { get; init; } = UiFieldType.String;

    /// <summary>スキーマの description。そのままヘルプ文言として表示する。</summary>
    public string? Description { get; init; }

    /// <summary>enum の候補。空なら列挙ではない。</summary>
    public IReadOnlyList<string> EnumValues { get; init; } = Array.Empty<string>();

    /// <summary>enum の候補に付ける表示名 (x-ui.enumLabels)。無い候補は値をそのまま出す。</summary>
    public IReadOnlyDictionary<string, string> EnumLabels { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 選択肢の取得元 (x-source)。agents / teams / graphs / skills / nodes / team-agents /
    /// subgraphs / code-nodes。ここが埋まっている項目は自由入力ではなくドロップダウンにする。
    /// </summary>
    public string? Source { get; init; }

    /// <summary>text / textarea / select / multiselect / number / switch / list / table / canvas / hidden。</summary>
    public string Widget { get; init; } = "text";

    public int Order { get; init; } = 500;

    /// <summary>所属するフォームのセクション名 (x-ui.group)。</summary>
    public string? Group { get; init; }

    /// <summary>親の required[] または x-ui.required による必須指定。</summary>
    public bool Required { get; init; }

    /// <summary>既定で畳んでおく項目。</summary>
    public bool Advanced { get; init; }

    public bool Deprecated { get; init; }

    /// <summary>省略時の挙動を示す文言 (x-ui.defaultHint)。プレースホルダーの代わりに出す。</summary>
    public string? DefaultHint { get; init; }

    public string? Placeholder { get; init; }

    /// <summary>数値項目の単位表示 (秒、USD など)。</summary>
    public string? Unit { get; init; }

    public double? Step { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    /// <summary>${...} 参照を書ける項目。GUI は参照の入力補助を出す。</summary>
    public bool SupportsReferences { get; init; }

    /// <summary>
    /// 兄弟プロパティの値による表示条件 (x-ui.showWhen)。
    /// 例: <c>{ "kind": ["loop"] }</c> なら kind が loop のときだけ表示する。
    /// graph.schema.json の allOf/if-then が表す「この kind のときだけ意味を持つ」を GUI に写したもの。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ShowWhen { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>Object のときの子項目。order 昇順。</summary>
    public IReadOnlyList<UiField> Fields { get; init; } = Array.Empty<UiField>();

    /// <summary>Array / Map のときの要素定義。</summary>
    public UiField? Item { get; init; }

    /// <summary>Array の 1 件を指す呼び名 (x-ui.itemLabel)。「メンバーを追加」のように使う。</summary>
    public string? ItemLabel { get; init; }

    public bool IsHidden => string.Equals(Widget, "hidden", StringComparison.Ordinal);

    /// <summary>ドロップダウンにできる項目か。enum か x-source のどちらかがあれば選択式にできる。</summary>
    public bool IsChoice => EnumValues.Count > 0 || !string.IsNullOrEmpty(Source);

    /// <summary>enum 候補の表示名。x-ui.enumLabels に無ければ値をそのまま返す。</summary>
    public string LabelFor(string value)
        => EnumLabels.TryGetValue(value, out var label) ? label : value;

    /// <summary>
    /// 兄弟の現在値を踏まえて、この項目を表示すべきか判定する。
    /// <paramref name="siblingValue"/> はプロパティ名から現在値を引く関数。
    /// </summary>
    public bool IsVisible(Func<string, string?> siblingValue)
    {
        ArgumentNullException.ThrowIfNull(siblingValue);
        if (IsHidden)
        {
            return false;
        }
        foreach (var condition in ShowWhen)
        {
            var current = siblingValue(condition.Key);
            if (current is null || !condition.Value.Contains(current, StringComparer.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public UiField? Field(string name)
        => Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal));
}

/// <summary>スキーマ 1 本を GUI 用に読み解いた結果 (案A/B)。</summary>
public sealed record UiSchemaDocument
{
    /// <summary>team / graph / agent / workspace / workflow。</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>フォームのセクションの並び (ルートの x-ui.groups)。</summary>
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();

    public IReadOnlyList<UiField> Fields { get; init; } = Array.Empty<UiField>();

    /// <summary>definitions 配下 (graph の node / edge)。</summary>
    public IReadOnlyDictionary<string, UiField> Definitions { get; init; }
        = new Dictionary<string, UiField>(StringComparer.Ordinal);

    /// <summary>この形式自体が非推奨か (workflow.yaml)。</summary>
    public bool Deprecated { get; init; }

    public string? DeprecationNote { get; init; }

    public UiField? Field(string name)
        => Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal));

    public UiField? Definition(string name)
        => Definitions.TryGetValue(name, out var field) ? field : null;

    /// <summary>指定セクションに属する項目を order 昇順で返す。</summary>
    public IReadOnlyList<UiField> FieldsInGroup(string group)
        => Fields
            .Where(field => string.Equals(field.Group, group, StringComparison.Ordinal) && !field.IsHidden)
            .OrderBy(field => field.Order)
            .ToArray();
}
