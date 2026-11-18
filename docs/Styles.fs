module TerminalDocs.Styles

open Ranvier.Css

let private style selector declarations =
    declarations
    |> List.map (fun (name, value) -> Declaration.create name value)
    |> Rule.style (Selector.unsafeFromText selector)

let css =
    [
        style ":root" [
            "--ink", "#152b2b"; "--muted", "#536c68"; "--accent", "#006552"; "--paper", "#f7f8f2"
            "--line", "#cbd5cb"; "--mint", "#a9eebd"
            "--sans", "Inter, system-ui, 'Segoe UI', sans-serif"
            "--mono", "'IBM Plex Mono', 'Cascadia Code', Consolas, monospace"
            "font-family", "var(--sans)"; "color", "var(--ink)"; "background", "var(--paper)"
            "line-height", "1.6"; "text-size-adjust", "100%"; "scroll-padding-top", "1.5rem"
        ]
        style "*, *::before, *::after" ["box-sizing", "border-box"]
        style "body" ["margin", "0"; "min-height", "100vh"; "display", "flex"; "flex-direction", "column"]
        style "body > main" ["flex", "1"]
        style "a" ["color", "var(--accent)"; "text-underline-offset", ".2em"]
        style ":focus-visible" ["outline", "3px solid #e98c28"; "outline-offset", "4px"]
        style ".skip" ["position", "absolute"; "z-index", "4"; "top", "-6rem"; "left", "1rem"; "background", "white"; "padding", ".6rem 1rem"]
        style ".skip:focus" ["top", ".5rem"]
        style ".mast" ["background", "#102c2b"; "color", "#f0faef"; "border-bottom", "3px solid #a9eebd"]
        style ".mast-inner, .foot-inner" [
            "width", "min(100% - 2.5rem, 77rem)"; "margin-inline", "auto"; "display", "flex"
            "align-items", "center"; "justify-content", "space-between"; "gap", "1rem"
        ]
        style ".mast-inner" ["min-height", "4.25rem"; "flex-wrap", "wrap"]
        style ".brand" ["display", "inline-flex"; "align-items", "center"; "gap", ".7rem"; "font", "650 1.02rem var(--mono)"; "letter-spacing", "-.045em"; "color", "inherit"; "text-decoration", "none"]
        style ".prompt-mark" ["width", "2.1rem"; "height", "2.1rem"]
        style ".mast nav" ["display", "flex"; "align-items", "center"; "gap", "clamp(1rem, 4vw, 2.75rem)"]
        style ".mast nav a" ["color", "#dce8df"; "font-size", ".89rem"; "text-decoration", "none"; "padding-block", ".7rem"]
        style ".mast nav a:hover, .mast nav a[aria-current]" ["color", "var(--mint)"; "text-decoration", "underline"]
        style ".reader" ["display", "grid"; "grid-template-columns", "13rem minmax(0, 47rem)"; "gap", "clamp(2rem, 6vw, 6rem)"; "width", "min(100% - 2.5rem, 77rem)"; "margin", "3.5rem auto 5rem"; "align-items", "start"]
        style ".guide-nav" ["position", "sticky"; "top", "2rem"; "border-left", "1px solid var(--line)"]
        style ".guide-nav strong" ["display", "block"; "padding", "0 1rem .75rem"; "font", "600 .75rem var(--mono)"; "letter-spacing", ".08em"; "text-transform", "uppercase"; "color", "var(--muted)"]
        style ".guide-nav a" ["display", "block"; "padding", ".45rem 1rem"; "line-height", "1.4"; "font-size", ".89rem"; "color", "var(--muted)"; "text-decoration", "none"]
        style ".guide-nav a:hover, .guide-nav a[aria-current]" ["color", "var(--accent)"; "background", "#e6eee3"]
        style ".guide-nav a[aria-current]" ["box-shadow", "inset 3px 0 var(--accent)"; "font-weight", "650"]
        style ".article" ["min-width", "0"; "font-size", "1.03rem"; "overflow-wrap", "break-word"]
        style ".article h1, .article h2, .article h3" ["line-height", "1.13"; "letter-spacing", "-.035em"; "text-wrap", "balance"]
        style ".article h1" ["font-size", "clamp(2.6rem, 6vw, 4.8rem)"; "margin", "0 0 1.1rem"]
        style ".article h2" ["font-size", "clamp(1.6rem, 3.5vw, 2.25rem)"; "margin", "2.4rem 0 1rem"]
        style ".article h3" ["font-size", "1.32rem"; "margin", "1.75rem 0 .65rem"]
        style ".nacara-heading-anchor" ["margin-inline-start", ".35em"; "text-decoration", "none"; "font-size", ".72em"; "opacity", "0"]
        style ".nacara-heading-anchor::after" ["content", "'#'"]
        style ":is(h2,h3,h4):hover .nacara-heading-anchor, .nacara-heading-anchor:focus-visible" ["opacity", "1"]
        style ".article .standfirst" ["font-size", "1.22rem"; "color", "var(--muted)"; "margin", "0 0 1.8rem"; "padding-bottom", "1.5rem"; "border-bottom", "1px solid var(--line)"]
        style ".article :is(p, li) > code" ["background", "#e8ede4"; "padding", ".13em .32em"; "border-radius", "3px"]
        style "code, pre, kbd" ["font-family", "var(--mono)"]
        style ".article pre" ["background", "#102a2c"; "color", "#e0f0e6"; "border", "1px solid #274846"; "padding", "1.1rem 1.25rem"; "border-radius", "6px"; "overflow-x", "auto"; "font-size", ".88rem"; "line-height", "1.6"]
        style ".article pre .tok-keyword" ["color", "#c9aeff"]
        style ".article pre :is(.tok-string,.tok-function)" ["color", "#9fe9ba"]
        style ".article pre :is(.tok-number,.tok-constant)" ["color", "#facb80"]
        style ".article pre .tok-comment" ["color", "#adc3bb"]
        style ".article :is(img,svg)" ["max-width", "100%"]
        style ".article table" ["display", "block"; "overflow-x", "auto"; "border-collapse", "collapse"]
        style ".article :is(th,td)" ["padding", ".5rem .7rem"; "border-bottom", "1px solid var(--line)"; "text-align", "start"]
        style ".article blockquote" ["margin-inline", "0"; "border-left", "3px solid var(--accent)"; "padding-inline", "1rem"]
        style ".site-foot" ["border-top", "1px solid var(--line)"; "margin-top", "auto"; "color", "var(--muted)"]
        style ".foot-inner" ["padding-block", "1.6rem"; "font", ".82rem var(--mono)"; "flex-wrap", "wrap"]
        Rule.media "(max-width: 46rem)" [
            style ".reader" ["grid-template-columns", "minmax(0, 1fr)"; "gap", "1.75rem"; "margin-block", "2rem 3rem"]
            style ".guide-nav" ["position", "static"; "border", "0"; "display", "flex"; "gap", ".4rem"; "flex-wrap", "wrap"]
            style ".guide-nav strong" ["width", "100%"; "padding", "0"]
            style ".guide-nav a" ["padding", ".25rem .55rem"; "border", "1px solid var(--line)"; "border-radius", "3px"]
        ]
        Rule.media "(max-width: 30rem)" [
            style ".mast-inner" ["padding-block", ".65rem"; "gap", ".2rem .7rem"]
            style ".mast nav" ["gap", "1.15rem"]
        ]
    ]
    |> Stylesheet.render
