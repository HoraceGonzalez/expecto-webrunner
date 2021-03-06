﻿namespace Expecto.WebRunner

open System
open System.Collections.Generic
open System.Reflection
open System.Runtime.CompilerServices
open Expecto
open Expecto.Impl

module TestExecution =
    open MBrace.FsPickler
    open Fake
    open HttpFs.Logging.DateTime

    type ExecutionStatusUpdate =
        | BeforeRun of source:string
        | BeforeEach of name:string
        | Info of info:string
        | Summary of Summary
        | Passed of name:string*duration:TimeSpan
        | Ignored of name:string*message:string
        | Failed of name:string*message:string*duration:TimeSpan
        | Exception of name:string*exn:exn*duration:TimeSpan
    and Summary =
        {   successful : bool
            total : int
            duration : TimeSpan
            passes : int
            ignores : int
            failures : int
            errors : int }
        static member OfTestRunSummary(summary:TestRunSummary) =
            {   successful = summary.successful
                total = List.sumBy (fun (_,r) -> r.count) summary.results
                duration = summary.duration
                passes = List.sumBy (fun (_,r) -> r.count) summary.passed
                ignores = List.sumBy (fun (_,r) -> r.count) summary.ignored
                failures = List.sumBy (fun (_,r) -> r.count) summary.failed
                errors = List.sumBy (fun (_,r) -> r.count) summary.errored }
                
    type Sink =
        { SendMessage : ExecutionStatusUpdate -> unit }
        
    let binarySerializer = FsPickler.CreateBinarySerializer()
    
    type TestExecutionRecorderProxy(recorder:Sink, assemblyPath:string) =
        inherit Remoting.MarshalByRefObjectInfiniteLease()
        interface IObserver<byte[]> with
            member this.OnCompleted() = ()
            member x.OnError(error) =
                Exception("unhandled exception",error,TimeSpan.MinValue)
                |> recorder.SendMessage
            member x.OnNext(message) = 
                binarySerializer.UnPickle<ExecutionStatusUpdate> message
                |> recorder.SendMessage
                
    let executeTestsFromAssembly(source:string) (sink:IObserver<byte[]>) (includeTests:string seq option) = 
        let filterTests = 
            match includeTests with
            | Some tests ->
                let set = tests |> Set.ofSeq
                fun t -> set |> Set.contains t
            | None -> 
                fun _ -> true
                
        let postUpdate status = async.Return <| sink.OnNext(binarySerializer.Pickle status) 
        let testPrinters = 
            {   beforeRun = (fun test -> BeforeRun source |> postUpdate)
                beforeEach = (BeforeEach >> postUpdate)
                info = (Info >> postUpdate)
                summary = (fun results summary -> 
                    Summary.OfTestRunSummary summary |> Summary |> postUpdate)
                passed = (fun name duration -> Passed(name,duration) |> postUpdate)
                ignored = (fun name message -> Ignored(name,message) |> postUpdate)
                failed = (fun name message duration -> Failed(name,message,duration) |> postUpdate)
                exn = (fun name ex duration -> Exception(name,ex,duration) |> postUpdate) 
            }
            
        Assembly.LoadFrom(source)
        |> SourceLocation.loadTestListFromAssembly
        |> Expecto.Test.filter filterTests
        |> Expecto.Tests.runTests { defaultConfig with printer = testPrinters }
        
    module Proxies = 
        // The curious 1-tuple argument here is so that we can force the AppDomain class to locate the correct
        // constructor. It enables us to pass in an argument that is unambiguously typed as the required
        // interface. (Otherwise, it ends up being of type TestExecutionRecorderProxy, and the dynamic
        // creation attempt seems not to be able to work out that it needs the constructor that takes an
        // IObserver<string>)
        //
        // Also note, there's some subtle .net black magic going on here. The constructor signature of one of these 
        // proxy can't reference any types not known to *both* the host domain (ie. the Expecto.WebRunner.dll assembly)
        // and known to the child domain (ie. the expecto test assembly. IObservable<> works as a message channel
        // because it's a part of the .net BCL, so both domains will be able to resolve it. Otherwise if we wanted
        // use a type we defined, a shared library would be necessary, which would be kind of incovenient.
        type ExecuteProxy(messageChannel: Tuple<IObserver<byte[]>>, assemblyPath: string, testsToInclude: string[]) =
            inherit Remoting.MarshalByRefObjectInfiniteLease()
            member this.ExecuteTests() =
                let tests = 
                    match testsToInclude with
                    | [||] -> None
                    | _ -> Some (Seq.ofArray testsToInclude)
                executeTestsFromAssembly assemblyPath messageChannel.Item1 tests
    
    type TestSource = 
        {   assemblyPath : string
            testCode : string }

    let executeTests (sinkFn:ExecutionStatusUpdate->unit) (sources:TestSource seq) =
        sources
        |> Seq.groupBy (fun s -> s.assemblyPath)
        |> Seq.map (fun (assemblyPath,sources) ->
            let testsToInclude = 
                sources 
                |> Seq.map (fun s -> s.testCode)
                |> Seq.toArray
            assemblyPath,testsToInclude)
        |> Seq.map (fun (assemblyPath,testsToInclude) ->
            async {
                use host = new Remoting.TestAssemblyHost(assemblyPath)
                let messageChannel = Tuple.Create(new TestExecutionRecorderProxy({ SendMessage = sinkFn }, assemblyPath) :> IObserver<byte[]>)
                let proxy = host.CreateInAppdomain<Proxies.ExecuteProxy>([|messageChannel; assemblyPath; testsToInclude|])
                do (proxy.ExecuteTests() |> ignore)
            })
        |> Seq.toList
        |> Async.Parallel
        |> Async.Ignore




