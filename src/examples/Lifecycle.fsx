#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.FSharp.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/ComposeIt.Akka.FSharp.Extensions.dll"

open Akka.FSharp
open Akka.Actor
open ComposeIt.Akka.FSharp.Extensions.Actor
open System

let preRestartCalled = ref false
    
let system = System.create "testSystem" (Configuration.load())
let actor = 
    spawnOpt system "actor" 
    <| fun mailbox ->
        let rec loop() = actor {
            let! msg = mailbox.Receive()
            match msg with
            | Lifecycle e -> 
                match e with
                | PreRestart(_, _) -> preRestartCalled := true
                | _ -> ()
            | Message m -> 
                if m = "restart"
                then failwith "System must be restarted"
                else mailbox.Sender() <! m
            | _ -> mailbox.Sender() <! msg
            return! loop ()
        }
        loop ()
    <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error -> Directive.Restart)) ]

actor <! "restart"
actor <? "msg" |> Async.RunSynchronously |> string
system.Terminate() |> ignore
system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore