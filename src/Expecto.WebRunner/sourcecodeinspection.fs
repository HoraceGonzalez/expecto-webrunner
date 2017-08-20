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

