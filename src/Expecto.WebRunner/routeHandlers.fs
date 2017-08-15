module Expecto.WebRunner.RouteHandlers

open System

module StaticContext = 
    open Suave
    let assets = Files.dir "assets"

module Html =
    open Hopac
    open Suave
    open Suave.Filters
    open Suave.Operators
    open Suave.Successful
    open Suave.RequestErrors
    open Suave.DotLiquid
    open DotLiquid
    
    setTemplatesDir "assets"

    let indexPage = page "index.html" ()

module Api = 
    open Expecto.WebRunner
    open Expecto.WebRunner.Remoting
    open Expecto.WebRunner.TestDiscovery

    module Models = 
        type Command =
            {   commandName : string
                testCode : string
                assemblyName : string }
        type StatusUpdate =
            {   updateName : string
                data : obj }
            
    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.WebSocket
    open Expecto.WebRunner.TestExecution
    open FSharpx.Functional.Lens

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
        
    let inline (=>) a b = a, b :> obj
    module A = Models
    let convertExecutionStatusUpdateToApi(e:TestExecution.ExecutionStatusUpdate) = 
        match e with
        | TestExecution.ExecutionStatusUpdate.BeforeEach testCode ->
            { A.updateName = "TestStarting" 
              A.data = dict [
                "name" => testCode ] }
        | TestExecution.ExecutionStatusUpdate.BeforeRun source ->
            { A.updateName = "TestingStarting"; A.data = dict ["source" => source] }
        | TestExecution.ExecutionStatusUpdate.Exception (name,exn,duration) ->
            { A.updateName = "TestException"; 
              A.data = dict [
                "name" => name 
                "message" => exn.Message 
                "duration" => duration.ToString("c") ] }
        | TestExecution.ExecutionStatusUpdate.Failed(name,message,duration) ->
            { A.updateName = "TestFailed"; 
              A.data = dict [
                "name" => name 
                "message" => message
                "duration" => duration.ToString("c") ] }
        | TestExecution.ExecutionStatusUpdate.Ignored(name,message) ->
            { A.updateName = "TestIgnored"; 
              A.data = dict [
                "name" => name 
                "message" => message ] }
        | TestExecution.ExecutionStatusUpdate.Info(message) ->
            { A.updateName = "TestInfo"; A.data = dict ["message" => message ] }
        | TestExecution.ExecutionStatusUpdate.Passed(name,duration) ->
            { A.updateName = "TestPassed"; 
              A.data = dict [
                "name" => name
                "duration" => duration.ToString("c") ] }
        | TestExecution.ExecutionStatusUpdate.Summary summary->
            { A.updateName = "TestingComplete"; 
              A.data = dict [
                "successful" => summary.successful
                "duration" => summary.duration.ToString("c")
                "errors" => summary.errors
                "failures" => summary.failures
                "ignores" => summary.ignores
                "passes" => summary.passes
                "total" => summary.total ] }
        
    type WebSocketSession(ws:WebSocket) =
        let notifier = MailboxProcessor<Models.StatusUpdate>.Start(fun inbox ->
            let rec loop() =
                async {
                    let! msg = inbox.Receive()
                    do! ws |> serializeToSocket msg |> Async.Ignore
                    do! loop()
                }
            loop())

        let agent = MailboxProcessor<SessionMessage>.Start(fun inbox ->
            let discoveredTestLists = System.Collections.Generic.Dictionary<string,DiscoveredTestList>()
            // Loop that waits for the agent and writes to web socket
            let rec loop() = 
                async {
                    try
                        let! msg = inbox.Receive()
                        match msg with
                        | DiscoverAll ->
                            let projectDir = "c:/code/realtyshares.new/bizapps/"
                            let! testLists = discoverAll projectDir None 
                            discoveredTestLists.Clear()
                            for test in testLists do
                                discoveredTestLists.[test.assemblyName] <- test
                            notifier.Post { A.updateName = "TestSetDiscovered"; A.data = testLists }
                        | Rediscover _ ->
                            ()
                        | ExecuteAll ->
                            let assembly = @"C:\code\RealtyShares.New\bizapps\tests\RS.Core.Tests\bin\Debug\RS.Core.Tests.exe"
                            let updateFn status = 
                                status
                                |> convertExecutionStatusUpdateToApi
                                |> notifier.Post

                            do! discoveredTestLists
                                |> Seq.map (fun kv -> kv.Value.assemblyPath)
                                |> TestExecution.executeAllTests updateFn
                    with
                    | exn -> printfn "Kablamo %A" exn

                    do! loop()
                }
            loop())
        member this.Notify msg = notifier.Post(msg)
        member this.DiscoverAllAndNotify() = agent.Post(DiscoverAll)
        member this.RediscoverAndNotify(source) = agent.Post(Rediscover source)
        member this.ExecuteAllAndNotify() = agent.Post(ExecuteAll)

    and private SessionMessage =
        | DiscoverAll
        | Rediscover of sourceAssembly:string
        | ExecuteAll

    let command (webSocket : WebSocket) (context: _) =
            
        let cts = new System.Threading.CancellationTokenSource()
        socket {
            let session = WebSocketSession(webSocket)
        
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
                    match deserializeFromSocket<Models.Command> data with 
                    | Success cmd ->
                        let update = { A.updateName = "AcceptedCommand"; A.data = dict[]}
                        do! webSocket |> serializeToSocket update
                            
                        match cmd.commandName with
                        | "discover all" ->
                            session.DiscoverAllAndNotify()
                        | "run all" ->
                            session.ExecuteAllAndNotify()
                        | "run test" ->
                            ()
                        | _ ->
                            let update = { A.updateName = "UnrecognizedCommand"; A.data = dict[]}
                            do! webSocket |> serializeToSocket update
                    | Failure _ ->
                        // the `send` function sends a message back to the client
                        let update = { A.updateName = "UnrecognizedCommand"; A.data = dict[]}
                        do! webSocket |> serializeToSocket update
                | (Close, _, _) ->
                    let emptyResponse = [||] |> ByteSegment
                    do! webSocket.send Close emptyResponse true
                    cts.Dispose()
                    // after sending a Close message, stop the loop
                    loop <- false

                | _ -> ()
        }
