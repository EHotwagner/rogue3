/// M12 (013-m12-audio-assets, DEC-003): deterministic, dependency-free synthesis of the product's
/// audio assets.
///
/// WHY THIS IS PRODUCT SOURCE AND NOT A SCRIPT. The twenty-nine WAV files under `assets/audio/` are
/// committed binaries, and a committed binary a reviewer cannot check is opaque. Putting the
/// synthesis here makes it reviewable twice over: a human reads the oscillator and envelope
/// definitions below, and `M12AudioAssetTests` regenerates every asset in memory and asserts the
/// committed bytes are *exactly* what this code produces. `scripts/generate-audio-assets.fsx` is a
/// thin driver over this module, so the script cannot drift from what the guard checks.
///
/// WHY NO `sin`, `exp`, `log`, `pow` OR `tanh` APPEAR BELOW. Those are libm calls, and libm is not
/// bit-identical across platforms and runtime versions. The reproducibility assertion compares bytes
/// produced on a developer machine with bytes produced on a CI runner, so a one-ulp difference in
/// `sin` would red the suite for no product reason. Every sample is therefore computed from `+`,
/// `-`, `*`, `/`, `abs` and `floor` alone — all IEEE-exact — with a polynomial sine and polynomial
/// envelopes standing in for the transcendental forms.
///
/// The result is placeholder fidelity and real function: oscillators, envelopes and seeded noise are
/// entirely adequate for a roguelike's shot/hit/death/door/bomb cues and its short looping themes.
/// Replacing these with authored audio changes no id, no declaration and no guard (DEC-011).
module Rogue3.AudioSynthesis

open System

/// 22050 Hz mono 16-bit PCM. Mono is required rather than incidental: `OpenAlBackend` spatializes
/// per source, and OpenAL plays a stereo buffer centred whatever position it is given.
[<Literal>]
let sampleRate = 22050

[<Literal>]
let channels = 1

[<Literal>]
let bitsPerSample = 16

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Arithmetic-only primitives
// ─────────────────────────────────────────────────────────────────────────────────────────────

let private frac (x: float) = x - floor x

/// `sin(2 * pi * turns)` to within about 1e-3 absolute, using only multiplication and `abs`.
/// The quadratic term is the standard parabolic sine; the second line is the usual error-feedback
/// refinement that pulls peak error from ~5.6% down to ~0.2%.
let private sinTurns (turns: float) =
    let x = frac turns * 2.0 - 1.0
    let parabola = 4.0 * x * (1.0 - abs x)
    -(0.225 * (parabola * abs parabola - parabola) + parabola)

type private Wave =
    | Sine
    | Square
    | Saw
    | Triangle
    | Noise

let private waveSample wave (phase: float) (noiseValue: float) =
    match wave with
    | Sine -> sinTurns phase
    | Square -> if frac phase < 0.5 then 1.0 else -1.0
    | Saw -> frac phase * 2.0 - 1.0
    | Triangle -> 1.0 - 4.0 * abs (frac (phase + 0.25) - 0.5)
    | Noise -> noiseValue

/// A seeded 32-bit LCG in `[-1, 1)`. Integer arithmetic only, so it is identical everywhere and
/// identical between runs — noise here is texture, never entropy.
let private noiseSequence (seed: int) =
    let mutable state = uint32 seed * 2654435761u ||| 1u
    fun () ->
        state <- state * 1664525u + 1013904223u
        float (int (state >>> 9) % 2097152) / 1048576.0 - 1.0

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Layers
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// One voice in a cue: a waveform swept linearly from `Freq0` to `Freq1` over `Duration` seconds,
/// starting at `Start`, shaped by a linear attack, a flat hold, and a polynomial decay of degree
/// `Curve` (1 linear, 2 percussive, 3 sharply percussive).
type private Layer =
    { Wave: Wave
      Start: float
      Duration: float
      Freq0: float
      Freq1: float
      Gain: float
      Attack: float
      Hold: float
      Curve: int
      Seed: int }

