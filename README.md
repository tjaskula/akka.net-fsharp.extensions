# Akka.Fsharp.API.Extensions
## Set of extensions to the Akka.NET F# API

This package contains some extensions to the [Akka.Net](http://getakka.net/) F# APIs. [![NuGet](https://img.shields.io/badge/nuget-v0.1.0.1-blue.svg)](https://www.nuget.org/packages/Akka.NET.FSharp.API.Extensions/)

### Installation


### Overriding Actor's Lifecycle
if you use extensively `actor` Computation Expression from the original [Akka.FSharp](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.FSharp/FsApi.fs#L191-L322) package, you have certainly noticed that there are no means to override all of the actor's lifecycle (`PreStart`, `PostStop`, `PreRestart`, `PostRestart`).

One way of doing it is usesing types instead of `actor` computation expression. Here is an example:

```fsharp
type PlaybackActor() =    
    inherit UntypedActor()

    override __.OnReceive message =
        match message with
        | :? string as e -> // sample with handling a string
        | :? int as i -> // sample with handling an int
        | _ -> __.Unhandled(message)

    override __.PreStart() =
        // do something, like logging for example

    override __.PostStop() =
        // do something, like logging for example

    override __.PreRestart (e, message) =
        // do something, like logging for example
        base.PreRestart(e, message)

    override __.PostRestart e =
        // do something, like logging for example
        base.PostRestart(e)
```

With `Akka.Fsharp.API.Extensions` you can spawn `actor`'s with two new functions, taking overrides functions as parameters. The new functions are definied as follows:

The `spawnOptOvrd` is based on the original `spawnOpt`. As the last parameter (`overrides : LifecycleOverride`) you can pass your overriding lifecycle functions. Here is the function signature:

```fsharp
 let spawnOptOvrd (actorFactory : IActorRefFactory) 
 				  (name : string) 
 				  (f : Actor<'Message> -> Cont<'Message, 'Returned>) 
        		  (options : SpawnOption list) (overrides : LifecycleOverride) : IActorRef =
        // body of the function
```

The `spawnOvrd` is based on the original `spawn`. As the last parameter (`overrides : LifecycleOverride`) you can pass your overriding lifecycle functions. Here is the function signature:

```fsharp
let spawnOvrd (actorFactory : IActorRefFactory) 
			  (name : string) 
			  (f : Actor<'Message> -> Cont<'Message, 'Returned>)
        	  (overrides : LifecycleOverride) : IActorRef = 
        // body of the function
```

Simple usage:

Let's say we would lile to override the `PostRestart` method. We can supply the overriding function body as follows:

```fsharp
let postRestart = Some(fun (baseFn : exn -> unit) -> /* you can log here */ )
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOptOvrd system "actorSys" 
        <| actorOf2 (fun mailbox (msg : string) ->
                if msg = "restart" then
                    failwith "System must be restarted"
                else
                    mailbox.Sender() <! msg)
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error ->
                Directive.Restart)) ]
        <| {defOvrd with PostRestart = postRestart}
```