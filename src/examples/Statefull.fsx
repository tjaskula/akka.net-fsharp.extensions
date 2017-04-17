#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/Akka.FSharp.dll"
#r @"../ComposeIt.Akka.FSharp.Extensions/bin/Debug/ComposeIt.Akka.FSharp.Extensions.dll"

open Akka.FSharp
open ComposeIt.Akka.FSharp.Extensions.Actor

let system = System.create "testSystem" (Configuration.load())

// 1. Basic sample
type Message =
    | Print
    | MyName of string

let rec namePrinter lastName = function
    | Print -> printfn "Last name was %s?" lastName
               become (namePrinter lastName)
    | MyName(who) ->
        printfn "Hello %s!" who
        become (namePrinter who)

let actor = 
    spawn system "actor" 
    <| actorOf (namePrinter "No One")

actor <! MyName "Tomasz"
actor <! MyName "Marcel"
actor <! Print


// 2. More advanced sample
type Message' =
    | PlayMovie of string
    | StopMovie

type ActorState =
    | Playing of string
    | Stopped of string

let rec moviePlayer lastState = function
    | PlayMovie m -> 
        match lastState with
        | Playing _ -> printfn "Error: cannot start playing another movie before stopping existing one"
        | Stopped t -> printfn "Currently watching %s" t
                       printfn "User Actor has now become Playing"
        become (moviePlayer (Playing m))        
    | StopMovie -> 
        match lastState with
        | Playing t -> printfn "Stopped watching %s" t
                       printfn "User Actor has now become Stopped"
        | Stopped _ -> printfn "Error: cannot stop if nothing is playing"
        become (moviePlayer (Stopped ""))                    
    
let aref = 
    spawn system "UserActorBecome"
    <| actorOf( printfn "Creating a UserActor"
                printfn "Setting initial behavior to Stopped"
                moviePlayer (Stopped ""))

aref <! PlayMovie "The Walking Dead"
aref <! PlayMovie "Breaking Bad"
aref <! StopMovie
aref <! StopMovie