let private voice wave start duration freq0 freq1 gain =
    { Wave = wave
      Start = start
      Duration = duration
      Freq0 = freq0
      Freq1 = freq1
      Gain = gain
      Attack = 0.004
      Hold = 0.0
      Curve = 2
      Seed = 1 }

let private tone wave start duration freq gain = voice wave start duration freq freq gain

let private withEnvelope attack hold curve layer =
    { layer with Attack = attack; Hold = hold; Curve = curve }

let private withSeed seed layer = { layer with Seed = seed }

/// Linear attack, flat hold, polynomial release. `t` is seconds since the layer started.
let private envelope (layer: Layer) (t: float) =
    if t < 0.0 || t > layer.Duration then 0.0
    elif t < layer.Attack then (if layer.Attack <= 0.0 then 1.0 else t / layer.Attack)
    else
        let releaseStart = layer.Attack + layer.Hold
        if t <= releaseStart then 1.0
        else
            let span = layer.Duration - releaseStart
            if span <= 0.0 then 0.0
            else
                let remaining = 1.0 - (t - releaseStart) / span
                let clamped = if remaining < 0.0 then 0.0 elif remaining > 1.0 then 1.0 else remaining
                let mutable value = 1.0
                for _ in 1 .. layer.Curve do
                    value <- value * clamped
                value

/// Cubic soft clip: unity gain and unity slope through zero, exactly `+/-1` at `+/-1.5`, and flat
/// beyond. Arithmetic only, and gentler than a hard clamp on the layered percussive cues.
let private softClip (x: float) =
    if x >= 1.5 then 1.0
    elif x <= -1.5 then -1.0
    else x - x * x * x / 6.75

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Pitch
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// Equal-temperament ratios for one octave, written out so no `pow` is needed. Octaves are exact
/// doublings and halvings.
let private semitoneRatios =
    [| 1.0
       1.0594630943592953
       1.122462048309373
       1.189207115002721
       1.2599210498948732
       1.3348398541700344
       1.4142135623730951
       1.4983070768766815
       1.5874010519681994
       1.681792830507429
       1.7817974362806785
       1.8877486253633868 |]

let private noteHz (rootHz: float) (semitones: int) =
    let octave = if semitones >= 0 then semitones / 12 else (semitones - 11) / 12
    let step = semitones - octave * 12
    let mutable value = rootHz * semitoneRatios.[step]
    for _ in 1 .. abs octave do
        value <- if octave > 0 then value * 2.0 else value / 2.0
    value

/// The rest marker in a pattern. Any value this low is silence rather than a very deep note.
[<Literal>]
let private Rest = -999

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Music
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// A four-second loop at 120 BPM: sixteen eighth-note lead steps over four one-beat bass notes.
/// Four seconds is an exact multiple of both grids, so the loop joins itself without a seam and
/// needs no fade — a fade is what would make a looping track audibly pulse.
type private TrackSpec =
    { RootHz: float
      Lead: Wave
      LeadPattern: int[]
      LeadGain: float
      Bass: Wave
      BassPattern: int[]
      BassGain: float
      Hat: bool }

[<Literal>]
let private TrackSeconds = 4.0

[<Literal>]
let private LeadStep = 0.25

[<Literal>]
let private BassStep = 1.0

let private trackLayers (spec: TrackSpec) =
    [ for index in 0 .. spec.LeadPattern.Length - 1 do
        let semitone = spec.LeadPattern.[index]
        if semitone > Rest then
            yield
                tone spec.Lead (float index * LeadStep) (LeadStep - 0.01) (noteHz spec.RootHz semitone) spec.LeadGain
                |> withEnvelope 0.008 0.06 2
      for index in 0 .. spec.BassPattern.Length - 1 do
        let semitone = spec.BassPattern.[index]
        if semitone > Rest then
            yield
                tone spec.Bass (float index * BassStep) (BassStep - 0.02) (noteHz spec.RootHz semitone) spec.BassGain
                |> withEnvelope 0.02 0.45 1
      if spec.Hat then
        for index in 0 .. 15 do
            yield
                tone Noise (float index * LeadStep) 0.05 0.0 0.10
                |> withEnvelope 0.002 0.0 3
                |> withSeed (index * 7 + 13) ]

