// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.EvidenceCommands

val mapKey:
  key: FS.GG.UI.KeyboardInput.ViewerKey -> isDown: bool -> Model.Msg option

val tick: elapsed: System.TimeSpan -> Model.Msg option

val viewerOptions: FS.GG.UI.SkiaViewer.ViewerOptions

val appCommandName:
  command: FS.GG.UI.Controls.Elmish.AdapterEffect<'a> -> string

val viewerEffectsForModel:
  model: Model.Model -> FS.GG.UI.SkiaViewer.ViewerEffect list

val interpretAtHostBoundary:
  msg: Model.Msg ->
    model: Model.Model ->
    Model.Model * FS.GG.UI.Controls.Elmish.AdapterCommand<Model.Msg> *
    FS.GG.UI.SkiaViewer.ViewerEffect list

val generatedHost: FS.GG.UI.SkiaViewer.GeneratedAppHost<Model.Model,Model.Msg>

/// The interactive host's composite: the shell router's state alongside the play model. `Init` boots
/// `MainMenu`; `Start` routes into `Playing`, where the play model advances on `Tick`.
type ShellHostModel =
    {
      Shell: GameShell.Model
      Play: Model.Model
      StatsOpen: bool

      /// Raw keys retained from native down until the matching native up. Gameplay consumes this
      /// snapshot on fixed ticks; shell chrome and rebind capture still consume the same raw seam.
      HeldKeys: Set<FS.GG.UI.KeyboardInput.KeyId>
    }
/// The interactive host's message: a shell-chrome message, a live-play message, or a forwarded raw
/// key-down. The raw key is FORWARDED (not resolved) in `MapKey` and routed in `Update`, where the
/// shell state (a capture in flight / the current screen) lives — the rebind-capture seam the shell
/// needs (a resolving `MapKey` drops the unbound key a capture waits on; fs-gg-keyboard-input).
type ShellHostMsg =
    | ShellDispatch of GameShell.Msg
    | PlayDispatch of Model.Msg
    | RawKeyChanged of key: FS.GG.UI.KeyboardInput.KeyId * isDown: bool
    | StartFreshRun of seed: uint64
    | ContinueRun
    | OpenStats
    | CloseStats
    | AbandonRun
    | ChangeDifficulty of Model.DifficultyMode
    | ChangeVolume of float
    | ChangeMuted of bool
    | ChangeScreenShake of bool
    | ChangeStatScope of Model.StatScope

/// The game's parameterization of the shell: its name (the menu title), its rebindable key->command
/// map (the play controls), and the resolutions/modes the settings screen offers.
val shellConfig: GameShell.Config

/// #63, third guard: RETIRE a restored `Borderless` rather than merely tolerating it.
///
/// `modeOfToken` still decodes `"borderless"`, so a settings file written before the withdrawal
/// restores `Mode = Borderless` — a mode this product no longer offers. Left alone that is
/// incoherent in three ways at once, none of which the other two guards reach: the settings screen
/// marks the selected mode by comparing against `Config.DisplayModes`, so NEITHER offered button
/// is marked and the player sees no selection; the marked state disagrees with the window, which
/// `windowBehavior` is really running as exclusive fullscreen; and every later `DisplayChanged`
/// re-persists the retired token, so the state is sticky until the player happens to click a mode.
///
/// Normalising at the load seam collapses all three: the model carries the mode that actually
/// serves it, the menu shows it selected, and the next persist writes the current token. This is
/// deliberately a PRODUCT-side normalisation, not a shell one — `GameShell` is game-agnostic and a
/// different game may still offer `Borderless` (it stays decodable, and the seam still guards it).
///
/// KNOWN ONE-WAY DOOR, accepted deliberately. The retirement rewrites the model, and the next
/// `DisplayChanged`/`KeymapChanged` persists it — so the player's `"borderless"` token is
/// overwritten with `"fullscreen"` and does not come back when #1196 is fixed. Retaining the DU
/// case preserves the ability to READ an old settings file, not the player's preference. The
/// alternative — carrying the original token forward so it could be restored later — means
/// persisting a preference the product cannot honour and must keep specially-casing, for a mode
/// most players never chose deliberately. Recorded because it is a real loss, not an oversight.
/// Public, like `viewerEffectsForShellEffect` and for the same reason: a generated-rogue3 test can
/// assert the restore normalisation without writing to the player's real settings path.
val retireWithdrawnDisplayMode: shell: GameShell.Model -> GameShell.Model

/// The product's settings DECODER — the shell's own total decode followed by the #63 retirement —
/// as one named seam rather than a pipe buried in a local function.
///
/// It is factored out and public because a mutation critic showed the difference matters: asserting
/// `retireWithdrawnDisplayMode` by calling it directly proves the FUNCTION is correct and proves
/// nothing about it being REACHED. Deleting the retirement from the load path left the whole suite
/// green and quietly turned the third guard into dead code. Driving this seam over real
/// `encodeSettings` bytes is what closes that.
///
/// Residual gap, stated rather than papered over: `shellSettingsPath` is a fixed per-user platform
/// path, so no test drives `loadShellSettings` itself without writing to the real profile
/// directory. A change that bypassed this function inside `loadShellSettings` would still not be
/// caught. Narrowing that further needs an injectable path, which is a wider change than #63.
val decodeShellSettings:
  bytes: byte array -> fallback: GameShell.Model -> GameShell.Model

/// Interpret one shell `Effect` at the host boundary: Exit closes the window; a display change
/// re-applies the window behaviour AND persists; a keymap change persists. Persistence is
/// best-effort (the host owns IO), so the settings screen survives a restart (the MUST persistence
/// of #991/#1001).
/// Pure shell-effect -> viewer-effect contract. Kept separate from persistence so generated-rogue3
/// tests can assert that a display selection reaches both owners without writing user preferences.
val viewerEffectsForShellEffect:
  effect: GameShell.Effect -> FS.GG.UI.SkiaViewer.ViewerEffect list

/// Translate every coordinate-bearing Controls pointer interaction the pinned shell host exposes.
/// Continuous unpressed hover moves and native gamepad polls are not members of InteractiveAppHost;
/// the product keeps those capability limits explicit instead of inventing evidence for them.
val pointerInteractionToMsg:
  interaction: FS.GG.UI.Controls.PointerInteraction -> Model.Msg option

val interactiveHost:
  FS.GG.UI.Controls.Elmish.InteractiveAppHost<ShellHostModel,ShellHostMsg>

/// Production shell boundary: boot-load the profile and realize debounced save requests in the
/// product-owned backend. The Controls pointer host has no persistence-aware launcher, so this
/// wrapper interprets Persist before the retained host returns effects to that launcher.
val createInteractiveHost:
  store: ProfileStore.Store ->
    FS.GG.UI.Controls.Elmish.InteractiveAppHost<ShellHostModel,ShellHostMsg>

val defaultCommand: string

val viewImageAtSize: evidencePath: string -> width: int -> height: int -> int

val windowDiagnostics: evidencePath: string -> int

val tryRunEvidenceCommand: args: string list -> int option
