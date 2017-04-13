﻿module ComposeIt.Akka.FSharp.Extensions.Tests

open Akka.FSharp
open Akka.Actor
open ComposeIt.Akka.FSharp.Extensions.Lifecycle
open System
open System.Threading.Tasks
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
    | Print
    | MyName of string

[<Fact>]
let ``can change behaviour with become`` () =
    
    let answer = ref "no one"

    let rec namePrinter lastName = function
        | Print -> answer := sprintf "Last name was %s?" lastName
                   answer |> empty
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