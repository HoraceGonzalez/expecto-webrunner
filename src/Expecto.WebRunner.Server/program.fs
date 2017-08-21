namespace Expecto.WebRunner

module Program =
    open System.Threading
    open System
    open System.IO

    module CommandLine =
        open Argu
    
        module Defaults =
            let projectDirectory = "../../"
            let assemblyFileFilter = "tests/**/bin/Debug/*Tests.exe"

        type Arguments =
            | [<AltCommandLine("-p")>] ProjectDirectory of string
            | [<AltCommandLine("-f")>] AssemblyFileFilter of string
            interface IArgParserTemplate with
                member this.Usage =
                    match this with
                    | ProjectDirectory _ -> 
                        sprintf "specifies the project directory path. Defaults to \"%s\"" Defaults.projectDirectory
                    | AssemblyFileFilter _ -> 
                         sprintf "specifies a test assembly file filter pattern. Defaults to \"%s\"" Defaults.assemblyFileFilter

    [<EntryPoint>]
    let main args =

        // parse command line args
        let args' = 
            let parser = Argu.ArgumentParser.Create<CommandLine.Arguments>() 
            parser.Parse(args, ignoreUnrecognized = true)
        let projectDir = 
            let dir = args'.GetResult (<@ CommandLine.Arguments.ProjectDirectory @>, defaultValue = CommandLine.Defaults.projectDirectory)           
            Path.GetFullPath dir 
        let filter = args'.GetResult (<@ CommandLine.Arguments.AssemblyFileFilter @>, defaultValue = CommandLine.Defaults.assemblyFileFilter)
 
        use mre = new ManualResetEventSlim(false)
        //use _sub = Console.CancelKeyPress.Subscribe (fun e -> 
        //    e.Cancel <- true
        //    Log.debug "Received the cancel command from console"
        //    mre.Set())
        use _server = App.spawnServer projectDir filter
        //use _waitABeat = Timers.postpone 200.<ms> (fun () -> Log.debug "Main thread is waiting for CancelKeyPress")
        mre.Wait()
        0