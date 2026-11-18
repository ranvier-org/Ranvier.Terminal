$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path $PSScriptRoot -Parent
$feed = Join-Path $root 'artifacts/packages'
$version = (dotnet msbuild "$root/src/Ranvier.Terminal/Ranvier.Terminal.fsproj" -getProperty:Version).Trim()
foreach ($id in 'Ranvier.Terminal.Core','Ranvier.Terminal.Css','Ranvier.Terminal.View','Ranvier.Terminal','Ranvier.Terminal.Components') {
    dotnet pack "$root/src/$id/$id.fsproj" -c Release --output $feed --artifacts-path "$root/artifacts/pack-build"
    if (!(Test-Path -LiteralPath "$feed/$id.$version.nupkg")) { throw "Missing $id $version" }
}
$consumer = Join-Path ([IO.Path]::GetTempPath()) ("terminal-package-" + [Guid]::NewGuid().ToString('N'))
try {
    dotnet new console --language F# --framework net10.0 --no-restore --name Consumer -o $consumer
    $project = Join-Path $consumer 'Consumer.fsproj'
    [xml]$xml = Get-Content $project -Raw
    $group = $xml.CreateElement('ItemGroup')
    foreach ($id in 'Ranvier.Terminal','Ranvier.Terminal.Components') {
        $reference = $xml.CreateElement('PackageReference')
        $reference.SetAttribute('Include', $id)
        $reference.SetAttribute('Version', "[$version]")
        $null = $group.AppendChild($reference)
    }
    $null = $xml.Project.AppendChild($group)
    $xml.Save($project)
    Set-Content "$consumer/Program.fs" 'open Ranvier.Terminal
open Ranvier.Terminal.Components
let frame = Frame.create 8 2
if frame.Width <> 8 || (Spinner.frames Spinner.Line).Length = 0 then failwith "Package consumer failed"
if System.OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = System.Runtime.InteropServices.Architecture.X64 then
    let options = { Subprocess.command "/bin/sh" ["-c"; "printf NATIVE_PTY >/dev/tty"] with Mode = PosixPseudoTerminal(80,24) }
    let session = Subprocess.start options System.Threading.CancellationToken.None
    try
        if not (session.Exit.Wait(10000)) || session.Exit.Result <> 0 then failwith "Packaged Darwin PTY failed"
        let bytes = session.Drain 4096 |> Array.collect (fun chunk -> chunk.Bytes)
        if not ((System.Text.Encoding.UTF8.GetString bytes).Contains "NATIVE_PTY") then failwith "Missing packaged PTY output"
    finally session.Close().GetAwaiter().GetResult()'
    $escapedFeed = [System.Security.SecurityElement]::Escape($feed)
    $config = "<configuration><packageSources><clear/><add key='release' value='$escapedFeed'/><add key='nuget' value='https://api.nuget.org/v3/index.json'/></packageSources><packageSourceMapping><packageSource key='release'><package pattern='Ranvier.Terminal*'/></packageSource><packageSource key='nuget'><package pattern='*'/></packageSource></packageSourceMapping></configuration>"
    Set-Content "$consumer/NuGet.Config" $config
    dotnet restore $project --packages "$consumer/packages" --configfile "$consumer/NuGet.Config"
    dotnet run --project $project -c Release --no-restore
} finally {
    if (Test-Path -LiteralPath $consumer) {
        $resolved = (Resolve-Path -LiteralPath $consumer).Path
        if ([IO.Path]::GetDirectoryName($resolved) -eq [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) -and [IO.Path]::GetFileName($resolved) -match '^terminal-package-[a-f0-9]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
