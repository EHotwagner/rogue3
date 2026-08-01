module Rogue3.View

open FS.GG.UI.Scene
open Rogue3.Model
// GAME family (feature 220): draw the Pong playfield as a pure Scene. REPLACE ME alongside
// Model.fs when you swap in your own game (see docs/scaffold-map.md). `Viewer.runApp` renders
// this live via `generatedHost.View`.
let view (model: Model) : SceneNode =
    Rogue3.Render.view model
