// sets the current directory to be same as the script directory
System.IO.Directory.SetCurrentDirectory (__SOURCE_DIRECTORY__)

#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.FSharp.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/ComposeIt.Akka.FSharp.Extensions.dll"

open Akka.FSharp
open ComposeIt.Akka.FSharp.Extensions.Lifecycle

let preStartCalled = ref false
let preStart = Some(fun (baseFn : unit -> unit) -> preStartCalled := true)

type Message =
    | Hi
    | Greet of string

let rec greeter lastKnown = function
    | Hi -> printfn "Who sent Hi? %s?" lastKnown |> empty
    | Greet(who) ->
        printfn "%s sends greetings" who
        become (greeter who)

let system = System.create "testSystem" (Configuration.load())

let actor = 
    spawnOvrd system "actor" 
    <| actorOf (greeter "Me")
    <| {defOvrd with PreStart = preStart}

actor <! Greet "Tom"
actor <! Greet "Jane"
actor <! Hi