/// Minor-key arpeggio figures, one per floor, darkening as the run descends. The root falls a tone
/// or so per floor and the sixth adds a driving hat, so a player can hear how deep they are.
let private floorSpecs =
    [| { RootHz = 220.0
         Lead = Square
         LeadPattern = [| 0; 7; 12; 7; 3; 7; 10; 7; 0; 7; 12; 15; 12; 10; 7; 3 |]
         LeadGain = 0.26
         Bass = Sine
         BassPattern = [| -12; -12; -5; -7 |]
         BassGain = 0.38
         Hat = false }
       { RootHz = 196.0
         Lead = Square
         LeadPattern = [| 0; 3; 7; 10; 7; 3; 0; 3; 5; 8; 12; 8; 5; 3; 0; Rest |]
         LeadGain = 0.26
         Bass = Sine
         BassPattern = [| -12; -9; -12; -7 |]
         BassGain = 0.38
         Hat = false }
       { RootHz = 174.61
         Lead = Saw
         LeadPattern = [| 0; 12; 10; 7; 3; 7; 10; 12; 0; 12; 10; 7; 5; 3; 2; 0 |]
         LeadGain = 0.22
         Bass = Sine
         BassPattern = [| -12; -12; -10; -5 |]
         BassGain = 0.40
         Hat = true }
       { RootHz = 164.81
         Lead = Saw
         LeadPattern = [| 0; 3; 6; 3; 0; 3; 6; 10; 12; 10; 6; 3; 0; Rest; 10; 6 |]
         LeadGain = 0.22
         Bass = Triangle
         BassPattern = [| -12; -6; -12; -8 |]
         BassGain = 0.40
         Hat = true }
       { RootHz = 146.83
         Lead = Saw
         LeadPattern = [| 0; 1; 3; 7; 8; 7; 3; 1; 0; 1; 3; 8; 10; 8; 3; 1 |]
         LeadGain = 0.22
         Bass = Triangle
         BassPattern = [| -12; -11; -12; -5 |]
         BassGain = 0.42
         Hat = true }
       { RootHz = 130.81
         Lead = Saw
         LeadPattern = [| 0; 6; 0; 6; 3; 6; 10; 6; 0; 6; 11; 6; 3; 6; 10; 11 |]
         LeadGain = 0.24
         Bass = Saw
         BassPattern = [| -12; -12; -6; -1 |]
         BassGain = 0.42
         Hat = true } |]

