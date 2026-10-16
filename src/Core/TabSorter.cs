using System.Globalization;

namespace Tab.Core;

/// <summary>What a sort rule compares.</summary>
public enum SortKind
{
    /// <summary>The weight of the resolved group.</summary>
    Group,
    /// <summary>The rendered text, by code unit (ignoring case unless told otherwise).</summary>
    Alpha,
    /// <summary>The rendered number, rounded to 5 decimals and clamped to ±1e9; text that is not a number sorts last.</summary>
    Numeric,
    /// <summary>The position of the rendered text in the rule's order list; texts not in the list sort after it.</summary>
    Ordered,
}

/// <summary>One compiled sort rule. <see cref="Value"/> is the placeholder template its keys are rendered from (empty for group).</summary>
public sealed record SortRule(SortKind Kind, string Value, bool Descending, bool IgnoreCase, string[] Order);

/// <summary>One player to sort: the name (the last tie breaker), the group weight, and one rendered key per rule (index-aligned with the rules;
/// the key of a group rule is not read).</summary>
public sealed record SortEntry(string Name, int Weight, string[] Keys);

/// <summary>Compiles the sort chain and orders players by it. Pure: no game calls, so it runs in tests.</summary>
public static class TabSorter
{
    /// <summary>The largest magnitude of a numeric key; larger values are clamped to it.</summary>
    public const double NumericLimit = 1e9;

    /// <summary>Decimals kept by a numeric key.</summary>
    public const int NumericDecimals = 5;

    /// <summary>Compiles config rules. A rule with an unknown kind, or a numeric or ordered rule without a value, is reported and skipped.</summary>
    public static List<SortRule> Compile(IEnumerable<SortRuleConfig> rules, List<string> problems)
    {
        var compiled = new List<SortRule>();
        foreach (SortRuleConfig rule in rules)
        {
            if (rule is null) continue;
            switch (rule.Kind.ToLowerInvariant())
            {
                case "group":
                    compiled.Add(new SortRule(SortKind.Group, "", rule.Descending ?? true, true, []));
                    break;
                case "alpha":
                case "text":
                    compiled.Add(new SortRule(SortKind.Alpha, rule.Value.Length == 0 ? "%tab_name%" : rule.Value,
                        rule.Descending ?? false, rule.IgnoreCase, []));
                    break;
                case "numeric":
                    if (rule.Value.Length == 0) problems.Add("a numeric sort rule has no value; it is skipped");
                    else compiled.Add(new SortRule(SortKind.Numeric, rule.Value, rule.Descending ?? true, true, []));
                    break;
                case "ordered":
                    if (rule.Value.Length == 0) problems.Add("an ordered sort rule has no value; it is skipped");
                    else compiled.Add(new SortRule(SortKind.Ordered, rule.Value, rule.Descending ?? false, rule.IgnoreCase, [.. rule.Order]));
                    break;
                default:
                    problems.Add($"sort rule kind \"{rule.Kind}\" is unknown (group, alpha, numeric, ordered); it is skipped");
                    break;
            }
        }
        return compiled;
    }

    /// <summary>Orders the entries. Returns the indexes of <paramref name="entries"/> from first to last in the list. The sort is stable: entries
    /// equal under every rule and name keep their input order.</summary>
    public static int[] Order(IReadOnlyList<SortRule> rules, IReadOnlyList<SortEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(entries);
        var indexes = new int[entries.Count];
        for (int i = 0; i < indexes.Length; i++) indexes[i] = i;
        // Insertion sort keeps the order of equal entries and needs no comparer allocation; a tab list has at most a few hundred entries.
        for (int i = 1; i < indexes.Length; i++)
        {
            int current = indexes[i];
            int j = i - 1;
            while (j >= 0 && Compare(rules, entries[indexes[j]], entries[current]) > 0)
            {
                indexes[j + 1] = indexes[j];
                j--;
            }
            indexes[j + 1] = current;
        }
        return indexes;
    }

    /// <summary>Compares two entries under the rules, then by name (ignoring case, then ordinal).</summary>
    public static int Compare(IReadOnlyList<SortRule> rules, SortEntry a, SortEntry b)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            SortRule rule = rules[i];
            int c = rule.Kind switch
            {
                SortKind.Group => CompareDirected(a.Weight.CompareTo(b.Weight), rule.Descending),
                SortKind.Alpha => CompareDirected(CompareText(KeyAt(a, i), KeyAt(b, i), rule.IgnoreCase), rule.Descending),
                SortKind.Numeric => CompareNumeric(KeyAt(a, i), KeyAt(b, i), rule.Descending),
                SortKind.Ordered => CompareDirected(OrderPosition(rule, KeyAt(a, i)).CompareTo(OrderPosition(rule, KeyAt(b, i))), rule.Descending),
                _ => 0,
            };
            if (c != 0) return c;
        }
        int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        return byName != 0 ? byName : string.CompareOrdinal(a.Name, b.Name);
    }

    /// <summary>The numeric value a key stands for after the rounding and clamping of a numeric rule, or NaN when it is not a number.</summary>
    public static double ParseNumeric(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value)) return double.NaN;
        value = Math.Clamp(value, -NumericLimit, NumericLimit);
        return Math.Round(value, NumericDecimals, MidpointRounding.AwayFromZero);
    }

    private static string KeyAt(SortEntry entry, int index) => index < entry.Keys.Length ? entry.Keys[index] : "";

    private static int CompareDirected(int ascending, bool descending) => descending ? -ascending : ascending;

    private static int CompareText(string a, string b, bool ignoreCase)
        => string.Compare(a, b, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static int CompareNumeric(string a, string b, bool descending)
    {
        double x = ParseNumeric(a);
        double y = ParseNumeric(b);
        // Text that is not a number goes last whichever way the rule points.
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            if (double.IsNaN(x) && double.IsNaN(y)) return 0;
            return double.IsNaN(x) ? 1 : -1;
        }
        return CompareDirected(x.CompareTo(y), descending);
    }

    private static int OrderPosition(SortRule rule, string key)
    {
        string[] order = rule.Order;
        for (int i = 0; i < order.Length; i++)
            if (CompareText(order[i], key, rule.IgnoreCase) == 0) return i;
        return order.Length;
    }
}
