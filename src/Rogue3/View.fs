module Rogue3.View

open FS.GG.UI.Scene
open Rogue3.Model
// GAME family (feature 220): draw the Pong playfield as a pure Scene. REPLACE ME alongside
// Model.fs when you swap in your own game (see docs/scaffold-map.md). `Viewer.runApp` renders
// this live via `generatedHost.View`.
let view (model: Model) : SceneNode =
    // Rendering#1071: this is the SAME rogue3-owned projection the coverage gate audits. A catalog
    // handle that is absent from this route is Unobserved and cannot establish runtime evidence.
    Rogue3.GameplayVisualInventory.project model
    |> List.map (fun projected -> projected.Scene)
    |> Group

