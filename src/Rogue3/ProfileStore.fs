module Rogue3.ProfileStore

open System
open System.IO
open System.Text
open System.Threading
open Rogue3.Model

type LoadResult =
    | Loaded of MetaProfile
    | Absent
    | Unreadable of string

let profileFileName = "meta-profile.json"

let platformProfilePath () =
    let root=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    Path.Combine(root,"FS-GG","HollowDepths",profileFileName)

let load path =
    try
        if not(File.Exists path) then Absent
        else
            match File.ReadAllText(path,Encoding.UTF8)|>tryDecodeMetaProfile with
            | Ok profile -> Loaded profile
            | Error reason -> Unreadable reason
    with ex -> Unreadable ex.Message

type AtomicWriteEvidence =
    { DestinationPath:string
      TempPath:string
      TempCreated:bool
      Renamed:bool
      TempExistsAfter:bool }

let writeAtomicWithEvidence (path:string) profile =
    let directory=Path.GetDirectoryName path
    if not(String.IsNullOrWhiteSpace directory) then Directory.CreateDirectory(directory)|>ignore
    let tempPath=path+".tmp"
    let bytes=Encoding.UTF8.GetBytes(encodeMetaProfile profile)
    use stream=new FileStream(tempPath,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)
    stream.Write(bytes,0,bytes.Length)
    stream.Flush(true)
    stream.Close()
    let tempCreated=File.Exists tempPath
    File.Move(tempPath,path,true)
    {DestinationPath=path;TempPath=tempPath;TempCreated=tempCreated;Renamed=File.Exists(path);TempExistsAfter=File.Exists(tempPath)}

let writeAtomic path profile = writeAtomicWithEvidence path profile |> ignore

/// Host-owned debounce coordinator. Requests replace the pending profile; the callback performs
/// one atomic write after the quiet window. Tests can call Flush to make the boundary deterministic.
type Store(path:string, debounce:TimeSpan) =
    let gate=obj()
    let mutable pending:MetaProfile option=None
    let mutable writeCount=0
    let mutable disposed=false
    let mutable lastWrite:AtomicWriteEvidence option=None
    let flush () =
        let profile=lock gate (fun()->let value=pending in pending<-None;value)
        match profile with
        | Some value ->
            let evidence=writeAtomicWithEvidence path value
            lock gate (fun()->writeCount<-writeCount+1;lastWrite<-Some evidence)
        | None -> ()
    let timer=new Timer(TimerCallback(fun _->flush()),null,Timeout.InfiniteTimeSpan,Timeout.InfiniteTimeSpan)
    member _.Path=path
    member _.TempPath=path+".tmp"
    member _.WriteCount=lock gate (fun()->writeCount)
    member _.LastWrite=lock gate (fun()->lastWrite)
    member _.Load()=load path
    member _.Request(profile:MetaProfile)=
        lock gate (fun()->
            if disposed then invalidOp "profile store is disposed"
            pending<-Some profile
            timer.Change(debounce,Timeout.InfiniteTimeSpan)|>ignore)
    member _.Flush()=timer.Change(Timeout.InfiniteTimeSpan,Timeout.InfiniteTimeSpan)|>ignore;flush()
    interface IDisposable with
        member _.Dispose()=
            lock gate (fun()->disposed<-true)
            timer.Change(Timeout.InfiniteTimeSpan,Timeout.InfiniteTimeSpan)|>ignore
            flush()
            timer.Dispose()
