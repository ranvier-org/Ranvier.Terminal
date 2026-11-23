# Publishing Ranvier.Terminal

Set the shared version in `Directory.Build.props`. The release includes the host,
Core, Css, View and Components packages alongside their `.Traced` twins. For a local rehearsal, use macOS with Xcode command line tools and run
`pwsh -File scripts/pack.ps1`. It packs the libraries and runs a standalone F#
consumer against the local feed with a fresh package cache.

Push the reviewed release commit to `main` and wait for **Verify** to finish.
Its Intel macOS job uploads the tested packages as `nuget-packages`. The matrix checks
the terminal host and supported subprocess transports. The documentation deploys
to GitHub Pages after its own strict link and site build passes.

Run **Publish NuGet** on `main` and supply the successful Verify run ID. The
workflow checks the source commit, branch, workflow and outcome before using the
artifact. It authenticates to NuGet through the trusted publisher policy for
`ranvier-org/Ranvier.Terminal`, workflow `publish.yml`, without an environment.
Set the repository variable `NUGET_USER` if the policy owner's NuGet username
differs from the workflow default.

After uploading the packages, the workflow creates a GitHub release and attaches
the same package archives. Check every package version on nuget.org after
indexing. A retry for the same commit skips packages that have already arrived.
