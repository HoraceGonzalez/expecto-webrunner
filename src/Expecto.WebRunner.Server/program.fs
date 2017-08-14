namespace Expecto.WebRunner

module Program =
    open System.Threading
    
    [<EntryPoint>]
    let main _ =
        let assembly = @"C:\code\RealtyShares.New\bizapps\tests\RS.Core.Tests\bin\Debug\RS.Core.Tests.exe"
        let updateFn status = 
            printf "%s" (serialize status)
        TestExecution.executeAllTests updateFn (assembly)
        0
        //use mre = new ManualResetEventSlim(false)
        ////use _sub = Console.CancelKeyPress.Subscribe (fun e -> 
        ////    e.Cancel <- true
        ////    Log.debug "Received the cancel command from console"
        ////    mre.Set())
        //use _server = spawnServer()
        ////use _waitABeat = Timers.postpone 200.<ms> (fun () -> Log.debug "Main thread is waiting for CancelKeyPress")
        //mre.Wait()
        //0