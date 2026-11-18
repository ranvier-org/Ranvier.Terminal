namespace Ranvier.Terminal.View

open Ranvier.Terminal.Css

module Popup =
    /// Mount the page before its floating surfaces, in back-to-front paint order.
    let host content (popups: Node array) =
        let root = Node.create [ Direction Overlay ] (Array.append [| content |] popups)
        root.PopupHost <- true
        root

    let private attach anchor popup =
        popup.PopupAnchor <- Some anchor
        Node.invalidate popup
        popup

    let atNode anchor popup = attach (NodeAnchor anchor) popup
    let atCaret anchor popup = attach (CaretAnchor anchor) popup

    /// Let pointer events pass through the entire decorative surface.
    let passThrough popup =
        popup.PointerTransparent <- true
        popup

    let onOutsidePress dismiss popup =
        popup.OnOutsidePress <- Some dismiss
        popup
