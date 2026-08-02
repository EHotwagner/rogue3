# M12 launch evidence — what the shipped build printed

Work item `013-m12-audio-assets`. FR-009 / AC-009. Captured 2026-08-02 on the development host
(Arch Linux, Wayland session under KWin, .NET SDK 10.0.302).

The command is the one a player uses and the one `feedback/2026-08-02-Rogue3.md` §4.4 established as
the working launch route for this repository. `./fake.sh build -t Run` is still not used: it stalls.

```
dotnet run --project src/Rogue3
```

## Before — `main` at `059e993`, no `assets/audio/` in the tree

```
FS.GG.Audio.Host: track 'title-theme' did not resolve to an asset — AssetResolver.ResolveTrack returned None, so every play of it is silent. The host does not own the id -> file mapping (FR-005): check the resolver your product supplies (a typo'd id, or an asset that was never shipped).
Gtk-Message: 05:00:45.823: Failed to load module "colorreload-gtk-module"
Gtk-Message: 05:00:45.823: Failed to load module "window-decorations-gtk-module"
```

The window opened and the title screen requested its music. `title-theme` is simply the first id the
product asks for; the remaining twenty-eight were equally unresolvable, and the host names each miss
once as it is first requested.

## After — this candidate

```
Gtk-Message: 05:32:11.945: Failed to load module "colorreload-gtk-module"
Gtk-Message: 05:32:11.945: Failed to load module "window-decorations-gtk-module"
--- captured after 25 s of live run; window still open ---
```

The window opened, the title screen requested its music, and **no `FS.GG.Audio.Host:` line was
printed at all**. That absence is the obligation: the host reports every unresolved id exactly once,
on stderr, and there was nothing to report. The two `Gtk-Message` lines are cosmetic module-load
noise, documented as such in the `fs-gg-skiaviewer` skill (`SKILL.md:406`).

## The `status=…` line could not be captured on this host, and here is exactly why

`Program.fs` prints its `status=… window-opened=… first-frame-presented=…` line only after
`ControlsElmish.runInteractiveAppWithWindowBehaviorAndAudio` **returns**, and the persistent launch
returns only when its window closes. On this host the window cannot be closed programmatically:

- It is a native Wayland surface under KWin. `xdotool search --name "Generated Rogue3"` finds
  nothing, and `xdotool search "" getwindowname %@` lists only the 18 XWayland windows belonging to
  other applications — the game is not among them.
- A KWin script loaded and started through `org.kde.KWin`'s `/Scripting` interface, matching first on
  caption and then on `pid`, returned success and did not close the window.
- `SIGINT` and `SIGTERM` terminate the process before the `printfn`, so neither yields a status line
  (`exit 124` and `exit 143` respectively).
- Forcing an X11 window — `WAYLAND_DISPLAY` unset, `XDG_RUNTIME_DIR` pointed at a directory with no
  Wayland socket, and `Xvfb :78` with `LIBGL_ALWAYS_SOFTWARE=1` — **does** produce a real X11 window
  (`xdotool search --name "Generated Rogue3"` returns window `2097160`), which is independent
  confirmation that the launch opens a window. But both `xdotool windowclose` (WM_DELETE_WINDOW) and
  `xdotool windowkill` end the process in a segmentation fault inside the software-GL teardown
  (`app-exit=139`), so no status line survives that route either.

This is a host capability limit, not a product claim being withheld, and it is the same limit
`feedback/2026-08-02-Rogue3.md` §4.4 and §12 record: "the host obtained no screenshot of a live
window", and forcing the X11 backend failed. It is filed again in this cycle's report because it now
blocks a *milestone obligation* rather than a screenshot.

## What is proved instead, and what is not

Proved:

1. The shipped build launches, opens a window, requests its title music, and prints **no**
   unresolved-asset diagnostic where the same build one commit earlier printed one.
2. `readiness/013-m12-audio-assets/audio-asset-evidence.txt`, produced by the shipped binary's
   `--audio-asset-evidence` command, reports `backend-kind=DeviceBacked` — a real OpenAL device, not
   the record-only Null backend `OpenAlBackend.create` substitutes when no device is available. Any
   playback claim made against a substituted backend would be vacuous (#34), so this is reported
   rather than assumed. It also reports `resolved=29 unresolved=0` over the whole declared set, with
   the parsed channels, bit depth and sample rate of every asset.
3. That command was deliberately run from an unrelated working directory
   (`working-directory=/tmp/.../scratchpad`, `base-directory=…/src/Rogue3/bin/Debug/net10.0/`) to
   prove AC-008: the product finds its assets beside its own assembly, not only beside the
   repository tree.

Not proved, and not claimed: that a speaker physically moved. This host has no route to record its
own audio output.
