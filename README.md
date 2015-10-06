# Akka.Fsharp.API.Extensions
## Set of extensions to the Akka.NET F# API

This package contains some extensions to the [Akka.Net](http://getakka.net/) F# APIs.

### Overriding Actor's Lifecycle
if your are using extensively `actor` Computation Expression from the original [Akka.FSharp](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.FSharp/FsApi.fs#L191-L322) package, you will notice that there are no means to override all of the actor's lifecycle (`PreStart`, `PostStop`, `PreRestart`, `PostRestart`).

One way of doing it is use types instead of `actor` computation expression. Here is an example:

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