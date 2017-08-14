namespace Expecto.WebRunner

open System
open System.Collections.Generic
open System.Reflection
open System.Runtime.CompilerServices
open Expecto
open Expecto.Impl


module SourceLocation =
    open Mono.Cecil.Cil
    open Mono.Cecil
    open Mono.Cecil.Rocks

    type SourceLocation =
        {
            SourcePath: string
            LineNumber: int
        }

    [<AutoOpen>]
    module private Impl =
        let lineNumberIndicatingHiddenLine = 0xfeefee
        let getEcma335TypeName (clrTypeName:string) = clrTypeName.Replace("+", "/")

        let enumerateTypes (assemblyPath:string) =
            let readerParams = new ReaderParameters()
            readerParams.ReadSymbols <- true 
            readerParams.InMemory <- true
            let moduleDefinition = ModuleDefinition.ReadModule(assemblyPath, readerParams)
            moduleDefinition.GetTypes()
            |> Seq.map (fun t -> 
                t.FullName, t)
            |> Map.ofSeq
            
        let getMethods typeName (types:Map<string,TypeDefinition>) =
            types
            |> Map.tryFind (getEcma335TypeName typeName)
            |> Option.map (fun t -> t.GetMethods())

        let optionFromObj = function 
            | null -> None 
            | x -> Some x
            
        let getSequencePoint(mapping: IDictionary<Instruction, SequencePoint>, instruction): SequencePoint =
            let (success, value) = mapping.TryGetValue instruction
            value

        let isStartDifferentThanHidden(seqPoint: SequencePoint) =
            if seqPoint = null then false
            else if seqPoint.StartLine <> lineNumberIndicatingHiddenLine then true
            else false

        let getFirstOrDefaultSequencePoint (m:MethodDefinition) =
            try
                let mapping = m.DebugInformation.GetSequencePointMapping()
        
                m.Body.Instructions
                |> Seq.tryFind (fun i -> (getSequencePoint(mapping, i) |> isStartDifferentThanHidden))
                |> Option.map (fun i -> (getSequencePoint(mapping, i)))
            with 
            | exn ->
                printfn "getFirstOrDefaultSequencePoint: %A" m
                None
                    
    let makeSourceLocator assemblyPath =
        let types = enumerateTypes assemblyPath
        fun className methodName ->
            match types |> getMethods className with
            | Some methods ->
                let candidateSequencePoints =
                    methods
                    |> Seq.filter (fun m -> m.Name = methodName)
                    |> Seq.choose getFirstOrDefaultSequencePoint
                query
                    {
                    for sp in candidateSequencePoints do
                    sortBy sp.StartLine
                    select { SourcePath = sp.Document.Url; LineNumber = sp.StartLine }
                    take 1
                    }
                |> Seq.tryFind (fun _ -> true)
            | _ -> None

    let referencesExpecto (asm:Assembly) = 
        asm.GetReferencedAssemblies()
        |> Array.exists (fun a -> a.Name = "Expecto")

    let loadTestListFromAssembly(asm:Assembly) =  
        match testFromAssembly (asm) with
        | Some t -> t
        | None -> TestList ([], Normal)

module TestDiscovery = 
    
    type DiscoveredTestList =
        {   assemblyName : string
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

    let discoverAll (projectDir:string) (filter:string option) =
        let filter' = defaultArg filter "tests/**/bin/Debug/*Tests*.exe"
        let sources = !! (projectDir @@ filter') 
        sources
        |> Seq.map (fun assemblyPath ->
            use host = new Remoting.TestAssemblyHost(assemblyPath)
            let discoverProxy = host.CreateInAppdomain<Proxies.TestDiscoveryProxy>()
            let testList = discoverProxy.DiscoverTests(assemblyPath)
            let getSourceLocation = SourceLocation.makeSourceLocator assemblyPath
            {   assemblyName = Fake.FileHelper.fileNameWithoutExt assemblyPath
                testCases =
                    testList
                    |> Seq.map (fun tc ->
                        let sourceLocation = getSourceLocation tc.typeName tc.methodName
                        { tc with
                            assemblyPath = assemblyPath
                            codeFilePath = sourceLocation |> Option.map (fun l -> l.SourcePath)
                            lineNumber = sourceLocation |> Option.map (fun l -> l.LineNumber) })
                        |> Seq.toArray })
        |> Seq.toArray
        
module TestExecution =
    open Fake.FSIHelper
    open HttpFs.Logging.DateTime

    type ExecutionStatusUpdate =
        | BeforeRun of Test
        | BeforeEach of name:string
        | Info of info:string
        | Summary of TestRunSummary
        | Passed of name:string*duration:TimeSpan
        | Ignored of name:string*message:string
        | Failed of name:string*message:string*duration:TimeSpan
        | Exception of name:string*exn:exn*duration:TimeSpan
        
    let executeTests(source:string) (sink:string->unit) = 
        let asm = Assembly.LoadFrom(source)
        let tests = SourceLocation.loadTestListFromAssembly asm
        let postUpdate status = async.Return <| sink (serialize status) 
        let testPrinters = 
            {   beforeRun = (BeforeRun >> postUpdate)
                beforeEach = (BeforeEach >> postUpdate)
                info = (Info >> postUpdate)
                summary = (fun results summary -> Summary summary |> postUpdate)
                passed = (fun name duration -> Passed(name,duration) |> postUpdate)
                ignored = (fun name message -> Ignored(name,message) |> postUpdate)
                failed = (fun name message duration -> Failed(name,message,duration) |> postUpdate)
                exn = (fun name ex duration -> Exception(name,ex,duration) |> postUpdate) 
            }
        let conf = { defaultConfig with printer = testPrinters }
        runTests conf tests |> ignore

    type TestSeq() =
        inherit Remoting.MarshalByRefObjectInfiniteLease()

    module Proxies = 
        // The curious 1-tuple argument here is so that we can force the AppDomain class to locate the correct
        // constructor. It enables us to pass in an argument that is unambiguously typed as the required
        // interface. (Otherwise, it ends up being of type TestExecutionRecorderProxy, and the dynamic
        // creation attempt seems not to be able to work out that it needs the constructor that takes an
        // IObserver<string*string>)
        type Sink() =
            inherit Remoting.MarshalByRefObjectInfiniteLease()
            member this.Hello() = printfn "hello"

        type ExecuteProxy(temp:TestSeq, assemblyPath: string, testsToInclude: string[]) =
            inherit Remoting.MarshalByRefObjectInfiniteLease()
            //let vsCallback: VsCallbackForwarder = new VsCallbackForwarder(proxyHandler.Item1, assemblyPath)
            member this.ExecuteTests() =
                executeTests assemblyPath (fun s -> printfn "%s" s)
        
    
    let executeAllTests (sinkFn:string->unit) (assemblyPath) =
        use host = new Remoting.TestAssemblyHost(assemblyPath)
        let testsToInclude = Array.empty<string>
        let proxy = host.CreateInAppdomain<Proxies.ExecuteProxy>([|TestSeq(), assemblyPath; testsToInclude|])
        proxy.ExecuteTests()

//executeTests "../tests/RS.Core.Tests/bin/Debug/RS.Core.Tests.exe"