let private trackSpec (id: string) =
    if id = AudioCueIds.titleTheme then
        Some
            { RootHz = 220.0
              Lead = Triangle
              LeadPattern = [| 0; Rest; 7; Rest; 12; Rest; 10; Rest; 8; Rest; 7; Rest; 3; Rest; 5; Rest |]
              LeadGain = 0.30
              Bass = Sine
              BassPattern = [| -12; -17; -14; -17 |]
              BassGain = 0.34
              Hat = false }
    elif id = AudioCueIds.shopTheme then
        Some
            { RootHz = 261.63
              Lead = Square
              LeadPattern = [| 0; 4; 7; 4; 9; 7; 4; 0; 2; 5; 9; 5; 11; 9; 5; 2 |]
              LeadGain = 0.24
              Bass = Triangle
              BassPattern = [| -12; -8; -5; -8 |]
              BassGain = 0.34
              Hat = true }
    elif id = AudioCueIds.bossTheme then
        Some
            { RootHz = 146.83
              Lead = Saw
              LeadPattern = [| 0; 0; 6; 0; 11; 0; 6; 0; 1; 1; 7; 1; 11; 1; 7; 6 |]
              LeadGain = 0.26
              Bass = Saw
              BassPattern = [| -24; -24; -23; -18 |]
              BassGain = 0.46
              Hat = true }
    elif id = AudioCueIds.gameOverTheme then
        Some
            { RootHz = 220.0
              Lead = Triangle
              LeadPattern = [| 12; Rest; 10; Rest; 8; Rest; 7; Rest; 5; Rest; 3; Rest; 0; Rest; Rest; Rest |]
              LeadGain = 0.30
              Bass = Sine
              BassPattern = [| -12; -14; -16; -17 |]
              BassGain = 0.36
              Hat = false }
    elif id = AudioCueIds.victoryTheme then
        Some
            { RootHz = 261.63
              Lead = Square
              LeadPattern = [| 0; 4; 7; 12; Rest; 12; 16; 19; Rest; 19; 16; 12; 7; 12; 16; 12 |]
              LeadGain = 0.26
              Bass = Triangle
              BassPattern = [| -12; -12; -5; -12 |]
              BassGain = 0.38
              Hat = true }
    else
        let index = [ 1 .. AudioCueIds.floorThemeCount ] |> List.tryFindIndex (fun n -> AudioCueIds.floorTheme n = id)
        index |> Option.map (fun i -> floorSpecs.[i])

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Cue definitions
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// The layer stack for one sound effect. Each cue is a couple of swept oscillators plus, where the
/// sound is impact rather than pitch, a seeded noise burst.
let private soundLayers (id: string) : Layer list option =
    if id = AudioCueIds.shotFire then
        Some
            [ voice Square 0.0 0.12 900.0 240.0 0.42 |> withEnvelope 0.002 0.01 2
              tone Noise 0.0 0.045 0.0 0.16 |> withEnvelope 0.001 0.0 3 |> withSeed 101 ]
    elif id = AudioCueIds.shotHit then
        Some
            [ voice Square 0.0 0.09 520.0 180.0 0.30 |> withEnvelope 0.001 0.0 3
              tone Noise 0.0 0.085 0.0 0.34 |> withEnvelope 0.001 0.0 2 |> withSeed 211 ]
    elif id = AudioCueIds.enemyDeath then
        Some
            [ voice Saw 0.0 0.34 330.0 70.0 0.40 |> withEnvelope 0.004 0.02 2
              tone Noise 0.0 0.20 0.0 0.24 |> withEnvelope 0.002 0.0 2 |> withSeed 307 ]
    elif id = AudioCueIds.playerHit then
        Some
            [ voice Square 0.0 0.22 240.0 95.0 0.46 |> withEnvelope 0.002 0.03 2
              voice Triangle 0.0 0.22 480.0 190.0 0.24 |> withEnvelope 0.002 0.02 2
              tone Noise 0.0 0.07 0.0 0.20 |> withEnvelope 0.001 0.0 3 |> withSeed 401 ]
    elif id = AudioCueIds.playerDeath then
        Some
            [ voice Saw 0.0 0.90 320.0 45.0 0.46 |> withEnvelope 0.006 0.06 2
              voice Sine 0.0 0.90 160.0 30.0 0.30 |> withEnvelope 0.010 0.10 2
              tone Noise 0.0 0.45 0.0 0.18 |> withEnvelope 0.004 0.0 2 |> withSeed 503 ]
    elif id = AudioCueIds.dodgeRoll then
        Some
            [ voice Triangle 0.0 0.18 200.0 620.0 0.26 |> withEnvelope 0.010 0.0 2
              tone Noise 0.0 0.16 0.0 0.20 |> withEnvelope 0.030 0.0 2 |> withSeed 601 ]
    elif id = AudioCueIds.bombExplosion then
        Some
            [ tone Noise 0.0 0.70 0.0 0.62 |> withEnvelope 0.003 0.04 2 |> withSeed 701
              voice Sine 0.0 0.60 110.0 28.0 0.52 |> withEnvelope 0.004 0.05 2
              voice Saw 0.0 0.25 240.0 60.0 0.28 |> withEnvelope 0.002 0.0 3 ]
    elif id = AudioCueIds.pickupCoin then
        Some
            [ tone Square 0.0 0.06 987.77 0.34 |> withEnvelope 0.002 0.03 2
              tone Square 0.06 0.14 1318.51 0.34 |> withEnvelope 0.002 0.05 2 ]
    elif id = AudioCueIds.pickupKey then
        Some
            [ tone Triangle 0.0 0.08 659.26 0.34 |> withEnvelope 0.003 0.03 2
              tone Triangle 0.08 0.16 987.77 0.34 |> withEnvelope 0.003 0.06 2 ]
    elif id = AudioCueIds.pickupBomb then
        Some
            [ tone Square 0.0 0.07 392.0 0.32 |> withEnvelope 0.003 0.02 2
              tone Square 0.07 0.14 293.66 0.32 |> withEnvelope 0.003 0.05 2 ]
    elif id = AudioCueIds.pickupHeart then
        Some
            [ tone Sine 0.0 0.10 523.25 0.38 |> withEnvelope 0.006 0.04 2
              tone Sine 0.10 0.20 783.99 0.38 |> withEnvelope 0.006 0.08 2 ]
    elif id = AudioCueIds.itemPickup then
        Some
            [ tone Square 0.0 0.09 523.25 0.28 |> withEnvelope 0.003 0.03 2
              tone Square 0.09 0.09 659.26 0.28 |> withEnvelope 0.003 0.03 2
              tone Square 0.18 0.09 783.99 0.28 |> withEnvelope 0.003 0.03 2
              tone Square 0.27 0.22 1046.50 0.30 |> withEnvelope 0.003 0.08 2 ]
    elif id = AudioCueIds.doorLock then
        Some
            [ voice Square 0.0 0.26 180.0 110.0 0.34 |> withEnvelope 0.002 0.04 2
              tone Noise 0.0 0.05 0.0 0.24 |> withEnvelope 0.001 0.0 3 |> withSeed 809 ]
    elif id = AudioCueIds.doorUnlock then
        Some
            [ voice Square 0.0 0.26 200.0 340.0 0.32 |> withEnvelope 0.004 0.05 2
              tone Triangle 0.12 0.18 523.25 0.24 |> withEnvelope 0.004 0.05 2 ]
    elif id = AudioCueIds.floorDescend then
        Some
            [ voice Sine 0.0 0.70 520.0 105.0 0.42 |> withEnvelope 0.008 0.08 2
              voice Triangle 0.0 0.70 260.0 52.0 0.26 |> withEnvelope 0.008 0.08 2
              tone Noise 0.35 0.30 0.0 0.12 |> withEnvelope 0.050 0.0 2 |> withSeed 907 ]
    elif id = AudioCueIds.bossIntro then
        Some
            [ voice Saw 0.0 1.20 110.0 104.0 0.42 |> withEnvelope 0.120 0.50 1
              voice Sine 0.0 1.20 55.0 52.0 0.34 |> withEnvelope 0.150 0.50 1
              tone Square 0.55 0.60 164.81 0.24 |> withEnvelope 0.020 0.25 2
              tone Noise 0.0 0.30 0.0 0.14 |> withEnvelope 0.100 0.0 2 |> withSeed 1009 ]
    elif id = AudioCueIds.bossPhase then
        Some
            [ voice Saw 0.0 0.24 220.0 440.0 0.34 |> withEnvelope 0.004 0.04 2
              voice Saw 0.24 0.30 440.0 660.0 0.34 |> withEnvelope 0.004 0.06 2
              tone Noise 0.0 0.10 0.0 0.16 |> withEnvelope 0.002 0.0 3 |> withSeed 1103 ]
    elif id = AudioCueIds.bossDeath then
        Some
            [ voice Saw 0.0 1.40 240.0 32.0 0.48 |> withEnvelope 0.006 0.10 2
              voice Sine 0.0 1.40 120.0 24.0 0.34 |> withEnvelope 0.010 0.15 2
              tone Noise 0.0 0.85 0.0 0.30 |> withEnvelope 0.004 0.05 2 |> withSeed 1201 ]
    else
        None

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Rendering
// ─────────────────────────────────────────────────────────────────────────────────────────────

