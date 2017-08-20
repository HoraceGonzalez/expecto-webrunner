module Expecto.WebRunner.RouteHandlers

open System

module StaticContext = 
    open Suave
    let assets = Files.dir "assets"

module Html =
    open Suave.DotLiquid
    open DotLiquid
    
    setTemplatesDir "assets"

    let indexPage = page "index.html" ()

module Api = 
    open Expecto.WebRunner
    open Expecto.WebRunner.CommandHandler

    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.WebSocket

    [<AutoOpen>]
    module private WebSocketHelpers = 
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

        type SocketReplyChannel (ws:WebSocket) = 
            let agent = 
                MailboxProcessor<StatusUpdate>.Start(fun inbox ->
                let rec loop() =
                    async {
                        let! msg = inbox.Receive()
                        do! ws |> serializeToSocket msg |> Async.Ignore
                        do! loop()
                    }
                loop())
            member this.Reply(update) = agent.Post(update)
            interface IDisposable with
                member this.Dispose() = (agent :> IDisposable).Dispose()
            
    let command (projectDir:string) (webSocket : WebSocket) =
            
        let cts = new System.Threading.CancellationTokenSource()
        socket {
            use notifier = new SocketReplyChannel(webSocket)
            use session = new CommandHandler(projectDir,notifier.Reply)
        
            // if `loop` is set to false, the server will stop receiving messages
            let mutable loop = true

            while loop do   
                // the server will wait for a message to be received without blocking the thread
                let! msg = webSocket.read()

                match msg with
                | (Text, data, true) ->
                    match deserializeFromSocket<Models.Command> data with 
                    | Success cmd ->
                        StatusUpdate.create "AcceptedCommand" (dict[])
                        |> notifier.Reply

                        match cmd.commandName with
                        | "discover all" ->
                            session.DiscoverAllAndNotify()
                        | "run all" ->
                            session.ExecuteAllAndNotify()
                        | "run test" ->
                            printfn "running testcodes:\n\n %s" (cmd.testCodes |?? Array.empty |> String.concat "\n")

                            if not <| String.IsNullOrWhiteSpace(cmd.assemblyName) then
                                cmd.testCodes |?? Array.empty
                                |> Seq.map (fun testCode -> 
                                    {   assemblyName = cmd.assemblyName
                                        testCode = testCode })
                                |> Seq.toList
                                |> session.ExecuteSelectedAndNotify
                        | _ ->
                            StatusUpdate.create "UnrecognizedCommand" (dict[])
                            |> notifier.Reply
                    | Failure _ ->
                        // the `send` function sends a message back to the client
                        StatusUpdate.create "UnrecognizedCommand" (dict[])
                        |> notifier.Reply
                | (Close, _, _) ->
                    let emptyResponse = [||] |> ByteSegment
                    do! webSocket.send Close emptyResponse true
                    cts.Dispose()
                    // after sending a Close message, stop the loop
                    loop <- false

                | _ -> ()
        }
