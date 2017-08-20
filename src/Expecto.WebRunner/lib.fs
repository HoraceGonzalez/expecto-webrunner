namespace Expecto.WebRunner

open System

/// Type used to implement operator overloading for the null-coalescing operator, |??
// This is a well-known fsharp hack that incorporates operator overloading and statically resolved type parameters.
type Coalesce =
    // strict version
    static member ``|??`` (l: 'a Nullable, r: 'a) : 'a = if l.HasValue then l.Value else r
    static member ``|??`` (l: 'a when 'a:null, r: 'a) = match l with | null -> r | _ -> l

    static member inline Invoke (a:'``Coalesce<'a>``) (b:'a) =
        let inline call (_ : ^M, input1 : ^I1, input2 : ^I2) =
            ((^M or ^I1) : (static member ``|??`` : _*_ -> _) input1, input2)
        call(Unchecked.defaultof<Coalesce>, a, b)

    // lazy version
    static member ``|?`` (l: 'a Nullable, r: 'a Lazy) : 'a = if l.HasValue then l.Value else r.Value
    static member ``|?`` (l: 'a when 'a:null, r: 'a Lazy) = match l with | null -> r.Value | _ -> l

    static member inline DeferredInvoke (a:'``Coalesce<'a>``) (b:'a Lazy) =
        let inline call (_ : ^M, input1 : ^I1, input2 : ^I2) =
            ((^M or ^I1) : (static member ``|?`` : _*_ -> _) input1, input2)
        call(Unchecked.defaultof<Coalesce>, a, b)

[<AutoOpen>]
module Operators =
    /// Option-coalescing operator. Works with option. Nullable, and reference types should use |??
    /// *Note: the default value is strictly evaluated. Use lazy version, <? if needed
    let inline (<??) (maybeNull: 'a option) (defaultValue: 'a) : 'a = defaultArg maybeNull defaultValue
    /// Option-coalescing operator: the lazy version. Nullable, and reference types should use |?
    /// defaultValue must be Lazy
    let inline (<?) (maybeNull: 'a option) (defaultValue: 'a Lazy) = match maybeNull with | Some v  -> v | None -> defaultValue.Value

    /// null-coalescing operator. Works with nullable, and reference types. For option types, use <??
    /// *Note: the default value is strictly evaluated. Use lazy version, <? if needed
    let inline (|??) maybeNull defaultValue = Coalesce.Invoke maybeNull defaultValue
    /// null-coalescing operator: the lazy version. Works with option, nullable, and reference types.
    /// defaultValue must be Lazy
    let inline (|?) maybeNull defaultValue = Coalesce.DeferredInvoke maybeNull defaultValue

    let inline (=>) a b = a, b :> obj

    /// Works well with records - should be used instead of checking against Unchecked.defaultof<...>. Recommendation: mark the thing as Option and don't use this!
    let inline isNull (x:^T when ^T : not struct) = obj.ReferenceEquals (x, null)

    /// sheer frustration of flip missing
    let inline (.|.) f a b = f b a

    /// Tuple 2 identity
    let inline id2 (a,b) = a,b
    let inline fst3 (a, _, _) = a
    let inline snd3 (_, b, _) = b
    let inline trd (_, _, c) = c
    let toRange minValue maxValue value =
        value
        |> min maxValue
        |> max minValue

[<AutoOpen>]
module Choice =
    let inline Success v : Choice<_,_> = Choice1Of2 v
    let inline Failure v : Choice<_,_> = Choice2Of2 v

    let (|Success|Failure|) =
        function
        | Choice1Of2 a -> Success a
        | Choice2Of2 e -> Failure e
     
    let choice = FSharpx.Choice.EitherBuilder()

[<AutoOpen>]
module Serialization = 
    open Newtonsoft.Json
    
    let settings =
        JsonSerializerSettings(ContractResolver = Serialization.CamelCasePropertyNamesContractResolver(), 
                               Formatting = Formatting.Indented)      
    let serialize o = JsonConvert.SerializeObject(value = o,settings = settings)
    let deserialize<'a> txt = JsonConvert.DeserializeObject<'a>(txt, settings = settings)
    let tryDeserialize<'a> txt =
        try JsonConvert.DeserializeObject<'a>(txt, settings = settings) |> Success
        with | exn -> Failure exn

module File =
    open System
    open System.IO

    type private State = 
        {   LastFileWriteTime: DateTime
            Updated: DateTime }

    let watch changesOnly filePath onChanged =
        let getLastWrite() = File.GetLastWriteTime filePath
        let state = ref { LastFileWriteTime = DateTime.Now; Updated = DateTime.Now }
        
        let changed (args: FileSystemEventArgs) =
            let curr = getLastWrite()
            if curr <> (!state).LastFileWriteTime && DateTime.Now - (!state).Updated > TimeSpan.FromMilliseconds 500. then
                onChanged(args)
                state := { LastFileWriteTime = curr; Updated = DateTime.Now }

        let watcher = new FileSystemWatcher(Path.GetDirectoryName filePath, Path.GetFileName filePath)
        watcher.NotifyFilter <- NotifyFilters.CreationTime ||| NotifyFilters.LastWrite ||| NotifyFilters.Size
        watcher.Changed.Add changed
        if not changesOnly then 
            watcher.Deleted.Add changed
            watcher.Renamed.Add changed
        watcher.IncludeSubdirectories <- true
        watcher.EnableRaisingEvents <- true
        watcher :> IDisposable


