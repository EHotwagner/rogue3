# M12 launch evidence — what the shipped build printed

Work item `013-m12-audio-assets`. FR-009 / AC-009. Captured 2026-08-02 on the development host
(Arch Linux, Wayland session under KWin, .NET SDK 10.0.302).

The command is the one a player uses and the one `feedback/2026-08-02-Rogue3.md` §4.4 established as
the working launch route for this repository. `./fake.sh build -t Run` is still not used: it stalls.

```
dotnet run --project src/Rogue3
```

The two captures below are committed verbatim beside this file as
`launch-output-before.txt` and `launch-output-after.txt`, so the comparison is inspectable rather
than quoted.

## Before — `main` at `059e993`, no `assets/audio/` in the tree

```
FS.GG.Audio.Host: track 'title-theme' did not resolve to an asset — AssetResolver.ResolveTrack returned None, so every play of it is silent. The host does not own the id -> file mapping (FR-005): check the resolver your product supplies (a typo'd id, or an asset that was never shipped).
Gtk-Message: 05:00:45.823: Failed to load module "colorreload-gtk-module"
Gtk-Message: 05:00:45.823: Failed to load module "window-decorations-gtk-module"
```

`title-theme` is simply the first id the product asks for; the host names each miss once, as it is
first requested, so a run that stayed on the title screen surfaces exactly one of the twenty-nine.

## After — this candidate

```
Gtk-Message: 06:02:43.998: Failed to load module "colorreload-gtk-module"
Gtk-Message: 06:02:43.998: Failed to load module "window-decorations-gtk-module"
--- captured after 25 s of live run; window still open ---
```

**No `FS.GG.Audio.Host:` line was printed at all.** That absence is the obligation: the host reports
every unresolved id exactly once on stderr, and there was nothing to report. The two `Gtk-Message`
lines are cosmetic module-load noise, documented as such in the `fs-gg-skiaviewer` skill
(`SKILL.md:406`).

**What this capture does and does not show.** It shows a 25-second live run of the shipped binary
that printed no unresolved-asset diagnostic. It does **not**, by itself, show that a window opened
or that a frame was presented — the process was still running when the capture was cut, so the
launch outcome record had not been printed. Window and frame evidence comes from the two separate
runs below. Read together they support "the shipped build runs, opens a window, presents frames, and
resolves every cue"; read alone, this capture supports only the last clause.

## What a `status=` line this host CAN produce

`--launch-evidence` runs `Viewer.runBounded` and self-closes, and it works here
(`readiness/013-m12-audio-assets/launch-mode-evidence.txt`):

```
status=ok
mode=persistent-evidence
command=--launch-evidence
self-closed-for-evidence=true
first-frame-presented=True
input-dispatch=not-required
window-opened=true
renderer-mode=OpenGL
user-close-observed=false
exit-path=true
```

So "no status line is obtainable on this host" would be false, and this file previously implied it.
What that route does **not** do is carry audio: `EvidenceCommands.fs:757-765` builds a
`ViewerRunRequest` and calls `Viewer.runBounded … (view initialModel)` over a static scene at
640×480 offscreen. There is no Elmish host, no `PlayAudio` sink and no `OpenAlBackend`, so it cannot
discharge AC-009 — but it does independently establish `window-opened=true`,
`first-frame-presented=True` and `renderer-mode=OpenGL` for this host and this candidate.

## Why the *player-launch* `status=` line specifically could not be captured

`Program.fs:174` prints `status=… window-opened=… first-frame-presented=…` only after
`ControlsElmish.runInteractiveAppWithWindowBehaviorAndAudio` **returns**, and that persistent launch
returns only when its window closes.

The graceful close is already wired. `GameShell.Quit` produces `Effect.ExitRequested`
(`GameShell.fs:176`), and `EvidenceCommands.fs:413` maps it to `ViewerEffect.CloseWindow`, which is
exactly the route `fs-gg-skiaviewer/SKILL.md:403` prescribes. **What is missing is a trigger
automation can reach:** `Quit` is dispatched only from the main menu's `"exit"` button `OnClick`
(`GameShell.fs:502`), i.e. a pointer click, and `EscapePressed` at `MainMenu` is a documented no-op
(`GameShell.fs:157`). This host cannot deliver a pointer click to the game's window:

- It is a native Wayland surface under KWin. `xdotool search --name "Generated Rogue3"` finds
  nothing while the game is running, and `xdotool search "" getwindowname %@` lists only the
  XWayland windows belonging to other applications.
- A KWin script loaded and started through `org.kde.KWin`'s `/Scripting` interface, matching first on
  caption and then on `pid`, returned success and closed nothing.
- `SIGINT` and `SIGTERM` terminate the process before the `printfn` (exit 124 and 143).
- Forcing X11 — `WAYLAND_DISPLAY` unset, `XDG_RUNTIME_DIR` pointed at a directory with no Wayland
  socket, and `Xvfb :78` with `LIBGL_ALWAYS_SOFTWARE=1` — **does** produce a real X11 window
  (`xdotool search --name "Generated Rogue3"` returns window `2097160`). But both
  `xdotool windowclose` (WM_DELETE_WINDOW) and `xdotool windowkill` end the process in a
  segmentation fault inside the software-GL teardown (exit 139), so no status line survives.

`feedback/2026-08-02-Rogue3.md` §4.4 records the same launch obtaining `window-opened=true` and
`first-frame-presented=true` with exit 0 on this host — by a human closing the window, which is the
one route automation does not have. The gap is therefore narrower than "unobtainable": it is that the
already-wired close has no non-pointer trigger. Filed as roadmap M15 with that scope.

## What is proved, and what is not

Proved:

1. The shipped build runs and prints **no** unresolved-asset diagnostic where the same build one
   commit earlier printed one (`launch-output-before.txt` vs `launch-output-after.txt`).
2. A window opens and a frame is presented on this host and this candidate —
   `window-opened=true first-frame-presented=True renderer-mode=OpenGL`, from `--launch-evidence`.
3. `readiness/013-m12-audio-assets/audio-asset-evidence.txt`, produced by the shipped binary's
   `--audio-asset-evidence` command, reports `backend-kind=DeviceBacked` — a real OpenAL device, not
   the record-only Null backend `OpenAlBackend.create` substitutes when no device is available. Any
   playback claim made against a substituted backend would be vacuous (#34), so this is reported
   rather than assumed. It also reports `resolved=29 unresolved=0` over the whole declared set, with
   the parsed channels, bit depth and sample rate of every asset.
4. `readiness/013-m12-audio-assets/audio-asset-evidence-foreign-cwd.txt` is the same command run from
   `/tmp`, an unrelated working directory, proving AC-008: the product finds its assets beside its
   own assembly, not only beside the repository tree.

Not proved, and not claimed:

- That a speaker physically moved. This host has no route to record its own audio output.
- That the device *accepted* the bytes. `--audio-asset-evidence` resolves and parses every asset
  through the shipped `Wav.tryParse`, but it plays nothing, so no buffer is uploaded. The package's
  own `AssetDiagnostics.UploadRejected` exists precisely because a device can still refuse bytes that
  parse. `backend-kind=DeviceBacked` and `resolved=29` are two independent facts from one command,
  not one fact about upload.
- That the *player-launch* outcome record says `window-opened=true`. See the section above.
