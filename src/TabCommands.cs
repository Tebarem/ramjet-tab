namespace Tab;

/// <summary>The <c>/tab</c> command: <c>/tab reload</c> installs the settings file again and reports its problems; <c>/tab dump NAME</c> shows one
/// player's group, texts, list position and placeholder values. Both need the <c>tab.admin</c> permission.</summary>
internal static class TabCommands
{
    public static void Register(TabPlugin plugin)
    {
        plugin.Commands.Register(
            Command.Literal("tab").Permission("tab.admin")
                .Then(Command.Literal("reload").Executes(ctx => Reload(plugin, ctx)))
                .Then(Command.Literal("dump").Then(Command.Argument("player", Arg.Player()).Executes(ctx => Dump(plugin, ctx)))),
            description: "Reload the tab list settings, or dump one player's tab state");
    }

    private static int Reload(TabPlugin plugin, CommandContext ctx)
    {
        if (!plugin.ReloadNow())
        {
            ctx.Reply("the settings file could not be read; the settings in force stay");
            return 0;
        }
        ctx.Reply("tab settings reloaded");
        foreach (string problem in plugin.Settings.Problems) ctx.Reply("  " + problem);
        return 1;
    }

    private static int Dump(TabPlugin plugin, CommandContext ctx)
    {
        Player target = ctx.GetPlayer("player");
        foreach (string line in plugin.Describe(target)) ctx.Reply(line);
        return 1;
    }
}
