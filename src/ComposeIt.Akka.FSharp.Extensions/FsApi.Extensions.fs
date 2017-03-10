namespace ComposeIt.Akka.FSharp.Extensions

module Lifecycle =
    
    open Akka.Actor
    open Akka.FSharp
    open Akka.FSharp.Linq
    open Microsoft.FSharp.Linq

    type LifecycleOverride =
        {
            PreStart    : ((unit -> unit) -> unit) option;
            PostStop    : ((unit -> unit) -> unit) option;
            PreRestart  : ((exn * obj -> unit) -> unit) option;
            PostRestart : ((exn -> unit) -> unit) option;
        }

    let defOvrd = {PreStart = None; PostStop = None; PreRestart = None; PostRestart = None}
    
    type FunActorExt<'Message, 'Returned>(actor : Actor<'Message> -> Cont<'Message, 'Returned>, overrides : LifecycleOverride) =
        inherit FunActor<'Message, 'Returned>(actor)

        member __.BasePreStart() = base.PreStart ()
        member __.BasePostStop() = base.PostStop ()
        member __.BasePreRestart(exn, msg) = base.PreRestart (exn, msg)
        member __.BasePostRestart(exn) = base.PostRestart (exn)

        member __.Next (current : Decorator<'Message>) (context : Actor<'Message>) (message : obj) : Decorator<'Message> = 
            match message with
            | :? 'Message as msg -> 
                match current with
                | :? Become<'Message> as become -> become.Next msg
                | _ -> current
            | other -> 
                base.Unhandled other
                current

        override x.PreStart() = 
            match overrides.PreStart with
            | None -> x.BasePreStart ()
            | Some o -> o x.BasePreStart
        override x.PostStop() =
            match overrides.PostStop with
            | None -> x.BasePostStop ()
            | Some o -> o x.BasePostStop
        override x.PreRestart(exn, msg) =
            match overrides.PreRestart with
            | None -> x.BasePreRestart (exn, msg)
            | Some o -> o x.BasePreRestart
        override x.PostRestart(exn) =
            match overrides.PostRestart with
            | None -> x.BasePostRestart (exn)
            | Some o -> o x.BasePostRestart

    and [<Interface>]Decorator<'Message> = interface end
    and ActorAction<'Message> =
        | Empty
        interface Decorator<'Message>
    and [<Struct>]Become<'Message>(next: 'Message -> Decorator<'Message>) =
        member x.Next = next
        interface Decorator<'Message>

    type ExpressionExt = 
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunActorExt<'Message, 'v>>>) = toExpression<FunActorExt<'Message, 'v>> f
        static member ToExpression<'Actor>(f : Quotations.Expr<(unit -> 'Actor)>) = toExpression<'Actor> (QuotationEvaluator.ToLinqExpression f)  

    /// <summary>
    /// Spawns an actor using specified actor computation expression, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="options">List of options used to configure actor creation</param>
    /// <param name="overrides">Functions used to override standard actor lifetime</param>
    let spawnOptOvrd (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) 
        (options : SpawnOption list) (overrides : LifecycleOverride) : IActorRef = 
        let e = ExpressionExt.ToExpression(fun () -> new FunActorExt<'Message, 'Returned>(f, overrides))
        let props = applySpawnOptions (Props.Create e) options
        actorFactory.ActorOf(props, name)

    /// <summary>
    /// Spawns an actor using specified actor computation expression.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="overrides">Functions used to override standard actor lifetime</param>
    let spawnOvrd (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>)
        (overrides : LifecycleOverride) : IActorRef = 
        spawnOptOvrd actorFactory name f [] overrides

    /// <summary>
    /// Returns an actor effect causing no changes in message handling pipeline.
    /// </summary>
    let inline empty (_: 'Any) : Decorator<'Message> = ActorAction.Empty :> Decorator<'Message>

    /// <summary>
    /// Returns an actor effect causing actor to switch its behavior.
    /// </summary>
    /// <param name="next">New receive function.</param>
    let inline become (next) : Decorator<'Message> = Become(next) :> Decorator<'Message>

    let (|Become|_|) (effect: Decorator<'Message>) =
        if effect :? Become<'Message>
        then Some ((effect :?> Become<'Message>).Next)
        else None