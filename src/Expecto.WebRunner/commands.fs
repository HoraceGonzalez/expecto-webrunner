namespace Expecto.WebRunner

open System
open System.IO

open Expecto.WebRunner.TestDiscovery

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CommandHandler = 
    open System.Collections.Generic

    [<AutoOpen>]
    module Models = 
        type Command =
            {   commandName : string
                testCodes : string array
                assemblyName : string }
        type StatusUpdate =
            {   updateName : string
                data : obj }
            static member create name data = 
                { updateName = name; data = data }
        and SelectedTest = 
            {   assemblyName:string
                testCode:string }
    
    [<AutoOpen>]
    module private _Impl =
        module Conversions =            
            let inline private update name (data:(string*obj) seq) = 
                StatusUpdate.create name (dict data)
              
            let toStatusUpdate(e:TestExecution.ExecutionStatusUpdate) = 
                match e with
                | TestExecution.ExecutionStatusUpdate.BeforeEach testCode ->
                    update "TestStarting" [ "name" => testCode ]
                | TestExecution.ExecutionStatusUpdate.BeforeRun source ->
                    update "TestingStarting" ["source" => source]
                | TestExecution.ExecutionStatusUpdate.Exception (name,exn,duration) ->
                    update "TestException" [
                        "name" => name 
                        "message" => exn.Message 
                        "duration" => duration.ToString("c") ]
                | TestExecution.ExecutionStatusUpdate.Failed(name,message,duration) ->
                    update "TestFailed" [ 
                        "name" => name 
                        "message" => message
                        "duration" => duration.ToString("c") ]
                | TestExecution.ExecutionStatusUpdate.Ignored(name,message) ->
                    update "TestIgnored" [ 
                        "name" => name 
                        "message" => message ]
                | TestExecution.ExecutionStatusUpdate.Info(message) ->
                    update "TestInfo" ["message" => message ]
                | TestExecution.ExecutionStatusUpdate.Passed(name,duration) ->
                    update "TestPassed" [ 
                        "name" => name
                        "duration" => duration.ToString("c") ]
                | TestExecution.ExecutionStatusUpdate.Summary summary->
                    update "TestingComplete" [
                        "successful" => summary.successful
                        "duration" => summary.duration.ToString("c")
                        "errors" => summary.errors
                        "failures" => summary.failures
                        "ignores" => summary.ignores
                        "passes" => summary.passes
                        "total" => summary.total ]
    
        type LoadedTestSet() =
            // a set of test lists
            let testLists = Dictionary<string,DiscoveredTestList>()
        
            // a set of filesystem watchers.
            let fileWatchers = Dictionary<string,IDisposable>()
        
            member this.Add (testList:DiscoveredTestList) onTestBinaryChange =
                // dispose of the file watcher if one already exists
                if fileWatchers.ContainsKey testList.assemblyName
                    then do fileWatchers.[testList.assemblyName].Dispose()
            
                testLists.[testList.assemblyName] <- testList
                fileWatchers.[testList.assemblyName] <- File.watch true testList.assemblyPath onTestBinaryChange
            member this.GetTestList assemblyName =
                let (exists,testList) = testLists.TryGetValue(assemblyName)
                if exists 
                    then Some testList
                    else None
            member this.GetTestSet() =
                testLists
                |> Seq.collect (fun kv ->
                    kv.Value.testCases
                    |> Seq.map (fun test -> kv.Value.assemblyPath,test))
                |> Seq.toList
            interface IDisposable with 
                member this.Dispose() = 
                    fileWatchers |> Seq.iter (fun w -> w.Value.Dispose())
                    testLists.Clear()
                    fileWatchers.Clear()

        type CommandType =
            | DiscoverAll
            | Rediscover of sourceAssembly:string
            | ExecuteAll
            | ExecuteSelected of tests:SelectedTest list

    type CommandHandler(projectDir:string,notify:Models.StatusUpdate->unit) =
        let agent = MailboxProcessor<CommandType>.Start(fun inbox ->
            let discoveredTestLists = ref (new LoadedTestSet())
            let rediscover (args:FileSystemEventArgs) = 
                // notify client that file has changed
                StatusUpdate.create "TestListChanged" (dict ["assemblyName" => args.Name])
                |> notify
                // redo the discovery
                inbox.Post(Rediscover args.FullPath)
            let notifyExecutionStatus = 
                Conversions.toStatusUpdate
                >> notify
            // Loop that waits for the agent and writes to web socket
            let rec loop() = 
                async {
                    try
                        let! msg = inbox.Receive()
                        match msg with
                        | DiscoverAll ->
                            let! testLists = discoverAll projectDir None 
                            (!discoveredTestLists :> IDisposable).Dispose()
                            discoveredTestLists := new LoadedTestSet()
                            for test in testLists do
                                (!discoveredTestLists).Add test rediscover
                            StatusUpdate.create "TestSetDiscovered" testLists
                            |> notify
                        | Rediscover assemblyPath ->
                            let! testList = TestDiscovery.discoverFromAssembly assemblyPath
                            (!discoveredTestLists).Add testList rediscover
                            StatusUpdate.create "TestListDiscovered" testList
                            |> notify
                        | ExecuteSelected tests ->
                            do! tests
                                |> List.choose(fun selectedTest ->
                                    selectedTest.assemblyName
                                    |> (!discoveredTestLists).GetTestList
                                    |> Option.map (fun t -> 
                                        { TestExecution.TestSource.assemblyPath = t.assemblyPath
                                          TestExecution.TestSource.testCode = selectedTest.testCode }))
                                |> TestExecution.executeTests notifyExecutionStatus
                        | ExecuteAll ->
                            do! ((!discoveredTestLists).GetTestSet())
                                |> Seq.map (fun (assemblyPath,test) ->
                                    {   TestExecution.TestSource.assemblyPath = assemblyPath
                                        TestExecution.TestSource.testCode = test.testCode })
                                |> TestExecution.executeTests notifyExecutionStatus
                    with
                    | exn -> printfn "Kablamo %A" exn
                    do! loop()
                }
            loop())
        member this.Notify msg = notify msg
        member this.DiscoverAllAndNotify() = agent.Post(DiscoverAll)
        member this.RediscoverAndNotify(source) = agent.Post(Rediscover source)
        member this.ExecuteAllAndNotify() = agent.Post(ExecuteAll)
        member this.ExecuteSelectedAndNotify (tests) = agent.Post(ExecuteSelected tests)
        interface IDisposable with
            member this.Dispose() = 
                ()
                (agent :> IDisposable).Dispose()