[<Literal>]
let private MasterGain = 0.88

let private renderLayers (totalSeconds: float) (layers: Layer list) =
    let sampleCount = int (totalSeconds * float sampleRate + 0.5)
    let buffer = Array.zeroCreate<float> sampleCount
    for layer in layers do
        let noise = noiseSequence layer.Seed
        let startSample = int (layer.Start * float sampleRate + 0.5)
        let layerSamples = int (layer.Duration * float sampleRate + 0.5)
        let mutable phase = 0.0
        for offset in 0 .. layerSamples - 1 do
            let index = startSample + offset
            let t = float offset / float sampleRate
            let progress = if layer.Duration <= 0.0 then 0.0 else t / layer.Duration
            let frequency = layer.Freq0 + (layer.Freq1 - layer.Freq0) * progress
            // The noise sequence is advanced every sample of the layer, whether or not the sample
            // lands inside the buffer, so a layer's texture does not depend on the buffer length.
            let noiseValue = noise ()
            if index >= 0 && index < sampleCount then
                buffer.[index] <- buffer.[index] + waveSample layer.Wave phase noiseValue * envelope layer t * layer.Gain
            phase <- phase + frequency / float sampleRate
    buffer

let private toPcm16 (buffer: float[]) =
    let bytes = Array.zeroCreate<byte> (buffer.Length * 2)
    for index in 0 .. buffer.Length - 1 do
        let scaled = softClip (buffer.[index] * MasterGain) * 32767.0
        // Arithmetic rounding rather than Math.Round: no mode to disagree about, no libm.
        let rounded = int (if scaled >= 0.0 then scaled + 0.5 else scaled - 0.5)
        let clamped = if rounded > 32767 then 32767 elif rounded < -32768 then -32768 else rounded
        let value = int16 clamped
        bytes.[index * 2] <- byte (int value &&& 0xFF)
        bytes.[index * 2 + 1] <- byte ((int value >>> 8) &&& 0xFF)
    bytes

