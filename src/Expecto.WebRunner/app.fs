module Expecto.WebRunner.App

open System
open System.IO
open System.Text
open System.Text.RegularExpressions

open Hopac

open Suave
open Suave.Logging
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful
open Suave.RequestErrors
open Suave.Files
    
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

[<AutoOpen>]
module private _Implementation = 

    module Path = 
        let status = "/status"
        let index = "/"

    let getConfig(scheme,port,maxContentLengthBytes) =
        let scheme = 
            match scheme with
            | s when s = "http"-> Protocol.HTTP
            | _ -> Protocol.HTTP //todo implement Protocol.HTTPS
        let port =
            match Sockets.Port.TryParse(string port) with
            | true,port -> port
            | false,_ -> 8086us
    
        let bindings = [Suave.Http.HttpBinding.create scheme Net.IPAddress.Loopback port]

        { Web.defaultConfig with 
                bindings = bindings
                maxContentLength = maxContentLengthBytes } 

let spawnServer() =
    let cancel = new Threading.CancellationTokenSource()                                                                 //TODO Test 6
        
    let suaveConfig = 
        let scheme = "http"
        let port = 8082us
        let maxContentLengthBytes = (1 <<< 20)
        { getConfig(scheme,port,maxContentLengthBytes) 
            with cancellationToken = cancel.Token
                 homeFolder = Some <| System.IO.Path.GetFullPath("assets") } 
      
    //let handleWith fn = Request.handleWith OK OK Api.Models.Conversions.Errors.ToApi.error (fun ctx -> pass() <?> (fn >> Async.map Hypermedia.Document.ofChoice) ctx)
        
    let withJsonHeader = setMimeType "application/json; charset=utf-8"
    let withTextHtmlHeader = setMimeType "text/html"
        
    let app = 
        setHeader "Cache-Control" "no-cache, no-store, must-revalidate"
        >=> setHeader "Pragma" "no-cache"
        >=> setHeader "Expires" "0"
        >=> choose [
            // static files
            withTextHtmlHeader >=> choose [
                GET >=> path Path.index >=> RouteHandlers.Html.indexPage
            ]
            // json api
            withJsonHeader >=> choose [
                path "/command" >=> handShake RouteHandlers.Api.command
                GET >=> path Path.status >=> OK "up"
            ]
            browseHome
            NOT_FOUND "No such route."
        ]
    let _listenting, server = Web.startWebServerAsync suaveConfig app
    Async.Start(server, cancel.Token)
    { new IDisposable with member this.Dispose() = cancel.Cancel() }