using Tab.Core;

namespace Tab.Tests;

/// <summary>Reading the placeholder tokens of a template, with the engine's escaping rules.</summary>
internal static class TemplateTests
{
    public static void All()
    {
        Program.Run("template: tokens in order, arguments not included", () =>
        {
            List<string> ids = PlaceholderTemplate.Ids("%tab_name% is %tab_group:short%, %tab_ping%");
            Program.Check(string.Join(",", ids) == "tab_name,tab_group,tab_ping", string.Join(",", ids));
        });

        Program.Run("template: double percent is a literal", () =>
        {
            Program.Check(PlaceholderTemplate.Ids("100%% done").Count == 0, "no tokens");
            Program.Check(string.Join(",", PlaceholderTemplate.Ids("%%%a%%")) == "a", "an escape next to a token");
            Program.Check(string.Join(",", PlaceholderTemplate.Ids("50%% off %tab_online%")) == "tab_online", "escaped percent then token");
        });

        Program.Run("template: a lone percent swallows up to the next one", () =>
        {
            // the engine reads "% off " as a token id: the admin must write %% for a literal percent sign
            List<string> ids = PlaceholderTemplate.Ids("50% off %tab_online%");
            Program.Check(ids.Count == 1 && ids[0] == " off ", "the text between the two signs is one id");
        });

        Program.Run("template: unclosed percent is text", () =>
        {
            Program.Check(PlaceholderTemplate.Ids("%unclosed").Count == 0, "no token");
            Program.Check(PlaceholderTemplate.Ids("tail %").Count == 0, "trailing percent");
        });

        Program.Run("template: empty token is not an id", () =>
        {
            Program.Check(PlaceholderTemplate.Ids("%:arg%").Count == 0, "no id before the colon");
            Program.Check(PlaceholderTemplate.Ids("%%").Count == 0, "escape only");
        });

        Program.Run("template: arguments split on colons", () =>
        {
            Program.Check(string.Join("|", PlaceholderTemplate.Arguments("id:a:b")) == "a|b", "two arguments");
            Program.Check(PlaceholderTemplate.Arguments("id").Length == 0, "no arguments");
            Program.Check(PlaceholderTemplate.Arguments("id:").Length == 1 && PlaceholderTemplate.Arguments("id:")[0] == "", "one empty argument");
        });

        Program.Run("template: text without percent signs has no tokens", () =>
        {
            Program.Check(PlaceholderTemplate.Ids("plain text").Count == 0, "plain text");
            Program.Check(PlaceholderTemplate.Ids("").Count == 0, "empty text");
        });
    }
}
