module TerminalDocs.Site

open System.IO
open System.Text.RegularExpressions
open Nacara.Core
open Nacara.Plugins

let private content =
    Collection.create "content" Theme.frontMatter
    |> Collection.sourceAll "content"
    |> Collection.title _.Title
    |> Collection.layout Theme.layout

let private site =
    let origin = "https://ranvier-org.github.io"
    let baseUrl = "/Ranvier.Terminal/"
    Site.create "Ranvier.Terminal"
    |> Site.baseUrl baseUrl
    |> Site.origin origin
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    // The canonical tags name the future deployment. Validate site navigation against the local build.
    |> LinkValidator.registerWith (LinkValidator.ignoring ["^" + Regex.Escape(origin + baseUrl)])
    |> TextMate.register
    |> Sitemap.register
    |> AgentFriendly.registerWith (AgentFriendly.summary "Build native F# terminal applications with Ranvier.Terminal." >> AgentFriendly.details "Getting started covers the application loop. Composing views covers styles and bindings. Rendering architecture traces a frame through the retained renderer.")
    |> Site.plugin Theme.assets
    |> Site.collection content

[<EntryPoint>]
let main argv =
    Directory.SetCurrentDirectory __SOURCE_DIRECTORY__
    Nacara.run site argv