/// A minimal canonical RIFF/WAVE container: 44-byte header, `fmt ` then `data`, little-endian.
/// Deliberately the same minimal shape `FS.GG.Audio.Host.Wav.tryParse` reads, so what is written
/// here is what the device backend uploads.
let private wavFile (pcm: byte[]) =
    use stream = new IO.MemoryStream()
    use writer = new IO.BinaryWriter(stream)
    let byteRate = sampleRate * channels * bitsPerSample / 8
    let blockAlign = channels * bitsPerSample / 8
    writer.Write(System.Text.Encoding.ASCII.GetBytes "RIFF")
    writer.Write(36 + pcm.Length)
    writer.Write(System.Text.Encoding.ASCII.GetBytes "WAVE")
    writer.Write(System.Text.Encoding.ASCII.GetBytes "fmt ")
    writer.Write 16
    writer.Write 1s // WAVE_FORMAT_PCM
    writer.Write(int16 channels)
    writer.Write sampleRate
    writer.Write byteRate
    writer.Write(int16 blockAlign)
    writer.Write(int16 bitsPerSample)
    writer.Write(System.Text.Encoding.ASCII.GetBytes "data")
    writer.Write pcm.Length
    writer.Write pcm
    writer.Flush()
    stream.ToArray()

/// The complete WAV bytes for one declared cue id.
///
/// TOTAL OVER THE DECLARATION, AND ONLY OVER IT. An id `AudioCueIds` does not export raises rather
/// than returning an empty buffer: a typo must be loud at generation time, not a silent zero-length
/// asset that satisfies a naive existence check.
let render (id: string) : byte[] =
    let layers, seconds =
        match soundLayers id with
        | Some layers ->
            let last = layers |> List.map (fun layer -> layer.Start + layer.Duration) |> List.max
            layers, last
        | None ->
            match trackSpec id with
            | Some spec -> trackLayers spec, TrackSeconds
            | None -> failwithf "AudioSynthesis.render: '%s' is not a declared cue id (see Rogue3.AudioCueIds)" id
    renderLayers seconds layers |> toPcm16 |> wavFile

/// `<id>.wav` — the file name `AudioCues.resolver` looks for under `assets/audio`.
let fileName (id: string) = id + ".wav"
