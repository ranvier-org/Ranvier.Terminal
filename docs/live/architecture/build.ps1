$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location $PSScriptRoot
try {
    dotnet tool restore
    npm ci --ignore-scripts --no-audit --no-fund
    dotnet tool run fickl Architecture.fsproj --outDir generated --configuration Release --noCache --noPackageCache
    node node_modules/rolldown/bin/cli.mjs generated/Architecture.js --format iife --minify --banner "/*! See architecture-licenses.txt for license notices. */" --file ../../static/architecture.js
    node node_modules/rolldown/bin/cli.mjs generated/Showcase.js --format iife --minify --banner "/*! See architecture-licenses.txt for license notices. */" --file ../../static/showcase.js
} finally {
    Pop-Location
}
