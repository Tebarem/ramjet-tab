using Tab.Core;

namespace Tab.Tests;

/// <summary>Ordering of the tab list by the compiled rules.</summary>
internal static class SorterTests
{
    private static SortRule Rule(SortKind kind, bool? descending = null, bool ignoreCase = true, params string[] order)
        => new(kind, kind == SortKind.Group ? "" : "%x%", descending ?? (kind is SortKind.Group or SortKind.Numeric), ignoreCase, order);

    private static SortEntry Entry(string name, int weight, params string[] keys) => new(name, weight, keys);

    private static string[] Names(int[] indexes, SortEntry[] entries) => [.. indexes.Select(i => entries[i].Name)];

    public static void All()
    {
        Program.Run("sorter: group weight, highest first", () =>
        {
            SortEntry[] entries = [Entry("low", 0), Entry("top", 100), Entry("mid", 50)];
            int[] order = TabSorter.Order([Rule(SortKind.Group)], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "top,mid,low", "highest weight first");
        });

        Program.Run("sorter: group rule flipped to lowest first", () =>
        {
            SortEntry[] entries = [Entry("low", 0), Entry("top", 100)];
            int[] order = TabSorter.Order([Rule(SortKind.Group, descending: false)], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "low,top", "lowest first when descending is false");
        });

        Program.Run("sorter: alpha ignores case by default", () =>
        {
            SortEntry[] entries = [Entry("zed", 0, "Zed"), Entry("bob", 0, "bob"), Entry("Alice", 0, "Alice")];
            int[] order = TabSorter.Order([Rule(SortKind.Alpha)], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "Alice,bob,zed", "case blind alphabetical");
        });

        Program.Run("sorter: alpha with case kept is ordinal", () =>
        {
            SortEntry[] entries = [Entry("alpha", 0, "alpha"), Entry("zed", 0, "Zed")];
            int[] order = TabSorter.Order([Rule(SortKind.Alpha, ignoreCase: false)], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "zed,alpha", "uppercase Z sorts before lowercase a");
        });

        Program.Run("sorter: numbers are rounded to five decimals", () =>
        {
            Program.Check(TabSorter.ParseNumeric("1.234561") == 1.23456, "round down");
            Program.Check(TabSorter.ParseNumeric("1.234569") == 1.23457, "round up");
            Program.Check(TabSorter.ParseNumeric("-0.000004") == 0.0, "tiny negatives are zero");
            Program.Check(double.IsNaN(TabSorter.ParseNumeric("ten")), "text is not a number");
        });

        Program.Run("sorter: numbers are clamped to a billion", () =>
        {
            Program.Check(TabSorter.ParseNumeric("5e9") == 1e9, "above the limit");
            Program.Check(TabSorter.ParseNumeric("-5e9") == -1e9, "below the limit");
            SortEntry[] entries = [Entry("b", 0, "5e9"), Entry("a", 0, "2e9")];
            int[] order = TabSorter.Order([Rule(SortKind.Numeric)], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "a,b", "both clamp to the same value, so the name decides");
        });

        Program.Run("sorter: numeric highest first, text last", () =>
        {
            SortEntry[] entries = [Entry("x", 0, "word"), Entry("c", 0, "3"), Entry("d", 0, "10"), Entry("e", 0, "")];
            int[] order = TabSorter.Order([Rule(SortKind.Numeric)], entries);
            string[] names = Names(order, entries);
            Program.Check(names[0] == "d" && names[1] == "c", "10 then 3");
            Program.Check(names[2] is "x" or "e", "non-numbers last");
            Program.Check(names[3] is "x" or "e" && names[2] != names[3], "both non-numbers after the numbers");
        });

        Program.Run("sorter: ordered list, unlisted last", () =>
        {
            SortEntry[] entries = [Entry("guest", 0, "guest"), Entry("adm", 0, "Admin"), Entry("own", 0, "owner")];
            int[] order = TabSorter.Order([Rule(SortKind.Ordered, ignoreCase: true, order: ["owner", "admin"])], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "own,adm,guest", "owner, admin, then the unlisted guest");
        });

        Program.Run("sorter: the chain decides in order, name breaks the rest", () =>
        {
            SortEntry[] entries =
            [
                Entry("Carl", 1, "x"),
                Entry("bea", 2, "x"),
                Entry("alex", 2, "x"),
                Entry("dan", 1, "x"),
            ];
            int[] order = TabSorter.Order([Rule(SortKind.Group), Rule(SortKind.Alpha)], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "alex,bea,Carl,dan", "weight 2 first, then names");
        });

        Program.Run("sorter: equal keys keep their input order by name", () =>
        {
            SortEntry[] entries = [Entry("same", 1, "k"), Entry("Same", 1, "k")];
            int[] order = TabSorter.Order([Rule(SortKind.Alpha)], entries);
            // the names are equal ignoring case, so the ordinal comparison decides: "Same" sorts before "same"
            Program.Check(string.Join(",", Names(order, entries)) == "Same,same", "ordinal name tie break");
        });

        Program.Run("sorter: an empty chain sorts by name only", () =>
        {
            SortEntry[] entries = [Entry("b", 9), Entry("a", 0)];
            int[] order = TabSorter.Order([], entries);
            Program.Check(string.Join(",", Names(order, entries)) == "a,b", "name order");
        });

        Program.Run("sorter: the compiler turns config rules into rules", () =>
        {
            var problems = new List<string>();
            List<SortRule> rules = TabSorter.Compile(
            [
                new SortRuleConfig { Kind = "group" },
                new SortRuleConfig { Kind = "alpha" },
                new SortRuleConfig { Kind = "numeric", Value = "%tab_ping%", Descending = false },
                new SortRuleConfig { Kind = "ordered", Value = "%tab_group%", Order = ["a"] },
            ], problems);
            Program.Check(problems.Count == 0, "no problems");
            Program.Check(rules.Count == 4, "four rules");
            Program.Check(rules[0].Descending && rules[0].Kind == SortKind.Group, "group defaults to highest first");
            Program.Check(rules[1].Value == "%tab_name%" && !rules[1].Descending, "alpha defaults to the name, ascending");
            Program.Check(!rules[2].Descending, "numeric can be set ascending");
            Program.Check(rules[3].Order.Length == 1 && rules[3].Order[0] == "a", "order list is kept");
        });
    }
}
