# TAB

Ramjet's tab list plugin: header and footer, per-viewer tab names, sorting, and nametag prefixes and suffixes, with placeholders and per
group, per user and per world settings. Clean-room: written from its own design (`src/DESIGN.md`) and public documentation only; no code from
other tab plugins.

- `src/`: the plugin, a Ramjet source plugin. Deploy it as `plugins/tab/` (the server compiles it; `src/tab.csproj` only exists for an out-of-server
  compile check). Design notes: `src/DESIGN.md`.
- `tests/Tab.Tests/`: console tests of the settings, the layer resolver, the sorter and the placeholder token reader. No game API is needed.

Build and test:

```bash
RAMJET_HOME=/path/to/ramjet/build/bin dotnet build src/tab.csproj
dotnet run --project tests/Tab.Tests
```

`RAMJET_HOME` is the engine's build output (the folder with `dotnet/Mc.Scripting.Api.dll`). The test project needs no engine build.

Settings: `data/config.json` (written with defaults on first run). Commands: `/tab reload`, `/tab dump <player>` (permission `tab.admin`).

## License

MIT, see `LICENSE`.
