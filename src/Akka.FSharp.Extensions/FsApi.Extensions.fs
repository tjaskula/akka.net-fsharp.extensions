namespace Akka.FSharp.Extensions

module Actor =
    
    open Akka.Actor
    open Akka.FSharp
    open Akka.FSharp.Linq
    open System
    open Microsoft.FSharp.Reflection
    open FSharp.Quotations.Evaluator

    type LifecycleMessage = 
        | PreStart
        | PostStop
        | PreRestart of cause : exn * message : obj
        | PostRestart of cause : exn

    type ActorMessage =
        | Lifecycle of LifecycleMessage
        | Message of obj

    let (|Message|_|) (msg : ActorMessage) = 
        match msg with
        | Message m -> 
            try
                Some(unbox m)
            with
            | :? System.InvalidCastException -> None 
        | _ -> None

    type FunActorExt<'Message, 'Returned>(actor : Actor<'Message> -> Cont<'Message, 'Returned>) as this =
        inherit FunActor<'Message, 'Returned>(actor)

        let mutable postStopWasHandled = false
        
        member __.Handle (msg : obj) =
            let conv = 
                match msg with
                | :? LifecycleMessage -> Lifecycle (msg :?> LifecycleMessage)
                | _ -> Message(msg)
            base.OnReceive(conv)

        override this.OnReceive msg = this.Handle msg

        override this.PreStart() = 
            base.PreStart ()
            this.Handle PreStart

        override this.PostStop() =
            if not postStopWasHandled then
                postStopWasHandled <- true
                base.PostStop ()
                this.Handle PostStop

        override this.PreRestart(exn, msg) =
            base.PreRestart (exn, msg)
            this.Handle(PreRestart(exn, msg))

        override this.PostRestart(exn) =
            base.PostRestart (exn)
            this.Handle(PostRestart exn)

    and ExpressionExt = 
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunActorExt<'Message, 'v>>>) = f
        static member ToExpression<'Actor>(f : Quotations.Expr<(unit -> 'Actor)>) = toExpression<'Actor> (QuotationEvaluator.ToLinqExpression f)

    /// <summary>
    /// Spawns an actor using specified actor computation expression, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="options">List of options used to configure actor creation</param>
    let spawnOpt (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) 
        (options : SpawnOption list) : IActorRef = 
        let e = ExpressionExt.ToExpression(fun () -> new FunActorExt<'Message, 'Returned>(f))
        let props = applySpawnOptions (Props.Create e) options
        actorFactory.ActorOf(props, name)

    /// <summary>
    /// Spawns an actor using specified actor computation expression.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    let spawn (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) : IActorRef = 
        spawnOpt actorFactory name f []

    /// <summary>
    /// Wraps provided function with actor behavior. 
    /// It will be invoked each time, an actor will receive a message. 
    /// </summary>
    let actorOf (fn : 'Message -> Cont<'Message, 'Returned>) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned> = 
        let rec loop() = 
            actor { 
                let! msg = mailbox.Receive()
                return! fn msg 
            }
        loop()

    /// <summary>
    /// Wraps provided function with actor behavior. 
    /// It will be invoked each time, an actor will receive a message. 
    /// </summary>
    let actorOf2 (fn : Actor<'Message> -> 'Message -> Cont<'Message, 'Returned>) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned> = 
        let rec loop() = 
            actor {
                let! msg = mailbox.Receive()
                return! fn mailbox msg
            }
        loop()

    /// <summary>
    /// Returns a continuation stopping the message handling pipeline.
    /// </summary>
    let empty : Cont<'Message, 'Returned> = Return ()

    /// <summary>
    /// Returns a continuation causing actor to switch its behavior.
    /// </summary>
    /// <param name="next">New receive function.</param>
    let inline become (next) : Cont<'Message, 'Returned> = Func(next)