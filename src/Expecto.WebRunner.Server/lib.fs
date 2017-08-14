namespace Expecto.WebRunner

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


