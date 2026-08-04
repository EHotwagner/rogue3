// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.GameShell

/// The screens the shell routes between. `Playing` is the only screen the shell does NOT draw
/// over — the game owns it — while `MainMenu`, `Paused`, and `Settings` are shell chrome. The
/// launch screen and the Esc-pause overlay are the SAME menu surface, reached from different
/// screens (the requirement: "the same menu is the launch screen").
type Screen =
    | MainMenu
    | Playing
    | Paused
    | Settings

/// How the window presents. Maps onto SkiaViewer's `ViewerWindowStartupState` in `windowBehavior`.
///
/// `Borderless` is a RESTORABLE but no longer OFFERED mode (EHotwagner/rogue3#63) — see
/// `windowBehavior` for why, and `Config.DisplayModes` for the withdrawal.
type DisplayMode =
    | Windowed
    | Borderless
    | Fullscreen

/// The display half of settings: the fixed logical resolution the game renders in (letterboxed
/// onto whatever surface via `LogicalCanvas`) plus how the window presents.
type DisplaySettings =
    {
      Resolution: FS.GG.UI.Scene.Size
      Mode: DisplayMode
    }

/// The state a game supplies once to parameterize the shell for itself.
type Config =
    {

      /// The game's name — the title label on the main menu.
      Title: string
      /// Stable player-facing actions, including labels/order and optional default bindings.
      Actions: FS.GG.UI.Controls.KeyRebindAction list
      /// The display modes the settings screen offers, in menu order.
      DisplayModes: DisplayMode list
      /// The resolutions the settings screen offers, in menu order.
      Resolutions: FS.GG.UI.Scene.Size list
      /// The display settings the game starts in.
      InitialDisplay: DisplaySettings
    }
/// The shell's whole state. A game embeds this in its own model and threads `Msg` through
/// `update`; the game's own model carries gameplay, which the shell never touches.
type Model =
    {
      Screen: Screen
      Display: DisplaySettings
      Keymap: FS.GG.UI.KeyboardInput.Keymap

      /// Stable action metadata retained so reset does not depend on runtime lookup state.
      Actions: FS.GG.UI.Controls.KeyRebindAction list
      /// The command currently awaiting its next key press (a rebind in flight), or `None`.
      Rebinding: FS.GG.UI.KeyboardInput.CommandId option
      /// The screen `Back`/Esc returns to when leaving `Settings` (`MainMenu` or `Paused`).
      SettingsReturn: Screen
    }
/// The messages the shell reduces. A game maps these into its own `Msg` (e.g. `Shell of Msg`).
type Msg =

    /// MainMenu → Playing.
    | Start
    /// MainMenu or Paused → Settings.
    | OpenSettings
    /// Settings → wherever it was opened from.
    | LeaveSettings
    /// Ask the host to close the app.
    | Quit
    /// Playing → Paused.
    | PauseGame
    /// Paused → Playing.
    | ResumeGame
    /// Playing or Paused → MainMenu. The game decides whether this means abandon/finish;
    /// the generic shell only owns the navigation transition.
    | ReturnToMenu
    /// Choose the logical resolution.
    | SetResolution of FS.GG.UI.Scene.Size
    /// Choose how the window presents.
    | SetDisplayMode of DisplayMode
    /// Begin capturing the next key press as the new binding for a command (from the rebind UI).
    | ArmRebind of FS.GG.UI.KeyboardInput.CommandId
    /// The next raw key after `ArmRebind` — completes the capture (or cancels, if it is the menu key).
    | CaptureKey of FS.GG.UI.KeyboardInput.KeyId
    /// Abandon an in-flight capture without rebinding.
    | CancelRebind
    /// Restore every action's declared default binding, leaving default-unbound actions unbound.
    | ResetBindings
    /// The universal Esc route (pause / resume / back / cancel-capture).
    | EscapePressed
/// A shell-level intent for the host to interpret — the effects `update` emits. The host applies
/// these at its own boundary (there is no shell runner; the game routes them, like any Elmish effect).
type Effect =

    /// The player chose Exit — the host should close the window.
    | ExitRequested
    /// The display settings changed — the host should re-apply the window behaviour + logical size
    /// (`windowBehavior` / `logicalSize`).
    | DisplayChanged of DisplaySettings
    /// The keymap changed (a rebind fired) — the host should persist it (`encodeKeymap`).
    | KeymapChanged of FS.GG.UI.KeyboardInput.Keymap
