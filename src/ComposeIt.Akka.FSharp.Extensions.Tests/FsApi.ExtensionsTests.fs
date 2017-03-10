module ComposeIt.Akka.FSharp.Extensions.Tests

open Akka.FSharp
open Akka.Actor
open ComposeIt.Akka.FSharp.Extensions.Lifecycle
open System
open Xunit

let equals (expected: 'a) (value: 'a) = Assert.Equal<'a>(expected, value) 
let success = ()

[<Fact>]
let ``can override PreStart method when starting actor with computation expression`` () =
    
    let preStartCalled = ref false
    let preStart = Some(fun (baseFn : unit -> unit) -> preStartCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOvrd system "actor" 
        <| actorOf2 (fun mailbox msg ->
                mailbox.Sender() <! msg)
        <| {defOvrd with PreStart = preStart}
    let response = actor <? "msg" |> Async.RunSynchronously
    (!preStartCalled, response) |> equals (true, "msg")

[<Fact>]
let ``can override PostStop methods when starting actor with computation expression`` () =
    
    let postStopCalled = ref false
    let postStop = Some(fun (baseFn : unit -> unit) -> postStopCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOvrd system "actor" 
        <| actorOf2 (fun mailbox msg ->
                mailbox.Sender() <! msg)
        <| {defOvrd with PostStop = postStop}
    actor <! PoisonPill.Instance
    system.Stop(actor)
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    (!postStopCalled) |> equals (true)

[<Fact>]
let ``can override PreRestart methods when starting actor with computation expression`` () =
    
    let preRestartCalled = ref false
    let preRestart = Some(fun (baseFn : exn * obj -> unit) -> preRestartCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOptOvrd system "actor3" 
        <| actorOf2 (fun mailbox (msg : string) ->
                if msg = "restart" then
                    failwith "System must be restarted"
                else
                    mailbox.Sender() <! msg)
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error ->
                Directive.Restart)) ]
        <| {defOvrd with PreRestart = preRestart}
    actor <! "restart"
    let response = actor <? "msg" |> Async.RunSynchronously
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    (!preRestartCalled, response) |> equals (true, "msg")

[<Fact>]
let ``can override PostRestart methods when starting actor with computation expression`` () =
    
    let postRestartCalled = ref false
    let postRestart = Some(fun (baseFn : exn -> unit) -> postRestartCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOptOvrd system "actor4" 
        <| actorOf2 (fun mailbox (msg : string) ->
                if msg = "restart" then
                    failwith "System must be restarted"
                else
                    mailbox.Sender() <! msg)
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error ->
                Directive.Restart)) ]
        <| {defOvrd with PostRestart = postRestart}
    actor <! "restart"
    let response = actor <? "msg" |> Async.RunSynchronously
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    (!postRestartCalled, response) |> equals (true, "msg")

type Message =
    | Hi
    | Greet of string

[<Fact>]
let ``can change behaviour with become`` () =
    
    let preStartCalled = ref false
    let preStart = Some(fun (baseFn : unit -> unit) -> preStartCalled := true)

    let rec greeter lastKnown = function
        | Hi -> printfn "Who sent Hi? %s?" lastKnown |> empty
        | Greet(who) ->
            printfn "%s sends greetings" |> ignore
            become (greeter who)

    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOvrd system "actor" 
        <| actorOf (greeter "Me")
        <| {defOvrd with PreStart = preStart}
    let response = actor <? "msg" |> Async.RunSynchronously
    (!preStartCalled, response) |> equals (true, "msg")