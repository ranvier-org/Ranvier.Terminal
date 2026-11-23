# Documentation site

From the Ranvier.Terminal root, build the static site with the .NET SDK selected by
`global.json`:

```sh
dotnet run --project docs/docs.fsproj -c Release -- build --strict
```

For a local preview with content reload, run:

```sh
dotnet run --project docs/docs.fsproj -c Release -- serve --port 4183
```

Open [the local site](http://localhost:4183/Ranvier.Terminal/). The deployable files are
in `docs/output/`. Serve that directory at `/Ranvier.Terminal/` to preserve navigation,
canonical URLs, and asset paths.

## Editing

Guides live in `docs/content/`. Link between Markdown pages using their source filenames;
Nacara resolves them to directory routes. The shared shell uses Ranvier.View server
rendering. `Styles.fs` and `HomepageStyles.fs` use Ranvier.Css to generate its stylesheet.

The gallery images are captures of the example applications' cell framebuffers. The
architecture page loads a local browser bundle built from F# with fickl. Ranvier.D5
computes its tree layout, and Ranvier.View.Dom binds the selected rendering path to the
diagram and its explanation. Both the images and the bundle ship in `docs/static/` so a
guide edit only requires the site build.

## Regenerating the visuals

The homepage replays the native Showcase meter card. The capture tool mounts the
Showcase, advances its model clock, dispatches mouse events to its checkbox and
records cell glyphs and paint. Ranvier.View.Dom displays those cells and switches
between the captured interaction states. Reduced-motion visitors start paused.

Partas.Nacara.Plugins.AgentFriendly publishes the Markdown pages, the page index at
`llms.txt`, and the combined guide at `llms-full.txt`.

Run the following command from the Terminal root. Asset regeneration uses PowerShell,
Node.js, and npm, and restores the project-local fickl tool.

```powershell
pwsh -NoProfile -File scripts/docs.ps1 -RefreshAssets
```

The script compiles the architecture and showcase bundles against published NuGet packages,
regenerates the deterministic framebuffer captures and rebuilds the site. The diagram
project's `D5Version` and `D5Feed` MSBuild properties remain available to override `Ranvier.D5`
from a local feed during a standalone diagram rebuild.
