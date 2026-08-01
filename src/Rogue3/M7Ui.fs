module Rogue3.M7Ui

open FS.GG.UI.Controls
open FS.GG.UI.Controls.Typed
open FS.GG.UI.DesignSystem
open Rogue3.Model

module Button = FS.GG.UI.Controls.Typed.Button
module Stack = FS.GG.UI.Controls.Typed.Stack
module TextBlock = FS.GG.UI.Controls.Typed.TextBlock
module BarChart = FS.GG.UI.Controls.Typed.BarChart
module LineChart = FS.GG.UI.Controls.Typed.LineChart

type Actions<'msg> =
    { NewRun: 'msg
      ContinueRun: 'msg
      DailySeed: 'msg
      OpenStats: 'msg
      AbandonRun: 'msg
      Difficulty: DifficultyMode -> 'msg
      Volume: float -> 'msg
      Muted: bool -> 'msg
      ScreenShake: bool -> 'msg
      CloseStats: 'msg
      Scope: StatScope -> 'msg }

let private button id text intent message =
    Button.view
        { Button.defaults with Id=Some id; Text=text; Intent=intent
                               Classes=[StyleClass.Variant (if intent=ButtonIntent.Danger then StyleVariant.Danger else StyleVariant.Primary)]
                               OnClick=Some message }

let private text value = TextBlock.view { TextBlock.defaults with Text=value }
let private stack children = Stack.view { Stack.defaults with Spacing=10.0; Children=children }

let private settingsRows actions model =
    let selected mode = if model.Profile.Settings.Difficulty=mode then "> " else ""
    [ text "Hollow Depths"
      text "Game settings — applied live and requested for MetaProfile persistence"
      button "difficulty-easy" (selected DifficultyMode.Easy+"Easy") ButtonIntent.Secondary (actions.Difficulty DifficultyMode.Easy)
      button "difficulty-normal" (selected DifficultyMode.Normal+"Normal") ButtonIntent.Secondary (actions.Difficulty DifficultyMode.Normal)
      button "difficulty-hard" (selected DifficultyMode.Hard+"Hard") ButtonIntent.Secondary (actions.Difficulty DifficultyMode.Hard)
      button "volume-down" "Master volume −" ButtonIntent.Secondary (actions.Volume (model.Profile.Settings.MasterVolume-0.1))
      button "volume-up" "Master volume +" ButtonIntent.Secondary (actions.Volume (model.Profile.Settings.MasterVolume+0.1))
      button "mute" (if model.Profile.Settings.Muted then "Sound: Muted" else "Sound: On") ButtonIntent.Secondary (actions.Muted (not model.Profile.Settings.Muted))
      button "screen-shake" (if model.Profile.Settings.ScreenShake then "Screen shake: On" else "Screen shake: Off") ButtonIntent.Secondary (actions.ScreenShake (not model.Profile.Settings.ScreenShake))
      button "stats-settings" "Stats & charts" ButtonIntent.Secondary actions.OpenStats ]

let shellView shellDispatch config (shell:Rogue3.GameShell.Model) (model:Model) (actions:Actions<'msg>) =
    let extras =
        match shell.Screen with
        | Rogue3.GameShell.MainMenu ->
            [ button "new-run" "New Run" ButtonIntent.Primary actions.NewRun
              if model.RunActive then button "continue" "Continue" ButtonIntent.Secondary actions.ContinueRun
              button "daily-seed" "Daily Seed" ButtonIntent.Secondary actions.DailySeed
              button "meta-progression" "Meta-progression" ButtonIntent.Secondary actions.OpenStats
              button "stats" "Stats" ButtonIntent.Secondary actions.OpenStats ]
        | Rogue3.GameShell.Paused ->
            [ button "stats-pause" "Stats & charts" ButtonIntent.Secondary actions.OpenStats
              button "abandon-run" "Abandon Run" ButtonIntent.Danger actions.AbandonRun ]
        | Rogue3.GameShell.Settings -> settingsRows actions model
        | Rogue3.GameShell.Playing -> []
    Rogue3.GameShell.viewWithRows shellDispatch config shell extras

let statsSeries model =
    let depthValues =
        match model.StatScope with
        | StatScope.ThisRun -> [model.RunStats.DepthReached]
        | StatScope.Lifetime -> model.Profile.Lifetime.DepthHistory
    let bucketCounts = depthHistogram depthValues
    let buckets = ["1-3";"4-6";"7-9";"10-12";"13+"]
    let depth =
        { Name="Run depth #2a78d6"
          Points=List.map3 (fun i label count -> { X=float i;Y=float count;Label=Some label }) [1..5] buckets bucketCounts }
    let damagePoints which =
        model.RunStats.DamageByFloor |> Map.toList |> List.map (fun (floor,(dealt,taken))->
            { X=float floor;Y=(if which then dealt else taken);Label=Some(string floor) })
    depth,
    [ { Name="Dealt #2a78d6";Points=damagePoints true }
      { Name="Taken #1baf7a";Points=damagePoints false } ]

let statsView model actions =
    let lifetime = model.Profile.Lifetime
    let depth, damage = statsSeries model
    stack
        [ text "STATS"
          text (sprintf "DEEPEST  Fl %d" (max model.RunStats.DepthReached lifetime.DeepestFloor))
          text (sprintf "RUNS     %d" lifetime.RunsPlayed)
          text (sprintf "WIN %%    %.0f %%" (winRatePct lifetime))
          text (sprintf "KILLS    %d" lifetime.TotalKills)
          button "scope-this-run" "This Run" ButtonIntent.Secondary (actions.Scope StatScope.ThisRun)
          button "scope-lifetime" "Lifetime" ButtonIntent.Secondary (actions.Scope StatScope.Lifetime)
          text "Run-depth distribution"
          BarChart.view { BarChart.defaults with Id=Some "depth-histogram";Series=[depth] }
          text "Damage per floor — Dealt #2a78d6 · Taken #1baf7a"
          LineChart.view { LineChart.defaults with Id=Some "damage-per-floor";Series=damage }
          button "stats-back" "ESC — Back" ButtonIntent.Secondary actions.CloseStats ]
