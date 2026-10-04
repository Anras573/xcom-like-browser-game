# FIREWALL

A turn-based squad tactics game with a strategy layer (XCOM: Enemy Unknown style) that runs in the browser. Lead Task Force FIREWALL against MERIDIAN, a rogue logistics AI, in the quarantined city of Port Halden. Built with C# / .NET 10 on the [Yaeger](https://github.com/Anras573/Yaeger) engine, running as Blazor WebAssembly + WebGL 2. Art is CC0 from Kenney (see [CREDITS.md](CREDITS.md)).

Design: [docs/GDD.md](docs/GDD.md). Agent/contributor guidance: [CLAUDE.md](CLAUDE.md).

**Play:** https://anras573.github.io/xcom-like-browser-game/ (once the Pages deployment is set up)

## Build and run

Requires the .NET 10 SDK.

```bash
git clone --recurse-submodules https://github.com/Anras573/xcom-like-browser-game
cd xcom-like-browser-game
dotnet build
dotnet test
dotnet run --project src/Firewall.Web
```

Format with `dotnet tool restore && dotnet csharpier format .`.