/// The `KeyId` the shell treats as the universal menu / back / pause / cancel key (Esc).
val menuKey: FS.GG.UI.KeyboardInput.KeyId

/// The initial shell state for a game: the main menu, the game's default keymap and display.
val init: config: Config -> Model

/// The shell reducer. Pure and total — every transition is deterministic and host-free, so a
/// scripted `Msg` sequence replays identically.
val update: msg: Msg -> model: Model -> Model * Effect list

/// The `ViewerWindowBehaviorRequest` for a display setting: the window's presentation mode maps
/// onto a `ViewerWindowStartupState`; the rest of the request keeps the framework defaults.
///
/// MITIGATION (EHotwagner/rogue3#63; root cause filed as FS-GG/FS.GG.Rendering#1196).
/// `Borderless` maps onto EXCLUSIVE `Fullscreen`, not onto
/// `ViewerWindowStartupState.WindowedFullscreen`.
///
/// OBSERVED, from real play on a multi-output Wayland host: selecting Borderless moved the window
/// half off screen and left every button dead while `Esc` kept working. That asymmetry is the
/// diagnosis — `MapKey` is coordinate-free, whereas a pointer sample is inverted through the
/// logical-canvas fit and hit-tested against the render tree, so a fit taken against the wrong
/// surface misses every control's bounds and no authored `OnClick` fires. Exclusive `Fullscreen`
/// on the IDENTICAL product path works, and `windowBehavior` diverged in exactly one match arm, so
/// the fault is the enum value rather than anything in this repository.
///
/// INFERRED mechanism, not contract: `SkiaViewer.fsi` describes `WindowedFullscreen` as "borderless
/// coverage of the monitor work area", so it must DERIVE a rectangle, and on two stacked outputs
/// that derivation is ambiguous; exclusive fullscreen plausibly avoids it by being handed a surface
/// outright. Recorded as the leading explanation — the OBSERVATION above is what this arm rests on.
///
/// WHY NOT FIX IT HERE. `InteractiveAppHost` has no surface-changed notification (its `View`
/// receives the LOGICAL size; `CaptureOutputSize`/`InitialOutputSize` belong to the bounded-run
/// evidence workflow), so a product cannot observe the new surface, let alone re-fit against it.
/// `SkiaViewer.fsi` also gives the host both directions of the transform and expects products to
/// author, lay out and hit-test in the logical size only. Requesting a state that works is
/// therefore the only correct product-side move.
///
/// REVERT THIS ARM when FS-GG/FS.GG.Rendering#1196 is fixed and the pin is raised past it.
///
/// The `Borderless` CASE ITSELF IS RETAINED: `modeOfToken` still decodes the `"borderless"` token.
/// Note the path this arm actually closes — it is NOT launch. `Program.fs` builds the launch
/// request from `shellConfig.InitialDisplay`, and the host's `Init` emits only `ApplyLogicalCanvas`,
/// so a restored mode never reaches the window at boot at all (a separate defect, EHotwagner/rogue3#75). The
/// reachable brick was mid-session: `SetResolution` emits `DisplayChanged` carrying the UNCHANGED
/// mode, so a restored-Borderless player merely changing resolution shipped `WindowedFullscreen`.
/// `EvidenceCommands.retireWithdrawnDisplayMode` now also normalises the restored mode at load, so
/// this arm is the backstop for any `Borderless` that reaches the seam by another route.
val windowBehavior:
  display: DisplaySettings -> FS.GG.UI.SkiaViewer.ViewerWindowBehaviorRequest

/// The fixed logical canvas size a game renders in for a display setting. Seed
/// `ViewerOptions.LogicalSize` with the initial value and emit `ApplyLogicalCanvas` for later
/// `DisplayChanged` values. SkiaViewer owns both the presentation fit and inverse pointer mapping;
/// Controls must not apply another coordinate transform.
val logicalSize: display: DisplaySettings -> FS.GG.UI.Scene.Size

/// What a raw key-down means right now, given the shell state — the single decision the host's
/// `mapKeyRaw` seam consults. `Game` carries the value a live-play command resolves to; the shell
/// resolves the key through the (possibly rebound) keymap only while `Playing` and not capturing.
type KeyOutcome<'game> =

    /// Feed this shell `Msg` back into `update` (a capture completion, or an Esc route).
    | ShellMsg of Msg
    /// Gameplay is live and the keymap resolves the key to this game value; the game acts on it.
    | Game of 'game
    /// The key means nothing right now.
    | NoInput
