module Expecto.WebRunner.RouteHandlers

open System

module StaticContext = 
    open Suave
    let assets = Files.dir "C:\code\RealtyShares.New\expecto-webrunner\src\Expecto.WebRunner.Server\assets"

module Html =
    open Hopac
    open Suave
    open Suave.Filters
    open Suave.Operators
    open Suave.Successful
    open Suave.RequestErrors
    open Suave.DotLiquid
    open DotLiquid
    
    setTemplatesDir (@"assets")

    let indexPage = page "index.html" ()

module Api = 
    open Expecto.WebRunner
    open Expecto.WebRunner.Remoting
    open Expecto.WebRunner.TestDiscovery

    module Models = 
        type Command =
            { commandName : string }
        type StatusUpdate =
            { updateName : string }
            
    let discover() =
        let projectDir = "c:/code/realtyshares.new/bizapps/"
        let testLists = discoverAll projectDir None
        testLists

    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.WebSocket

    let serializeToSocket o (ws:WebSocket) = 
        let msg = 
            serialize o
            |> System.Text.Encoding.UTF8.GetBytes
            |> ByteSegment
        ws.send Text msg true

    let deserializeFromSocket<'a> msgBytes = 
        let json =
            msgBytes
            |> System.Text.Encoding.UTF8.GetString
        tryDeserialize<'a> json
        

    let command (webSocket : WebSocket) (context: _) =
        let notifier = MailboxProcessor<string>.Start(fun inbox ->
            // Loop that waits for the agent and writes to web socket
            let rec notifyLoop() = 
                async { 
                    let! msg = inbox.Receive()
                    let! _ = webSocket |> serializeToSocket msg
                    do! notifyLoop()
                }
            notifyLoop())

        //// Start this using cancellation token, so that you can stop it later
        //Async.Start(notifyLoop, cts.Token)

        let cts = new System.Threading.CancellationTokenSource()
        let pinger() =
            let rec loop() = 
                async {
                    let msg = sprintf "the current time is %s" <| DateTime.Now.ToString("h:m:s tt")
                    printfn "sending: %s" msg
                    notifier.Post <| msg 
                    do! Async.Sleep 5000
                    return! loop()
                }
            loop()

        socket {
            // if `loop` is set to false, the server will stop receiving messages
            let mutable loop = true

            while loop do   
                // the server will wait for a message to be received without blocking the thread
                let! msg = webSocket.read()

                match msg with
                // the message has type (Opcode * byte [] * bool)
                //
                // Opcode type:
                //   type Opcode = Continuation | Text | Binary | Reserved | Close | Ping | Pong
                //
                // byte [] contains the actual message
                //
                // the last element is the FIN byte, explained later
                | (Text, data, true) ->
                    let cmd = deserializeFromSocket<Models.Command> data
                    match cmd with 
                    | Success cmd when cmd.commandName = "run all" ->
                        let assembly = @"C:\code\RealtyShares.New\bizapps\tests\RS.Core.Tests\bin\Debug\RS.Core.Tests.exe"
                        let updateFn status = 
                            notifier.Post (serialize status)
                        TestExecution.executeAllTests updateFn (assembly)
                        // the `send` function sends a message back to the client
                        let update = { Models.StatusUpdate.updateName = "AcceptedCommand" }
                        do! webSocket |> serializeToSocket update
                    | _ ->
                        // the `send` function sends a message back to the client
                        let update = { Models.StatusUpdate.updateName = "UnrecognizedCommand" }
                        do! webSocket |> serializeToSocket update
                | (Close, _, _) ->
                    let emptyResponse = [||] |> ByteSegment
                    do! webSocket.send Close emptyResponse true
                    cts.Dispose()
                    // after sending a Close message, stop the loop
                    loop <- false

                | _ -> ()
        }
