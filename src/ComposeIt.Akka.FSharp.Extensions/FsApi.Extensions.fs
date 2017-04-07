namespace ComposeIt.Akka.FSharp.Extensions

module Lifecycle =
    
    open System
    open Akka.Actor
    open Akka.FSharp
    open Akka.FSharp.Linq
    open Microsoft.FSharp.Linq
    open Microsoft.FSharp.Quotations
    open Microsoft.FSharp.Linq.QuotationEvaluation

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
    and [<Struct>]AsyncDecorator<'Message>(asyncDecorator: Async<Decorator<'Message>>) =
        member __.Decorator = asyncDecorator
        interface Decorator<'Message>

    type ExpressionExt = 
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunActorExt<'Message, 'v>>>) = toExpression<FunActorExt<'Message, 'v>> f
        static member ToExpression<'Actor>(f : Quotations.Expr<(unit -> 'Actor)>) = toExpression<'Actor> (QuotationEvaluator.ToLinqExpression f)

    
    let (|Become|_|) (effect: Decorator<'Message>) =
        if effect :? Become<'Message>
        then Some ((effect :?> Become<'Message>).Next)
        else None

    /// Gives access to the next message throu let! binding in actor computation expression.
    //type Behavior<'In, 'Out> = 
    //    | Become of ('In -> Behavior<'In, 'Out>)
    //    | Return of 'Out

    /// The builder for actor computation expression.
    type ActorBuilder() =
        member __.Bind(_ : IO<'In>, continuation : 'In -> Decorator<'In>) : Decorator<'Message> = upcast Become(fun message -> continuation message)
        member this.Bind(behavior : Decorator<'In>, continuation : Decorator<'In> -> Decorator<'In>) : Decorator<'In> = 
            match behavior with
            | :? Become<'In> as become -> Become<'In>(fun message -> this.Bind(become.Next message, continuation)) :> Decorator<'In>
            | returned -> continuation returned    
        member __.Bind(asyncInput: Async<'In>, continuation: 'In -> Decorator<'Out>) : Decorator<'Out> =
            upcast AsyncDecorator (async {
                let! returned = asyncInput 
                return continuation returned 
            })
        member __.ReturnFrom (effect: Decorator<'Message>) = effect
        member __.Return (value: Decorator<'Message>) : Decorator<'Message> = value
        member __.Zero () : Decorator<'Message> = Empty :> Decorator<'Message>
        member __.Yield value = value

        member this.TryWith(tryExpr : unit -> Decorator<'In>, catchExpr : exn -> Decorator<'In>) : Decorator<'In> = 
            try 
                true, tryExpr ()
            with error -> false, catchExpr error
            |> function 
            | true, Become(next) -> Become<'In>(fun message -> this.TryWith((fun () -> next message), catchExpr)) :> Decorator<'In>
            | _, value -> value    

        member this.TryFinally(tryExpr : unit -> Decorator<'In>, finallyExpr : unit -> unit) : Decorator<'In> = 
            try 
                match tryExpr() with
                | Become next -> Become(fun message -> this.TryFinally((fun () -> next message), finallyExpr)) :> Decorator<'In>
                | behavior -> 
                    finallyExpr()
                    behavior
            with error -> 
                finallyExpr()
                reraise()
    
        member this.Using(disposable : #IDisposable, continuation : _ -> Decorator<'In>) : Decorator<'In> = 
            this.TryFinally((fun () -> continuation disposable), fun () -> if disposable <> null then disposable.Dispose())
    
        member this.While(condition : unit -> bool, continuation : unit -> Decorator<'In>) : Decorator<'In> = 
            if condition() then 
                match continuation() with
                | Become next -> 
                    Become (fun message -> 
                        next message |> ignore
                        this.While(condition, continuation)) :> Decorator<'In>
                | _ -> this.While(condition, continuation)
            else Empty :> Decorator<'In>
    
        member __.For(iterable : 'Iter seq, continuation : 'Iter -> Decorator<'In>) : Decorator<'In> = 
            use e = iterable.GetEnumerator()
        
            let rec loop() = 
                if e.MoveNext() then 
                    match continuation e.Current with
                    | Become fn -> 
                        Become(fun m -> 
                            fn m |> ignore
                            loop()) :> Decorator<'In>
                    | _ -> loop()
                else Empty :> Decorator<'In>
            loop()
    
        member __.Delay(continuation : unit -> Decorator<'In>) = continuation
        member __.Run(continuation : unit -> Decorator<'In>) = continuation ()
        member __.Run(continuation : Decorator<'In>) = continuation
    
        member this.Combine(first : unit -> Decorator<'In>, second : unit -> Decorator<'In>) : Decorator<'In> = 
            match first () with
            | Become next -> Become(fun message -> this.Combine((fun () -> next message), second)) :> Decorator<'In>
            | _ -> second ()
    
        member this.Combine(first : Decorator<'In>, second : unit -> Decorator<'In>) : Decorator<'In> = 
            match first with
            | Become next -> Become(fun message -> this.Combine(next message, second)) :> Decorator<'In>
            | _ -> second ()
    
        member this.Combine(first : unit -> Decorator<'In>, second : Decorator<'In>) : Decorator<'In> = 
            match first () with
            | Become next -> Become(fun message -> this.Combine((fun () -> next message), second)) :> Decorator<'In>
            | _ -> second
    
        member this.Combine(first : Decorator<'In>, second : Decorator<'In>) : Decorator<'In> = 
            match first with
            | Become next -> Become(fun message -> this.Combine(next message, second)) :> Decorator<'In>
            | _ -> second
        
    /// Builds an actor message handler using an actor expression syntax.
    let actor = ActorBuilder()

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
    /// Spawns an actor using specified actor computation expression, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="options">List of options used to configure actor creation</param>
    /// <param name="overrides">Functions used to override standard actor lifetime</param>
    let spawnOptOvrd2 (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Decorator<'Message>) 
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
    /// Spawns an actor using specified actor computation expression.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="overrides">Functions used to override standard actor lifetime</param>
    let spawnOvrd2 (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Decorator<'Message>)
        (overrides : LifecycleOverride) : IActorRef = 
        spawnOptOvrd2 actorFactory name f [] overrides

    /// <summary>
    /// Wraps provided function with actor behavior. 
    /// It will be invoked each time, an actor will receive a message. 
    /// </summary>
    let actorOf (fn : 'Message -> #Decorator<'Message>) (mailbox : Actor<'Message>) : Decorator<'Message> = 
        let rec loop() = 
            actor { 
                let! msg = mailbox.Receive()
                return fn msg 
            }
        loop()

    /// <summary>
    /// Returns an actor effect causing no changes in message handling pipeline.
    /// </summary>
    let inline empty (_: 'Any) : Decorator<'Message> = ActorAction.Empty :> Decorator<'Message>

    /// <summary>
    /// Returns an actor effect causing actor to switch its behavior.
    /// </summary>
    /// <param name="next">New receive function.</param>
    let inline become (next) : Decorator<'Message> = Become(next) :> Decorator<'Message>