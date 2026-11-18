module TerminalDocs.HomepageStyles

open Ranvier.Css

let private rule selector properties =
    properties
    |> List.map (fun (name, value) -> Declaration.create name value)
    |> Rule.style (Selector.unsafeFromText selector)

let css =
    [
        rule ".terminal-home" [
            "max-width", "82rem"; "margin", "0 auto"; "padding", "0 clamp(1rem,4vw,4rem) 4rem" ]
        rule ".terminal-hero" [
            "display", "grid"; "grid-template-columns", "minmax(0,0.83fr) minmax(0,1.17fr)"
            "align-items", "center"; "gap", "clamp(1.5rem,4vw,4rem)"; "padding", "clamp(2.5rem,6vw,6rem) 0" ]
        rule ".hero-copy h1" [
            "font-family", "var(--sans)"; "font-size", "clamp(3.4rem,5.5vw,6.4rem)"
            "letter-spacing", "-.065em"; "line-height", ".99"; "font-weight", "680"; "margin", "0" ]
        rule ".hero-copy h1 em" [
            "display", "block"; "font-family", "Georgia,serif"; "font-weight", "500"
            "color", "var(--accent)"; "letter-spacing", "-.065em" ]
        rule ".hero-copy > p" [
            "max-width", "35ch"; "margin", "1.4rem 0 1.7rem"; "line-height", "1.55"
            "font-size", "clamp(1.05rem,1.45vw,1.25rem)"; "color", "var(--muted)" ]
        rule ".hero-actions" [ "display", "flex"; "flex-wrap", "wrap"; "align-items", "center"; "gap", "1rem 1.4rem" ]
        rule ".terminal-home .terminal-action" [
            "background", "var(--accent)"; "color", "var(--paper)"; "padding", ".78rem 1.15rem"
            "border-radius", ".35rem"; "font-weight", "650"; "text-decoration", "none" ]
        rule ".terminal-action span" [ "margin-left", ".45rem" ]
        rule ".terminal-home .terminal-link" [
            "color", "var(--ink)"; "font-weight", "600"; "text-underline-offset", ".24em" ]
        rule ".terminal-window" [
            "margin", "0"; "background", "#242424"; "color", "#e0d1ad"; "overflow", "hidden"
            "border-radius", ".65rem"; "border", "1px solid #33433d"; "box-shadow", "0 12px 28px #0d302518" ]
        rule ".window-title, .terminal-window figcaption" [
            "padding", ".65rem .95rem"; "margin", "0"; "font-family", "var(--mono)"
            "font-size", ".75rem"; "color", "#d6ded2"; "background", "#24332e" ]
        rule ".window-title" [ "display", "flex"; "justify-content", "space-between"; "gap", "1rem" ]
        rule ".window-title span:last-child" [ "color", "#acc3b5" ]
        rule ".showcase-fallback" [ "width", "100%"; "height", "auto"; "display", "block" ]
        rule "#showcase-live:has(.native-screen) + .showcase-fallback" [ "display", "none" ]
        rule ".terminal-window figcaption a" [ "color", "#c7e2da" ]
        rule "#showcase-live" [ "container-type", "inline-size"; "background", "#0c121d" ]
        rule ".native-screen" [
            "position", "relative"; "margin", "0 auto"; "width", "48ch"; "max-width", "100%"
            "font-family", "Consolas, 'DejaVu Sans Mono', monospace"; "font-size", "min(3.28cqw,1.35rem)"
            "line-height", "1"; "white-space", "pre"; "font-variant-ligatures", "none"; "font-synthesis", "none" ]
        rule ".native-row" [ "display", "grid"; "grid-template-columns", "repeat(48,minmax(0,1fr))"; "height", "1em"; "white-space", "pre" ]
        rule ".native-row span" [ "min-width", "0"; "overflow", "hidden"; "text-align", "center" ]
        rule ".native-checkbox" [
            "position", "absolute"; "height", "1em"; "font", "inherit"; "padding", "0"; "border", "0"
            "background", "transparent"; "cursor", "pointer" ]
        rule ".native-checkbox:focus-visible" [ "outline", "1px solid #65e0c8"; "outline-offset", "-1px" ]
        rule ".terminal-window figcaption" [ "border-top", "1px solid #394a41"; "line-height", "1.4" ]
        rule ".home-guides" [ "border-top", "1px solid var(--line)"; "padding-top", "1.75rem" ]
        rule ".home-guides h2" [
            "font-size", "clamp(1.65rem,3vw,2.25rem)"; "margin", "0 0 1.15rem"; "letter-spacing", "-.04em" ]
        rule ".guide-grid" [ "display", "grid"; "grid-template-columns", "repeat(4,minmax(0,1fr))"; "gap", ".85rem" ]
        rule ".guide-grid a" [
            "display", "block"; "border", "1px solid var(--line)"; "padding", "1.2rem"
            "border-radius", ".4rem"; "text-decoration", "none"; "color", "var(--ink)" ]
        rule ".guide-grid a:hover, .guide-grid a:focus-visible" [ "border-color", "var(--accent)" ]
        rule ".guide-grid h3" [
            "display", "flex"; "justify-content", "space-between"; "gap", ".4rem"; "font-size", "1.08rem"; "margin", "0 0 .6rem" ]
        rule ".guide-grid h3 span" [ "font-family", "var(--mono)"; "color", "var(--accent)" ]
        rule ".guide-grid p" [ "margin", "0"; "line-height", "1.5"; "color", "var(--muted)" ]
        rule ".preview-frame" [
            "margin", "1rem 0"; "background", "#0c121d"; "overflow", "hidden"; "border-radius", ".45rem" ]
        rule ".preview-frame img" [ "max-width", "100%"; "height", "auto"; "display", "block"; "margin", "0 auto" ]
        rule ".preview-frame figcaption" [
            "margin", "0"; "background", "#1b2a33"; "color", "#d8e4e9"
            "font-size", ".86rem"; "padding", ".65rem 1rem"; "line-height", "1.5" ]
        Rule.media "(max-width: 56rem)" [
            rule ".terminal-hero" [ "grid-template-columns", "1fr"; "gap", "2rem" ]
            rule ".hero-copy h1 em" [ "display", "inline" ]
            rule ".hero-copy > p" [ "max-width", "55ch" ]
            rule ".guide-grid" [ "grid-template-columns", "repeat(2,minmax(0,1fr))" ] ]
        Rule.media "(max-width: 32rem)" [
            rule ".guide-grid" [ "grid-template-columns", "1fr" ]
            rule ".hero-copy h1" [ "font-size", "clamp(2.8rem,13vw,4.2rem)" ] ]
    ] |> Stylesheet.render
