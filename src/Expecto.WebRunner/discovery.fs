namespace Expecto.WebRunner

open System
open System.Collections.Generic
open System.Reflection
open System.Runtime.CompilerServices
open Expecto
open Expecto.Impl

module TestDiscovery = 
    
    type DiscoveredTestList =
        {   assemblyPath : string
            assemblyName : string
            testCases : DiscoveredTestCase seq }
    and DiscoveredTestCase = 
        {   testCode : string
            typeFullName : string
            typeNamespace : string
            typeName : string
            methodName : string
            codeFilePath : string option
            lineNumber : int option
            assemblyPath : string }
        static member Zero = 
            {   testCode = String.Empty
                typeFullName = String.Empty
                typeNamespace = String.Empty
                typeName = String.Empty
                methodName = String.Empty
                codeFilePath = None
                lineNumber = None
                assemblyPath = String.Empty }
        static member Create testCode typeNamespace typeName typeFullName methodName = 
            { DiscoveredTestCase.Zero with 
                testCode = testCode
                typeNamespace = typeNamespace
                typeName = typeName 
                typeFullName = typeFullName
                methodName = methodName }
    
    let getFuncTypeAndMethodToUse (testFunc:TestCode) (asm:Assembly) =
        let traverseObjectGraph (root : obj) =
            //Safeguard against circular references
            let rec inner traversed current = seq {
                let currentComparable = {
                    new obj() with
                        member __.Equals(other) = current.Equals other
                        member __.GetHashCode() = current.GetHashCode()
                    interface IComparable with
                        member this.CompareTo(other) =
                                this.GetHashCode().CompareTo(other.GetHashCode())
                }
                if current = null ||
                    Set.contains currentComparable traversed then do () else

                let newTraversed = Set.add currentComparable traversed
                yield current
                yield! current.GetType().GetFields(BindingFlags.Instance |||
                                                    BindingFlags.NonPublic |||
                                                    BindingFlags.Public)
                        |> Seq.collect (fun info -> info.GetValue(current)
                                                    |> inner newTraversed)
            }
            inner Set.empty root

        query {
            for o in traverseObjectGraph testFunc do
            let oType = o.GetType()
            where (oType.Assembly.FullName = asm.FullName)
            for m in oType.GetMethods() do
            where (m.Name = "Invoke" && m.DeclaringType = oType)
            select (Some (oType, m))
            headOrDefault
        }
 
    let discoverTests(asm:Assembly) =
        if not <| SourceLocation.referencesExpecto(asm) then
            Array.empty
        else
            asm
            |> SourceLocation.loadTestListFromAssembly
            |> Expecto.Test.toTestCodeList
            |> Seq.map (fun flatTest ->
                let (ns,typeFullName,typeName,methodName) =
                    match getFuncTypeAndMethodToUse flatTest.test asm with
                    | None -> "Unknown","Unknown","Unknown","Unknown"
                    | Some (t, m) -> t.Namespace,t.FullName,t.Name,m.Name
                DiscoveredTestCase.Create flatTest.name ns typeFullName typeName methodName)
            |> Array.ofSeq

    module Proxies =
        open Remoting
        type TestDiscoveryProxy() =
            inherit MarshalByRefObjectInfiniteLease()
            member this.DiscoverTests(source: string) =
                Assembly.LoadFrom(source)
                |> discoverTests

    open Fake
    open Hopac

    let discoverFromAssembly (assemblyPath:string) =
        async {
            printfn "discovering: %s" assemblyPath
            
            use host = new Remoting.TestAssemblyHost(assemblyPath)
            let discoverProxy = host.CreateInAppdomain<Proxies.TestDiscoveryProxy>()
            let testList = discoverProxy.DiscoverTests(assemblyPath)
            //let getSourceLocation = SourceLocation.makeSourceLocator assemblyPath
            return
                {   assemblyPath = assemblyPath 
                    assemblyName = Fake.FileHelper.fileNameWithoutExt assemblyPath
                    testCases =
                        testList
                        |> Seq.map (fun tc ->
                            let sourceLocation : SourceLocation option = None // getSourceLocation tc.typeName tc.methodName
                            { tc with
                                assemblyPath = assemblyPath
                                codeFilePath = None //sourceLocation |> Option.map (fun l -> l.SourcePath)
                                lineNumber = None //sourceLocation |> Option.map (fun l -> l.LineNumber) 
                                })
                            |> Seq.toArray }
        }

    let discoverAll (projectDir:string) (filter:string option) =
        let filter' = defaultArg filter "tests/**/bin/Debug/*Tests*.exe"
        let sources = !! (projectDir @@ filter') 
        sources
        |> Seq.map (fun assemblyPath -> 
            let discover = 
                async {
                    printfn "discovering: %s" assemblyPath
            
                    use host = new Remoting.TestAssemblyHost(assemblyPath)
                    let discoverProxy = host.CreateInAppdomain<Proxies.TestDiscoveryProxy>()
                    let testList = discoverProxy.DiscoverTests(assemblyPath)
                    //let getSourceLocation = SourceLocation.makeSourceLocator assemblyPath
                    return
                        {   assemblyPath = assemblyPath 
                            assemblyName = Fake.FileHelper.fileNameWithoutExt assemblyPath
                            testCases =
                                testList
                                |> Seq.map (fun tc ->
                                    //let sourceLocation = getSourceLocation tc.typeName tc.methodName
                                    { tc with
                                        assemblyPath = assemblyPath
                                        codeFilePath = None //sourceLocation |> Option.map (fun l -> l.SourcePath)
                                        lineNumber = None //sourceLocation |> Option.map (fun l -> l.LineNumber) 
                                        })
                                    |> Seq.toArray }
                }
            discover)
        |> Async.Parallel
