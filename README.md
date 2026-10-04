# FIREWALL

[![CI](https://github.com/Anras573/xcom-like-browser-game/actions/workflows/ci.yml/badge.svg)](https://github.com/Anras573/xcom-like-browser-game/actions/workflows/ci.yml)
[![Deploy](https://github.com/Anras573/xcom-like-browser-game/actions/workflows/deploy.yml/badge.svg)](https://github.com/Anras573/xcom-like-browser-game/actions/workflows/deploy.yml)
[![Play](https://img.shields.io/badge/play-in%20your%20browser-3fd0ff)](https://anbora.dk/xcom-like-browser-game/)

A turn-based squad tactics game with a strategy layer (XCOM: Enemy Unknown style) that runs in the browser. Lead Task Force FIREWALL against MERIDIAN, a rogue logistics AI, in the quarantined city of Port Halden. Built with C# / .NET 10 on the [Yaeger](https://github.com/Anras573/Yaeger) engine, running as Blazor WebAssembly + WebGL 2. Art is CC0 from Kenney (see [CREDITS.md](CREDITS.md)).

Design: [docs/GDD.md](docs/GDD.md). Agent/contributor guidance: [CLAUDE.md](CLAUDE.md).

**Play:** https://anbora.dk/xcom-like-browser-game/

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

## Continuous integration

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every pull request and on every push to `main`. It:

1. checks formatting (`dotnet csharpier check .`)
2. builds with warnings as errors
3. runs the tests
4. publishes the WebAssembly app and runs the GitHub Pages preparation step, so a deploy-breaking change fails in the PR rather than after merge

## Deployment (GitHub Pages)

Every push to `main` deploys the game to **https://anbora.dk/xcom-like-browser-game/** through [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml). You can also run it by hand from the Actions tab (**Deploy to GitHub Pages → Run workflow**).

The domain comes from the owner's GitHub Pages user site (`anbora.dk`), which GitHub applies to every project site under the account. This repo has no `CNAME` of its own, and `https://anras573.github.io/xcom-like-browser-game/` redirects there. The game is still served from the `/xcom-like-browser-game/` sub-path, which is why the `<base href>` rewrite below is needed.

### One-time repository setup (already done)

The repository's Pages source must be **GitHub Actions**, not a branch:

> **Settings → Pages → Build and deployment → Source: GitHub Actions**

This was set up when the deploy workflow was added. If the deploy job fails with a Pages "not found" or permissions error, check this setting first. The workflow also needs the default `github-pages` environment, which GitHub creates on the first deployment. If that environment has protection rules, `main` must be allowed to deploy to it.

### What the deploy does

1. Checks out the repo **with submodules** (the Yaeger engine lives in `external/Yaeger`).
2. Runs the tests, then `dotnet publish src/Firewall.Web -c Release -o publish`.
3. Runs [`tools/prepare_pages.sh`](tools/prepare_pages.sh) on `publish/wwwroot`. The game is served from a sub-path (`/xcom-like-browser-game/`), not the domain root, so the script:
   - rewrites `<base href="/" />` in `index.html` to `<base href="/<repo>/" />`. The engine resolves its JS module (`_content/Yaeger.Browser/yaeger-browser.js`) and all textures against this, through `YaegerBrowser.InitializeAsync(Nav.BaseUri)`. Keep `<base href="/" />` in source; only the deployed copy changes.
   - deletes the precompressed `index.html.br` and `index.html.gz`, which still contain the old base href
   - adds `.nojekyll`, so `_framework/` and `_content/` survive if the folder is ever deployed from a branch
   - copies `index.html` to `404.html`
4. Uploads `publish/wwwroot` as the Pages artifact and deploys it.

### Testing a Pages build locally

```bash
rm -rf publish site    # start clean: publish doesn't overwrite an index.html edited by an earlier run
dotnet publish src/Firewall.Web -c Release -o publish
mkdir -p site/xcom-like-browser-game && cp -r publish/wwwroot/. site/xcom-like-browser-game/
tools/prepare_pages.sh site/xcom-like-browser-game xcom-like-browser-game
cd site && python3 -m http.server 8000
# open http://localhost:8000/xcom-like-browser-game/
```

If the page works at `/` but not under the sub-path, look in the browser's network tab for requests going to the site root. Every asset URL must be relative (`assets/tile.png`, not `/assets/tile.png`).
