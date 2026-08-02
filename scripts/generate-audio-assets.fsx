// M12 (013-m12-audio-assets, DEC-003): write every declared audio cue asset to assets/audio/.
//
//     dotnet fsi scripts/generate-audio-assets.fsx
//
// This script is deliberately trivial: it contains no cue id and no synthesis, only a loop over
// `Rogue3.AudioCueIds` calling `Rogue3.AudioSynthesis.render`. Both of those are COMPILED PRODUCT
// SOURCE, loaded here rather than duplicated, so the script cannot drift from what the product
// requests or from what `M12AudioAssetTests` re-derives and compares byte for byte.
//
// Nothing is downloaded. There is no network dependency to add and no third-party licence to
// launder into the repository: every byte written here is computed from arithmetic in
// `src/Rogue3/AudioSynthesis.fs`, which a reviewer can read.
//
// Re-running it is safe and idempotent — the synthesis is deterministic, so an unchanged
// declaration and an unchanged synthesis produce byte-identical files.

#load "../src/Rogue3/AudioCueIds.fs"
#load "../src/Rogue3/AudioSynthesis.fs"

open System.IO
open Rogue3

let repositoryRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let outputDirectory = Path.Combine(repositoryRoot, "assets", "audio")

Directory.CreateDirectory outputDirectory |> ignore

let written =
    AudioCueIds.all
    |> List.map (fun id ->
        let bytes = AudioSynthesis.render id
        let path = Path.Combine(outputDirectory, AudioSynthesis.fileName id)
        File.WriteAllBytes(path, bytes)
        id, bytes.Length)

// Anything already in the directory that the declaration does not name is reported rather than
// deleted: a stray file is a human decision, and silently removing files under `assets/` is not this
// script's job. It cannot be ignored either — `M12AudioAssetTests`'s "assets/audio holds exactly the
// declared set and nothing else" scenario reds on an undeclared file, so a cue renamed without
// deleting its old asset is a red suite rather than an orphan that ships in every build output.
let declared = AudioCueIds.all |> List.map AudioSynthesis.fileName |> Set.ofList
let stray =
    Directory.GetFiles(outputDirectory, "*.wav")
    |> Array.map Path.GetFileName
    |> Array.filter (fun name -> not (declared.Contains name))

for id, size in written do
    printfn "wrote %-24s %7d bytes" (AudioSynthesis.fileName id) size

printfn ""
printfn "sounds=%d tracks=%d total=%d files, %d bytes into %s"
    AudioCueIds.sounds.Length
    AudioCueIds.tracks.Length
    written.Length
    (written |> List.sumBy snd)
    outputDirectory

if stray.Length > 0 then
    printfn "WARNING: %d undeclared .wav file(s) present and left alone: %s" stray.Length (String.concat ", " stray)
