module ComposeIt.Akka.FSharp.Extensions.Tests

open Akka.FSharp
open Akka.Actor
open ComposeIt.Akka.FSharp.Extensions.Actor
open System
open System.Threading
open System.Threading.Tasks
open Xunit

let equals (expected: 'a) (value: 'a) = Assert.Equal<'a>(expected, value) 
let success = ()

[<Fact>]
let ``can override PreStart method when starting actor with computation expression`` () =
    
    let preStartCalled = ref false
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawn system "actor" 
        <| fun mailbox ->
            let rec loop() = actor {
                let! (msg : obj) = mailbox.Receive()
                match msg with
                | LifecycleEvent e -> 
                    match e with
                    | PreStart -> preStartCalled := true
                    | _ -> ()
                | _ -> ()
                return! loop ()
            }
            loop ()
    actor <! "msg"
    actor <! PoisonPill.Instance
    system.Stop(actor)
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    !preStartCalled |> equals true

[<Fact>]
let ``can override PostStop methods when starting actor with computation expression`` () =
    
    let postStopCalled = ref false
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawn system "actor" 
        <| fun mailbox ->
            let rec loop() = actor {
                let! (msg : obj) = mailbox.Receive()
                match msg with
                | LifecycleEvent e -> 
                    match e with
                    | PostStop -> postStopCalled := true
                    | _ -> ()
                | _ -> ()
                return! loop ()
            }
            loop ()
    actor <! PoisonPill.Instance
    system.Stop(actor)
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    (!postStopCalled) |> equals (true)

[<Fact>]
let ``can override PreRestart methods when starting actor with computation expression`` () =
    
    let preRestartCalled = ref false
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOpt system "actor" 
        <| fun mailbox ->
            let rec loop() = actor {
                let! (msg : obj) = mailbox.Receive()
                match msg with
                | LifecycleEvent e -> 
                    match e with
                    | PreRestart(_, _) -> preRestartCalled := true
                    | _ -> ()
                | :? string as m -> 
                    if m = "restart"
                    then failwith "System must be restarted"
                    else mailbox.Sender() <! m
                | _ -> mailbox.Sender() <! msg
                return! loop ()
            }
            loop ()
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error ->
                Directive.Restart)) ]
    actor <! "restart"
    let response = actor <? "msg" |> Async.RunSynchronously
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    !preRestartCalled |> equals true

[<Fact>]
let ``can override PostRestart methods when starting actor with computation expression`` () =
    
    let postRestartCalled = ref false
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOpt system "actor" 
        <| fun mailbox ->
            let rec loop() = actor {
                let! (msg : obj) = mailbox.Receive()
                match msg with
                | LifecycleEvent e -> 
                    match e with
                    | PostRestart exn -> postRestartCalled := true
                    | _ -> ()
                | :? string as m -> 
                    if m = "restart"
                    then failwith "System must be restarted"
                    else mailbox.Sender() <! m
                | _ -> mailbox.Sender() <! msg
                return! loop ()
            }
            loop ()
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error ->
                Directive.Restart)) ]
    actor <! "restart"
    let response = actor <? "msg" |> Async.RunSynchronously
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    !postRestartCalled |> equals true

type Message =
    | Print
    | MyName of string

[<Fact>]
let ``can change behaviour with become`` () =
    
    let answer = ref "no one"

    let rec namePrinter lastName = function
        | Print -> answer := sprintf "Last name was %s?" lastName 
                   empty
        | MyName(who) ->
            become (namePrinter who)

    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawn system "actor" 
        <| actorOf (namePrinter "No one")
    actor <! MyName "Tomasz"
    actor <! MyName "Marcel"
    actor <! Print
    Task.Delay(100).Wait()
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    (answer.Value) |> equals ("Last name was Marcel?")