#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.FSharp.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/ComposeIt.Akka.FSharp.Extensions.dll"

open Akka.FSharp
open ComposeIt.Akka.FSharp.Extensions.Lifecycle

type Message =
    | Print
    | MyName of string

let rec namePrinter lastName = function
    | Print -> printfn "Last name was %s?" lastName |> empty
    | MyName(who) ->
        printfn "Hello %s!" who
        become (namePrinter who)

let system = System.create "testSystem" (Configuration.load())

let actor = 
    spawn system "actor" 
    <| actorOf (namePrinter "No One")

actor <! MyName "Tomasz"
actor <! MyName "Marcel"
actor <! Print