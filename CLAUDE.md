# CLAUDE.md

Guidance for coding agents working on **FIREWALL**, a turn-based squad tactics browser game (Blazor WASM + the Yaeger engine).

**Design source of truth: [`docs/GDD.md`](docs/GDD.md).** Design changes go there via PR.

## Commands

```bash
git clone --recurse-submodules <repo>   # or: git submodule update --init
dotnet build
dotnet test
dotnet run --project src/Firewall.Web   # serves the app locally
dotnet tool restore && dotnet csharpier format .   # format (CI runs `dotnet csharpier check .`)
```

## Layout

- `src/Firewall.Rules`: pure C# rules library. No package or project references.
- `src/Firewall.Web`: Blazor WASM host and all presentation, built on Yaeger.
- `tests/Firewall.Rules.Tests`: xUnit tests.
- `external/Yaeger`: engine, a git submodule (excluded from csharpier via `.csharpierignore`).
- `tools/`: asset scripts (Python). Downloads go to `tools/.cache/` (git-ignored).

## Architecture rules

- `Firewall.Rules` never references Yaeger, Blazor or JS.
- Rules are deterministic given a seed: inject `IRandom`, never use `Random.Shared` or the clock.
- Presentation sends commands to the rules and plays back the resulting events.
- All Yaeger ECS components must be `struct`s.
- JSON uses System.Text.Json source generation (`JsonSerializerContext`). Reflection-based serialization breaks under Blazor trimming.

## Prefer engine features over game-side code

Yaeger is maintained by this project's owner. If a browser gap blocks you, file an issue on
[Anras573/Yaeger](https://github.com/Anras573/Yaeger/issues) instead of building a large workaround.

## Yaeger browser notes

- Don't copy engine JS into this repo. `Yaeger.Browser` ships `_content/Yaeger.Browser/yaeger-browser.js` as a static web asset and runs the `requestAnimationFrame` loop.
- Call `await YaegerBrowser.InitializeAsync(Nav.BaseUri)` (base-relative, so GitHub Pages sub-paths work), then `YaegerBrowser.StartGameLoop(Action<double> tick)`. Call `StopGameLoop()` and dispose the surface on teardown.
- `BrowserRenderSurface.ClearColor` sets the clear colour. `SetCamera` flushes queued quads automatically.
- `UnifiedRenderSystem` and `CameraFollowSystem` live in `Yaeger.Core` and take an `IViewport` (`BrowserRenderSurface` implements it).
- Text: `BrowserTextRenderSurface` (Canvas 2D glyph atlas), `LoadFontAsync`, `TextLayout`.
- Input: `BrowserInputState` with `WasKeyPressed/Released` and `WasMouseButtonPressed/Released` edges. Call `BrowserInputState.BeginFrame()` once per tick.
- Audio: `IAudioOutput` / `BrowserAudioOutput` (WebAudio; unlocks on first gesture).
- Any `IRenderSurface` wrapper (e.g. `RenderStatsSurface`) must forward `GetTextureSize`. Otherwise it inherits the default "unknown size" and the engine silently skips its half-texel UV inset that stops neighbour bleed on tiles, sprite sheets and particles.
- Textures: `PreloadAsync`, `IsReady`, `GetTextureSize`, `GetLoadError`. Preload before the first frame to avoid white placeholders. `TextureSampling` defaults to Linear + Clamp, no mipmaps.
- Particles can use atlas regions via `ParticleEmitter.UvMin/UvMax`.
- Limits: `UnifiedRenderSystem` draws whole textures, uniform-grid `SpriteSheet` frames, `Tilemap`s and `Text`, not arbitrary atlas sub-rects, so world textures are uniform 64x64 grids. `UiRenderSystem` is native-only, so UI draws game-side (hit-testing helpers in Core are fine). No gamepad in the browser.
- `index.html` keeps `<base href="/" />`; CI rewrites it for GitHub Pages.

## Bumping the Yaeger pin

```bash
cd external/Yaeger && git fetch && git checkout <commit-or-tag>
cd ../.. && dotnet build && dotnet test
git add external/Yaeger && git commit -m "Bump Yaeger to <commit>"
```

The pin must be `7ab0680` or later (browser parity, the half-texel UV inset, Yaeger #315/#317, and `IInputState.IsMouseInside`, Yaeger #319).
