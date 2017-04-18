# Akka.FSharp.API.Extensions
## Set of extensions to the Akka.NET F# API

This package contains some extensions to the [Akka.Net](http://getakka.net/) F# APIs. [![NuGet](https://img.shields.io/badge/nuget-v0.1.1.1-blue.svg)](https://www.nuget.org/packages/Akka.NET.FSharp.API.Extensions/)

### Installation

Run the following command in the [Package Manager Console](http://docs.nuget.org/docs/start-here/using-the-package-manager-console)

```
PM> Install-Package Akka.NET.FSharp.API.Extensions
```

### Overriding Actor's Lifecycle
if you use extensively `actor` Computation Expression from the original [Akka.FSharp](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.FSharp/FsApi.fs#L191-L322) package, you have certainly noticed that there are no way to handle the actor's lifecycle (`PreStart`, `PostStop`, `PreRestart`, `PostRestart`).

One way to achieve that is through types instead of `actor` computation expression. Here is an example:

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

With `Akka.FSharp.API.Extensions` you can handle `actor`'s lifecycle events directly through user predefinied messages.
The idea was "borrowed" from the excellent [Akka.Net](http://getakka.net/) F# Api implementation [Akkling](https://github.com/Horusiath/Akkling/wiki/Managing-actor's-lifecycle) although the implementation is different and based directly on the original Akka.Net F# Api.

### Simple usage:

Let's say we would like to handle the `PreStart` method:

```fsharp
	use system = System.create "actor-system" (Configuration.load())
		let actor = 
			spawn system "actor" 
			<| fun mailbox ->
				let rec loop() = actor {
					let! (msg : obj) = mailbox.Receive()
					match msg with
					| LifecycleEvent e -> 
						match e with
						| PreStart -> () // do whatever you need to do
						| _ -> ()
					| _ -> ()
					return! loop ()
				}
				loop ()
```

### Stateful Actors

You can create simply stateful actors with `become` function. An example is more worth than words:

```fsharp
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
```

# Maintainer

- [@tjaskula](https://twitter.com/tjaskula)