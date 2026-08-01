module Rogue3.Determinism

// ============================================================================================
// The product's single definition of "byte-identical" (source specification §13, §14.1).
//
// WHY THIS EXISTS RATHER THAN `sprintf "%A"`. `%A` is not a documented stable format and — the
// part that actually breaks determinism evidence — it TRUNCATES a collection after 100 elements
// and appends an ellipsis. Measured on this toolchain:
//
//     sprintf "%A" [1..600] = sprintf "%A" ([1..599] @ [999])   // true, both 401 chars
//
// Hollow Depths' maximum-content model carries 600 particles, 120 enemy bullets and 40 shots, so
// a `%A` golden cannot see a divergence past element 100 in ANY of them. A replay that diverged
// only in the tail would compare equal, which is precisely the defect a determinism golden is for.
//
// This encoder therefore walks the value structurally with no length limit, in an order that is
// a function of the value alone: record fields in declaration order, union case name plus fields,
// tuples positionally, and Map/Set in their own comparison order (F# enumerates both sorted).
// Numbers use invariant round-trip formatting so a host's culture cannot change a byte.
// ============================================================================================

open System
open System.Collections
open System.Collections.Concurrent
open System.Globalization
open System.Reflection
open System.Security.Cryptography
open System.Text
open Microsoft.FSharp.Reflection

let private invariant = CultureInfo.InvariantCulture

let private recordFields = ConcurrentDictionary<Type, PropertyInfo[]>()

let private keyValuePairDefinition = typedefof<Collections.Generic.KeyValuePair<_, _>>

let private appendEscaped (builder: StringBuilder) (value: string) =
    builder.Append '"' |> ignore

    for character in value do
        match character with
        | '\\' -> builder.Append "\\\\" |> ignore
        | '"' -> builder.Append "\\\"" |> ignore
        | '\n' -> builder.Append "\\n" |> ignore
        | '\r' -> builder.Append "\\r" |> ignore
        | '\t' -> builder.Append "\\t" |> ignore
        | c when Char.IsControl c -> builder.Append("\\u").Append(int c |> fun code -> code.ToString("x4", invariant)) |> ignore
        | c -> builder.Append c |> ignore

    builder.Append '"' |> ignore

/// Round-trip float text that is stable across hosts. NaN and the infinities are named rather than
/// formatted, because their culture-formatted text has varied between runtimes.
let private appendFloat (builder: StringBuilder) (value: float) =
    if Double.IsNaN value then builder.Append "nan" |> ignore
    elif Double.IsPositiveInfinity value then builder.Append "inf" |> ignore
    elif Double.IsNegativeInfinity value then builder.Append "-inf" |> ignore
    else builder.Append(value.ToString("R", invariant)) |> ignore

let private appendFloat32 (builder: StringBuilder) (value: float32) =
    if Single.IsNaN value then builder.Append "nan" |> ignore
    elif Single.IsPositiveInfinity value then builder.Append "inf" |> ignore
    elif Single.IsNegativeInfinity value then builder.Append "-inf" |> ignore
    else builder.Append(value.ToString("R", invariant)) |> ignore

let rec private write (builder: StringBuilder) (value: obj) =
    match value with
    | null -> builder.Append "~" |> ignore
    | :? bool as flag -> builder.Append(if flag then "true" else "false") |> ignore
    | :? string as text -> appendEscaped builder text
    | :? char as character -> appendEscaped builder (string character)
    | :? float as number -> appendFloat builder number
    | :? float32 as number -> appendFloat32 builder number
    | :? sbyte as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? byte as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? int16 as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? uint16 as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? int as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? uint32 as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? int64 as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? uint64 as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? decimal as number -> builder.Append(number.ToString(invariant)) |> ignore
    | :? Guid as id -> builder.Append(id.ToString("D", invariant)) |> ignore
    | :? TimeSpan as span -> builder.Append(span.ToString("c", invariant)) |> ignore
    | :? DateTimeOffset as instant -> builder.Append(instant.ToString("O", invariant)) |> ignore
    | :? DateTime as instant -> builder.Append(instant.ToString("O", invariant)) |> ignore
    | _ -> writeStructured builder value

and private writeStructured (builder: StringBuilder) (value: obj) =
    let valueType = value.GetType()

    if valueType.IsEnum then
        builder.Append(valueType.Name).Append('.').Append(Enum.GetName(valueType, value)) |> ignore
    elif valueType.IsGenericType && valueType.GetGenericTypeDefinition() = keyValuePairDefinition then
        let key = valueType.GetProperty "Key"
        let item = valueType.GetProperty "Value"
        write builder (key.GetValue value)
        builder.Append "=>" |> ignore
        write builder (item.GetValue value)
    elif FSharpType.IsRecord(valueType, true) then
        let fields = recordFields.GetOrAdd(valueType, fun t -> FSharpType.GetRecordFields(t, true))
        builder.Append(valueType.Name).Append '{' |> ignore

        fields
        |> Array.iteri (fun index field ->
            if index > 0 then builder.Append ';' |> ignore
            builder.Append(field.Name).Append '=' |> ignore
            write builder (field.GetValue value))

        builder.Append '}' |> ignore
    elif FSharpType.IsTuple valueType then
        let fields = FSharpValue.GetTupleFields value
        builder.Append '(' |> ignore

        fields
        |> Array.iteri (fun index field ->
            if index > 0 then builder.Append ',' |> ignore
            write builder field)

        builder.Append ')' |> ignore
    // Sequences are matched BEFORE unions on purpose: an F# list is a union, and walking 600 cons
    // cells recursively would both nest 600 deep and lose the flat, obviously-ordered encoding.
    // Map and Set enumerate in their own comparison order, so their encoding is value-determined.
    elif (value :? IEnumerable) then
        let items = value :?> IEnumerable
        builder.Append(valueType.Name).Append '[' |> ignore
        let mutable index = 0

        for item in items do
            if index > 0 then builder.Append ';' |> ignore
            write builder item
            index <- index + 1

        builder.Append ']' |> ignore
    elif FSharpType.IsUnion(valueType, true) then
        let case, fields = FSharpValue.GetUnionFields(value, valueType, true)
        builder.Append(valueType.Name).Append('.').Append(case.Name) |> ignore

        if fields.Length > 0 then
            builder.Append '(' |> ignore

            fields
            |> Array.iteri (fun index field ->
                if index > 0 then builder.Append ',' |> ignore
                write builder field)

            builder.Append ')' |> ignore
    elif FSharpType.IsFunction valueType then
        // A function value has no observable structure; naming its type keeps the encoding total
        // without pretending two different closures are distinguishable.
        builder.Append("fun<").Append(valueType.FullName).Append('>') |> ignore
    else
        builder.Append(valueType.FullName).Append(':') |> ignore
        appendEscaped builder (Convert.ToString(value, invariant))

/// The canonical text encoding of a value. Equal encodings mean equal values for every shape the
/// simulation uses; the encoding never truncates.
let encode (value: obj) =
    let builder = StringBuilder(1024)
    write builder value
    builder.ToString()

/// UTF-8 bytes of the canonical encoding, with no byte-order mark.
let bytes (value: obj) = UTF8Encoding(false).GetBytes(encode value)

/// Lower-case sha256 of the canonical bytes — the compact form for evidence and receipts.
let digest (value: obj) =
    bytes value |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
