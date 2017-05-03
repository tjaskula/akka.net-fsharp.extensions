module ComposeIt.Akka.FSharp.Extensions.Tests

open Akka.FSharp
open Akka.Actor
open ComposeIt.Akka.FSharp.Extensions.Actor
open System
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
                let! msg = mailbox.Receive()
                match msg with
                | Lifecycle e -> 
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
                let! msg = mailbox.Receive()
                match msg with
                | Lifecycle e -> 
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
                let! msg = mailbox.Receive()
                match msg with
                | Lifecycle e -> 
                    match e with
                    | PreRestart(_) -> preRestartCalled := true
                    | _ -> ()
                | Message m ->
                    if m = "restart"
                    then failwith "System must be restarted"
                    else mailbox.Sender() <! m
                | _ -> mailbox.Sender() <! msg
                return! loop ()
            }
            loop ()
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun _ -> Directive.Restart)) ]
    actor <! "restart"
    (actor <? "msg" |> Async.RunSynchronously) |> ignore
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
                let! msg = mailbox.Receive()
                match msg with
                | Lifecycle e -> 
                    match e with
                    | PostRestart _ -> postRestartCalled := true
                    | _ -> ()
                | Message m -> 
                    if m = "restart"
                    then failwith "System must be restarted"
                    else mailbox.Sender() <! m
                | _ -> mailbox.Sender() <! msg
                return! loop ()
            }
            loop ()
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun _ -> Directive.Restart)) ]
    actor <! "restart"
    (actor <? "msg" |> Async.RunSynchronously) |> ignore
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
        | Message m ->
        match m with
            | Print -> answer := sprintf "Last name was %s?" lastName 
                       empty
            | MyName(who) ->
                become (namePrinter who)
        | _ -> become (namePrinter lastName)

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

type PlayMovieMessage = {MovieTitle : string; UserId : int}

type PlayerMessage =
    | PlayMovie of PlayMovieMessage
    | StopMovie

[<Fact>]
let ``should forward not expected messages types to unhandled mailbox`` () =
    
    let answer = ref (Message(0))

    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawn system "actor"
        <| fun mailbox ->
            let rec loop() = actor {                
                let! msg = mailbox.Receive()
                match msg with
                    | Lifecycle _ -> ()
                    | Message m -> 
                        match m with
                        | msg when msg.UserId > 40 -> ()
                        | t -> answer := msg
                               mailbox.Unhandled msg

                return! loop()
            }
            loop()
    actor <! {MovieTitle = "Akka.NET : The Movie"; UserId = 42}
    actor <! {MovieTitle = "Partial Recall"; UserId = 99}
    actor <! {MovieTitle = "Boolean Lies"; UserId = 77}
    actor <! {MovieTitle = "Codenan the Destroyer"; UserId = 1}
    actor <! 87
    system.Terminate() |> ignore
    system.WhenTerminated.Wait(TimeSpan.FromSeconds(2.)) |> ignore
    answer.Value |> equals (Message(87))