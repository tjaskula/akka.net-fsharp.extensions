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

        member __.Next (current : ContWrapper<'Message, 'Returned>) (context : Actor<'Message>) (message : obj) : ContWrapper<'Message, 'Returned> = 
            match message with
            | :? 'Message as msg -> 
                match current with
                | :? Become<'Message, 'Returned> as become -> become.Next msg
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

    and [<Interface>]ContWrapper<'Message, 'Returned> =
        abstract Continuation : 'Message -> Cont<'Message, 'Returned>
    and ActorAction<'Message, 'Returned> =
        | Empty
        interface ContWrapper<'Message, 'Returned> with
            member __.Continuation = let d = fun (m :'Returned) -> Return(m)
    and [<Struct>]Become<'Message, 'Returned>(next: 'Message -> ContWrapper<'Message, 'Returned>) =
        member x.Next = next
        interface ContWrapper<'Message, 'Returned>
            member __.Continuation f = Func(fun m -> f m)
    and [<Struct>]AsyncContWrapper<'Message, 'Returned>(asyncDecorator: Async<ContWrapper<'Message, 'Returned>>) =
        member __.Decorator = asyncDecorator
        interface ContWrapper<'Message, 'Returned>
            member __.Continuation f = Func(fun m -> f m)

    type ExpressionExt = 
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunActorExt<'Message, 'v>>>) = toExpression<FunActorExt<'Message, 'v>> f
        static member ToExpression<'Actor>(f : Quotations.Expr<(unit -> 'Actor)>) = toExpression<'Actor> (QuotationEvaluator.ToLinqExpression f)

    
    let (|Become|_|) (continuation: ContWrapper<'Message, 'Returned>) =
        if continuation :? Become<'Message, 'Returned>
        then Some ((continuation :?> Become<'Message, 'Returned>).Next)
        else None

    /// Gives access to the next message throu let! binding in actor computation expression.
    //type Behavior<'In, 'Out> = 
    //    | Become of ('In -> Behavior<'In, 'Out>)
    //    | Return of 'Out

    /// The builder for actor computation expression.
    type ActorBuilder() =
        member __.Bind(_ : IO<'In>, continuation : 'In -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = upcast Become(fun message -> continuation message)
        member this.Bind(behavior : ContWrapper<'In, 'Out>, continuation : ContWrapper<'In, 'Out> -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            match behavior with
            | :? Become<'In, 'Out> as become -> Become<'In, 'Out>(fun message -> this.Bind(become.Next message, continuation)) :> ContWrapper<'In, 'Out>
            | returned -> continuation returned    
        member __.Bind(asyncInput: Async<'In>, continuation: 'In -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> =
            upcast AsyncContWrapper (async {
                let! returned = asyncInput 
                return continuation returned 
            })
        member __.ReturnFrom (effect: ContWrapper<'In, 'Out>) = effect
        member __.Return (value: ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = value
        member __.Zero () : ContWrapper<'In, 'Out> = Empty :> ContWrapper<'In, 'Out>
        member __.Yield value = value

        member this.TryWith(tryExpr : unit -> ContWrapper<'In, 'Out>, catchExpr : exn -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            try 
                true, tryExpr ()
            with error -> false, catchExpr error
            |> function 
            | true, Become(next) -> Become<'In, 'Out>(fun message -> this.TryWith((fun () -> next message), catchExpr)) :> ContWrapper<'In, 'Out>
            | _, value -> value    

        member this.TryFinally(tryExpr : unit -> ContWrapper<'In, 'Out>, finallyExpr : unit -> unit) : ContWrapper<'In, 'Out> = 
            try 
                match tryExpr() with
                | Become next -> Become(fun message -> this.TryFinally((fun () -> next message), finallyExpr)) :> ContWrapper<'In, 'Out>
                | behavior -> 
                    finallyExpr()
                    behavior
            with error -> 
                finallyExpr()
                reraise()
    
        member this.Using(disposable : #IDisposable, continuation : _ -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            this.TryFinally((fun () -> continuation disposable), fun () -> if disposable <> null then disposable.Dispose())
    
        member this.While(condition : unit -> bool, continuation : unit -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            if condition() then 
                match continuation() with
                | Become next -> 
                    Become (fun message -> 
                        next message |> ignore
                        this.While(condition, continuation)) :> ContWrapper<'In, 'Out>
                | _ -> this.While(condition, continuation)
            else Empty :> ContWrapper<'In, 'Out>
    
        member __.For(iterable : 'Iter seq, continuation : 'Iter -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            use e = iterable.GetEnumerator()
        
            let rec loop() = 
                if e.MoveNext() then 
                    match continuation e.Current with
                    | Become fn -> 
                        Become(fun m -> 
                            fn m |> ignore
                            loop()) :> ContWrapper<'In, 'Out>
                    | _ -> loop()
                else Empty :> ContWrapper<'In, 'Out>
            loop()
    
        member __.Delay(continuation : unit -> ContWrapper<'In, 'Out>) = continuation
        member __.Run(continuation : unit -> ContWrapper<'In, 'Out>) = continuation ()
        member __.Run(continuation : ContWrapper<'In, 'Out>) = continuation
    
        member this.Combine(first : unit -> ContWrapper<'In, 'Out>, second : unit -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            match first () with
            | Become next -> Become(fun message -> this.Combine((fun () -> next message), second)) :> ContWrapper<'In, 'Out>
            | _ -> second ()
    
        member this.Combine(first : ContWrapper<'In, 'Out>, second : unit -> ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            match first with
            | Become next -> Become(fun message -> this.Combine(next message, second)) :> ContWrapper<'In, 'Out>
            | _ -> second ()
    
        member this.Combine(first : unit -> ContWrapper<'In, 'Out>, second : ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            match first () with
            | Become next -> Become(fun message -> this.Combine((fun () -> next message), second)) :> ContWrapper<'In, 'Out>
            | _ -> second
    
        member this.Combine(first : ContWrapper<'In, 'Out>, second : ContWrapper<'In, 'Out>) : ContWrapper<'In, 'Out> = 
            match first with
            | Become next -> Become(fun message -> this.Combine(next message, second)) :> ContWrapper<'In, 'Out>
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
    let spawnOptOvrd (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> ContWrapper<'Message, 'Returned>) 
        (options : SpawnOption list) (overrides : LifecycleOverride) : IActorRef = 
        let unwrapCont (f : Actor<'Message> -> ContWrapper<'Message, 'Returned>) : Actor<'Message> -> Cont<'Message, 'Returned> =
            let continuation a = f(a).Continuation(fun m -> )
            continuation
        let e = ExpressionExt.ToExpression(fun () -> new FunActorExt<'Message, 'Returned>(unwrapCont f, overrides))
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
    let spawnOvrd (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> ContWrapper<'Message, 'Returned>)
        (overrides : LifecycleOverride) : IActorRef = 
        spawnOptOvrd actorFactory name f [] overrides

    /// <summary>
    /// Wraps provided function with actor behavior. 
    /// It will be invoked each time, an actor will receive a message. 
    /// </summary>
    let actorOf (fn : 'Message -> #ContWrapper<'Message, 'Returned>) (mailbox : Actor<'Message>) : ContWrapper<'Message, 'Returned> = 
        let rec loop() = 
            actor { 
                let! msg = mailbox.Receive()
                return fn msg 
            }
        loop()

    /// <summary>
    /// Returns an actor effect causing no changes in message handling pipeline.
    /// </summary>
    let inline empty (_: 'Any) : ContWrapper<'Message, 'Returned> = ActorAction.Empty :> ContWrapper<'Message, 'Returned>

    /// <summary>
    /// Returns an actor effect causing actor to switch its behavior.
    /// </summary>
    /// <param name="next">New receive function.</param>
    let inline become (next) : ContWrapper<'Message, 'Returned> = Become(next) :> ContWrapper<'Message, 'Returned>