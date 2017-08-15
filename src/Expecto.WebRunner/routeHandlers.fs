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
            {   commandName : string
                testCode : string
                assemblyPath : string }
        type StatusUpdate =
            {   updateName : string
                data : obj }
            
    let discover() =
        let projectDir = "c:/code/realtyshares.new/bizapps/"
        let testLists = discoverAll projectDir None
        testLists

    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.WebSocket
    open Expecto.WebRunner.TestExecution

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
                
    let command (webSocket : WebSocket) (context: _) =
        let notifier = MailboxProcessor<Models.StatusUpdate>.Start(fun inbox ->
            // Loop that waits for the agent and writes to web socket
            let rec notifyLoop() = 
                async { 
                    let! msg = inbox.Receive()
                    let! _ = webSocket |> serializeToSocket msg
                    do! notifyLoop()
                }
            notifyLoop())
            
        let cts = new System.Threading.CancellationTokenSource()

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
                            status
                            |> convertExecutionStatusUpdateToApi
                            |> notifier.Post
                        let update = { A.updateName = "AcceptedCommand"; A.data = dict[]}
                        TestExecution.executeAllTests updateFn (assembly)
                        // the `send` function sends a message back to the client
                        do! webSocket |> serializeToSocket update
                    | Success cmd when cmd.commandName = "run test" ->
                        // the `send` function sends a message back to the client
                        let update = { A.updateName = "AcceptedCommand"; A.data = dict["testCode" => cmd.testCode]}
                        do! webSocket |> serializeToSocket update
                    | _ ->
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
