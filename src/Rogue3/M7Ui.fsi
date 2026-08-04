// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.M7Ui

type Actions<'msg> =
    {
      NewRun: 'msg
      RetryRun: 'msg
      ReturnTitle: 'msg
      ContinueRun: 'msg
      DailySeed: 'msg
      OpenStats: 'msg
      AbandonRun: 'msg
      Difficulty: (Model.DifficultyMode -> 'msg)
      Volume: (float -> 'msg)
      Muted: (bool -> 'msg)
      ScreenShake: (bool -> 'msg)
      CloseStats: 'msg
      Scope: (Model.StatScope -> 'msg)
    }

val shellView:
  shellDispatch: (GameShell.Msg -> 'msg) ->
    config: GameShell.Config ->
    shell: GameShell.Model ->
    model: Model.Model ->
    actions: Actions<'msg> -> FS.GG.UI.Controls.Widget<'msg> option

val statsSeries:
  model: Model.Model ->
    FS.GG.UI.Controls.ChartSeries * FS.GG.UI.Controls.ChartSeries list

val statsKpis: model: Model.Model -> (string * string) list

val statsView:
  model: Model.Model -> actions: Actions<'a> -> FS.GG.UI.Controls.Widget<'a>