/// One normalized native-key edge. `GameEdge(value, true)` begins a held gameplay control and
/// `GameEdge(value, false)` ends it; shell chrome consumes only key-down edges. Keeping both edges
/// on this seam prevents an interactive host from accidentally implementing capture with raw keys
/// while translating gameplay into one-shot nudges that can never be released.
type KeyEventOutcome<'game> =
    | ShellEdge of Msg
    | GameEdge of value: 'game * isDown: bool
    | NoKeyEvent

/// Route a raw key-DOWN. `toGame` lifts a resolved live-play `CommandId` into the game's own value
/// (return `None` to decline). This is the whole raw-key contract the shell needs from the host:
/// wire the host's `mapKeyRaw` to forward every key-down here, dispatch a `ShellMsg` through
/// `update`, and hand a `Game` value to the game. A capture in flight swallows the next key
/// (`CaptureKey`); otherwise the menu key routes (`EscapePressed`) and, only while `Playing`, an
/// unbound-for-chrome key resolves through the keymap.
val routeKeyDown:
  toGame: (FS.GG.UI.KeyboardInput.CommandId -> 'game option) ->
    key: FS.GG.UI.KeyboardInput.KeyId -> model: Model -> KeyOutcome<'game>

/// Route both edges of one normalized native key event. Key-down preserves the shell's capture and
/// Esc behavior; while playing, both down and up resolve through the same current keymap so the host
/// can retain a control until its matching release. Chrome never reacts to key-up.
val routeKeyEvent:
  toGame: (FS.GG.UI.KeyboardInput.CommandId -> 'game option) ->
    key: FS.GG.UI.KeyboardInput.KeyId ->
    isDown: bool -> model: Model -> KeyEventOutcome<'game>

/// Serialize the current bindings to the versioned, deterministic keymap JSON envelope — the
/// blob the host writes so a player's rebindings survive a restart (the MUST persistence of #991).
val encodeKeymap: model: Model -> byte array

/// Restore bindings from a blob `encodeKeymap` produced. A decode failure (a corrupt or absent
/// file) is total: the model's current keymap is kept, so a bad save degrades to the defaults
/// rather than throwing at startup.
val decodeKeymap: bytes: byte array -> model: Model -> Model

/// Serialize the current display settings to the versioned, deterministic JSON envelope — the
/// display counterpart of `encodeKeymap`, so resolution + mode survive a restart on the same seam.
val encodeDisplay: model: Model -> byte array

/// Restore display settings from a blob `encodeDisplay` produced. Like `decodeKeymap`, it is total:
/// a corrupt, absent, or newer-format file keeps the model's current display, so a bad save degrades
/// to the game's initial resolution/mode rather than throwing at startup.
val decodeDisplay: bytes: byte array -> model: Model -> Model

/// Serialize the WHOLE settings screen — display + bindings — to one deterministic envelope that
/// COMPOSES the display codec and `KeymapCodec`: a fixed-order object with a `display` member
/// (`writeDisplayObject`) and a `keymap` member (the embedded `KeymapCodec.encode` object). A host
/// that wants a single settings file writes this instead of the two blobs separately.
val encodeSettings: model: Model -> byte array

/// Restore the whole settings screen from a blob `encodeSettings` produced. Total and per-member:
/// each of `display` and `keymap` is applied through its own total decode, so a member that is
/// missing or corrupt leaves that half of the model at its current value while the other half still
/// restores — a partial save never throws and never wholesale-resets the settings.
val decodeSettings: bytes: byte array -> model: Model -> Model

/// The menu chrome for the current screen, as a typed widget tree, or `None` while `Playing` (the
/// game owns the screen then — the shell draws nothing over it). `dispatch` embeds a shell `Msg`
/// into the game's own message type. The settings screen wires the resolution + display-mode
/// choices and explicitly keyed binding rows (each row's rebind affordance dispatches
/// `ArmRebind`, which arms the `mapKeyRaw` capture).
val viewWithRows:
  dispatch: (Msg -> 'msg) ->
    config: Config ->
    model: Model ->
    extraRows: FS.GG.UI.Controls.Widget<'msg> list ->
    FS.GG.UI.Controls.Widget<'msg> option
