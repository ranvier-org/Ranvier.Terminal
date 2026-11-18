module TerminalDocs.Theme

open Nacara.Core
open Ranvier.View
open Ranvier.View.Dsl
open Feliz.ViewEngine

type FrontMatter = { Title: string; Description: string option }

let frontMatter : Decoder<FrontMatter> =
    Decode.object (fun get ->
        { Title = get.Required.Field "title" Decode.string
          Description = get.Optional.Field "description" Decode.string })

let private guides =
    [ "getting-started", "Getting started"
      "composing-views", "Composing views"
      "navigation", "Navigation and components"
      "subprocess-output", "Subprocess output"
      "architecture", "Rendering architecture" ]

let private render (view: TemplateView<unit>) =
    let definition = Component.template (fun _ () -> ()) view
    Server.render Server.Options.defaults definition () |> Html.rawText

let layout (context: PageContext<FrontMatter>) =
    let current = context.Page.Route
    let route slug = Route.create context.Page.Locale [slug]
    let home = context.Site.UrlOf (Route.home context.Page.Locale)
    let link slug = context.Site.UrlOf (route slug)
    let isHome = Route.isHome current
    let active slug = if current = route slug then Some "page" else None
    let isGuide = guides |> List.exists (fun (slug, _) -> current = route slug)
    let architecture = current = route "architecture"
    let css path = Html.link [prop.rel "stylesheet"; prop.href (context.Site.UrlOfAsset path)]
    let href (url: string) = a.attribute ("href", url)
    let mast =
        header (header.className "mast") {
            div (div.className "mast-inner") {
                a (a.className "brand", href home) {
                    img (img.className "prompt-mark", img.attribute ("alt", ""), img.attribute ("src", context.Site.UrlOfAsset "terminal-mark.svg"))
                    "Ranvier.Terminal"
                }
                nav (nav.aria ("label", "Primary")) {
                    a (href (link "getting-started"), a.aria ("current", if isGuide then Some "location" else None)) { "Guides" }
                    a (href (link "gallery"), a.aria ("current", active "gallery")) { "Gallery" }
                }
            }
        }
    let side =
        nav (nav.className "guide-nav", nav.aria ("label", "Documentation")) {
            strong () { "Documentation" }
            for slug, label in guides @ ["gallery", "Component gallery"] do
                a (href (link slug), a.aria ("current", active slug)) { label }
        }
    let foot =
        footer (footer.className "site-foot") {
            div (div.className "foot-inner") {
                a (href home) { "Ranvier.Terminal" }
                a (href "https://ranvier-org.github.io/Ranvier/") { "Ranvier documentation ↗" }
            }
        }
    let title = if isHome then context.Site.Title else context.FrontMatter.Title + " · " + context.Site.Title
    Html.html [
        prop.lang "en"
        prop.children [
            Html.head [
                Html.meta [prop.charset.utf8]
                Html.meta [prop.name "viewport"; prop.content "width=device-width, initial-scale=1"]
                Html.title title
                Html.link [prop.rel "icon"; prop.href (context.Site.UrlOfAsset "terminal-mark.svg")]
                match context.FrontMatter.Description with
                | Some description -> Html.meta [prop.name "description"; prop.content description]
                | None -> ()
                match context.Site.AbsoluteUrlOf current with
                | Some canonical -> Html.link [prop.rel "canonical"; prop.href canonical]
                | None -> ()
                css "terminal-docs.css"
                if architecture then css "architecture.css"
            ]
            Html.body [
                render (a (href "#content", a.className "skip") { "Skip to content" })
                render mast
                if isHome then
                    Html.main [prop.id "content"; prop.dangerouslySetInnerHTML context.Content]
                else
                    Html.main [
                        prop.className "reader"
                        prop.children [
                            render side
                            Html.article [
                                prop.id "content"; prop.className "article"
                                prop.children [
                                    Html.h1 context.FrontMatter.Title
                                    match context.FrontMatter.Description with
                                    | Some description -> Html.p [prop.className "standfirst"; prop.text description]
                                    | None -> ()
                                    Html.div [prop.dangerouslySetInnerHTML context.Content]
                                ]
                            ]
                        ]
                    ]
                render foot
                if isHome then
                    Html.script [prop.src (context.Site.UrlOfAsset "showcase-frames.js"); prop.custom ("defer", "defer") ]
                    Html.script [prop.src (context.Site.UrlOfAsset "showcase.js"); prop.custom ("defer", "defer")]
                if architecture then
                    Html.script [prop.src (context.Site.UrlOfAsset "architecture.js"); prop.custom ("defer", "defer")]
            ]
        ]
    ]

let assets =
    { new IPlugin with
        member _.Name = "terminal-docs-theme"
        member _.Configure registry =
            registry
            |> Registry.asset (WriteText(Styles.css + HomepageStyles.css, RelativePath.create "terminal-docs.css")) }
