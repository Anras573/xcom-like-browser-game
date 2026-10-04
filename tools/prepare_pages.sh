#!/usr/bin/env bash
# Prepares a `dotnet publish` wwwroot folder for hosting under a GitHub Pages
# project sub-path (https://<owner>.github.io/<repo>/).
#
# Usage: tools/prepare_pages.sh <publish-wwwroot-dir> <repo-name>
#
# Used by .github/workflows/deploy.yml and by the browser smoke test (#47), so
# the deployed site and the tested site are prepared identically.
set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "usage: $0 <publish-wwwroot-dir> <repo-name>" >&2
    exit 2
fi

site_dir="$1"
repo_name="$2"
index="$site_dir/index.html"

if [[ ! -f "$index" ]]; then
    echo "error: $index not found (did dotnet publish run?)" >&2
    exit 1
fi

# GitHub repo names only contain letters, digits, '-', '_' and '.'; refuse anything
# else rather than interpolating it into a sed expression.
if [[ ! "$repo_name" =~ ^[A-Za-z0-9._-]+$ ]]; then
    echo "error: unexpected repo name '$repo_name'" >&2
    exit 1
fi

# 1. Point the app at the sub-path. Yaeger resolves its JS module and textures
#    against <base href> (YaegerBrowser.InitializeAsync(Nav.BaseUri)).
#    Re-running on an already prepared folder is a no-op for this step.
target_base="<base href=\"/${repo_name}/\" />"
if grep -qF "$target_base" "$index"; then
    echo "index.html already has $target_base"
elif grep -qF '<base href="/" />' "$index"; then
    sed -i "s#<base href=\"/\" />#${target_base}#" "$index"
else
    echo 'error: index.html has neither <base href="/" /> nor the target base href' >&2
    exit 1
fi

# 2. The precompressed copies still contain the old <base href>; drop them so a
#    server can never pick a stale one.
rm -f "$index.br" "$index.gz"

# 3. Jekyll ignores paths starting with '_' (_framework/, _content/). Actions-based
#    Pages deployments don't run Jekyll, but keep the marker so a branch-based
#    deployment of this folder would still work.
touch "$site_dir/.nojekyll"

# 4. Serve the app for unknown paths too.
cp "$index" "$site_dir/404.html"

echo "Prepared $site_dir for https://<owner>.github.io/${repo_name}/"
