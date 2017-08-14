namespace Expecto.WebRunner

module Program =
    open System.Threading
    
    [<EntryPoint>]
    let main _ =
        use mre = new ManualResetEventSlim(false)
        //use _sub = Console.CancelKeyPress.Subscribe (fun e -> 
        //    e.Cancel <- true
        //    Log.debug "Received the cancel command from console"
        //    mre.Set())
        use _server = App.spawnServer()
        //use _waitABeat = Timers.postpone 200.<ms> (fun () -> Log.debug "Main thread is waiting for CancelKeyPress")
        mre.Wait()
        0