﻿namespace FSharpx
#nowarn "40"

open System
open System.Collections
open System.Collections.Generic

module Monoid =
    
    /// Monoid (associative binary operation with identity)
    /// The monoid implementation comes from Matthew Podwysocki's http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/01/a-kick-in-the-monads-writer-edition.aspx.
    [<AbstractClass>]
    type Monoid_<'a>() =
        /// <summary>
        /// Identity
        /// </summary>
        abstract member mempty : 'a

        /// <summary>
        /// Associative operation
        /// </summary>
        abstract member mappend : 'a -> 'a -> 'a

        /// <summary>
        /// Fold a list using this monoid
        /// </summary>
        abstract member mconcat : 'a seq -> 'a
        default x.mconcat a = Seq.fold x.mappend x.mempty a
    
    /// List monoid
    type ListMonoid<'a>() =
        inherit Monoid_<'a list>()
            override this.mempty = []
            override this.mappend a b = a @ b

    /// Option wrapper monoid
    type OptionMonoid<'a>(m: 'a Monoid_) =
        inherit Monoid_<'a option>()
            override this.mempty = None
            override this.mappend a b = 
                match a,b with
                | Some a, Some b -> Some (m.mappend a b)
                | Some a, None   -> Some a
                | None, Some a   -> Some a
                | None, None     -> None
                
    /// Monoid (int,0,+)
    let IntSumMonoid = 
        { new Monoid_<int>() with
            override this.mempty = 0
            override this.mappend a b = a + b }

    /// Monoid (int,1,*)
    let IntProductMonoid =
        { new Monoid_<int>() with
            override this.mempty = 1
            override this.mappend a b = a * b }


    // Monoid instances
    type Dual<'a> = Dual of 'a with
        static member inline (?<-) (_     , _Monoid:Mempty , _:Dual<_>) = Dual (mempty()   )
        static member inline (?<-) (Dual x, _Monoid:Mappend,   Dual y ) = Dual (mappend y x)
    let getDual (Dual x) = x

    type Endo<'a> = Endo of ('a -> 'a) with
        static member        (?<-) (_     , _Monoid:Mempty , _:Endo<_>) = Endo id
        static member        (?<-) (Endo f, _Monoid:Mappend,   Endo g ) = Endo (f << g)

    let appEndo (Endo f) = f

    type All = All of bool with
        static member (?<-) (_    , _Monoid:Mempty , _:All  ) = All true
        static member (?<-) (All x, _Monoid:Mappend,   All y) = All (x && y)

    type Any = Any of bool with
        static member (?<-) (_    , _Monoid:Mempty , _:Any  ) = Any false
        static member (?<-) (Any x, _Monoid:Mappend,   Any y) = Any (x || y)

    type Sum<'a> = Sum of 'a with
        static member inline (?<-) (_    , _Monoid:Mempty , _:Sum<_>) = Sum LanguagePrimitives.GenericZero
        static member inline (?<-) (Sum x, _Monoid:Mappend,   Sum y ) = Sum (x + y)

    type Product<'a> = Product of 'a with
        static member inline (?<-) (_        , _Monoid:Mempty , _:Product<_>) = Product LanguagePrimitives.GenericOne
        static member inline (?<-) (Product x, _Monoid:Mappend,   Product y ) = Product (x * y)

/// Generic monadic operators    
module Operators =

    let inline returnM builder x = (^M: (member Return: 'b -> 'c) (builder, x))
    let inline bindM builder m f = (^M: (member Bind: 'd -> ('e -> 'c) -> 'c) (builder, m, f))
    let inline liftM builder f m =
        let inline ret x = returnM builder (f x)
        bindM builder m ret

    /// Sequential application
    let inline applyM (builder1:^M1) (builder2:^M2) f m =
        bindM builder1 f <| fun f' ->
            bindM builder2 m <| fun m' ->
                returnM builder2 (f' m') 

module Async =
    open Operators
        
    let inline bind f m = async.Bind(m,f)
    let inline returnM x = returnM async x
    let inline (>>=) m f = bindM async m f
    let inline (=<<) f m = bindM async m f
    /// Sequential application
    let inline (<*>) f m = applyM async async f m
    /// Sequential application
    let inline ap m f = f <*> m
    let inline pipe m f = liftM async f m
    let inline pipe2 x y f = returnM f <*> x <*> y
    let inline pipe3 x y z f = returnM f <*> x <*> y <*> z
    let inline map f m = pipe m f
    let inline lift2 f x y = returnM f <*> x <*> y
    let inline (<!>) f m = pipe m f
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = pipe2 x y (fun _ z -> z)
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = pipe2 x y (fun z _ -> z)

    /// Sequentially compose two async actions, discarding any value produced by the first
    let inline (>>.) m f = bindM async m (fun _ -> f)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let foldM f s = 
        Seq.fold (fun acc t -> acc >>= (flip f) t) (returnM s)

module ZipList = 
    let returnM v = Seq.initInfinite (fun _ -> v)
    /// Sequential application
    let (<*>) f a = Seq.zip f a |> Seq.map (fun (k,v) -> k v)
    /// Sequential application
    let inline ap m f = f <*> m
    let inline lift2 f a b = returnM f <*> a <*> b
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = lift2 (fun _ z -> z) x y
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = lift2 (fun z _ -> z) x y

module Option =

    /// The maybe monad.
    /// This monad is my own and uses an 'a option. Others generally make their own Maybe<'a> type from Option<'a>.
    /// The builder approach is from Matthew Podwysocki's excellent Creating Extended Builders series http://codebetter.com/blogs/matthew.podwysocki/archive/2010/01/18/much-ado-about-monads-creating-extended-builders.aspx.
    type MaybeBuilder() =
        member this.Return(x) = Some x
        member this.ReturnFrom(m: 'a option) = m
        member this.Bind(m, f) = Option.bind f m
        member this.Zero() = None
        member this.Combine(comp1, comp2) = this.Bind(comp1, fun () -> comp2)
        member this.Delay(f) = this.Bind(this.Return(), f)
        member this.TryWith(m, h) = this.ReturnFrom(m)
        member this.TryFinally(m, compensation) =
            try this.ReturnFrom(m)
            finally compensation()
        member this.Using(res:#IDisposable, body) =
            this.TryFinally(body res, fun () -> match res with null -> () | disp -> disp.Dispose())
        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                this.Bind(m, fun () -> this.While(guard, m))
        member this.For(sequence:seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                                 fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current)))
    let maybe = MaybeBuilder()
    
    open Operators
    
    let inline returnM x = returnM maybe x
    let inline (>>=) m f = bindM maybe m f
    let inline (=<<) f m = bindM maybe m f
    /// Sequential application
    let inline (<*>) f m = applyM maybe maybe f m
    /// Sequential application
    let inline ap m f = f <*> m
    let inline (<!>) f m = Option.map f m
    let inline lift2 f a b = returnM f <*> a <*> b
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = lift2 (fun _ z -> z) x y
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = lift2 (fun z _ -> z) x y

    /// Sequentially compose two maybe actions, discarding any value produced by the first
    let inline (>>.) m f = bindM maybe m (fun _ -> f)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let fromNullable (n: _ Nullable) = 
        if n.HasValue
            then Some n.Value
            else None
    let toNullable =
        function
        | None -> Nullable()
        | Some x -> Nullable(x)

    let inline fromBool b = if b then Some() else None

    let inline fromBoolAndValue b = 
        match b with
        | true,v -> Some v
        | _ -> None

    let fromChoice =
        function
        | Choice1Of2 a -> Some a
        | _ -> None

    let inline getOrElse v =
        function
        | Some x -> x
        | None -> v

    let inline getOrElseLazy (v: _ Lazy) =
        function
        | Some x -> x
        | None -> v.Value

    let inline getOrElseF v =
        function
        | Some x -> x
        | None -> v()

    let getOrDefault =
        function
        | Some x -> x
        | None -> Unchecked.defaultof<_>
        
    let inline orElse v =
        function
        | Some x -> Some x
        | None -> v

    let inline filter pred =
        function
        | Some x when pred x -> Some x
        | _ -> None

    [<CompiledName("Cast")>]
    let inline cast (o: obj) =
        try
            Some (unbox o)
        with _ -> None

    let foldM f s = 
        Seq.fold (fun acc t -> acc >>= (flip f) t) (returnM s)

module Nullable =
    let (|Null|Value|) (x: _ Nullable) =
        if x.HasValue then Value x.Value else Null

    let create x = Nullable x
    let getOrDefault n v = match n with Value x -> x | _ -> v
    let getOrElse (n: 'a Nullable) (v: 'a Lazy) = match n with Value x -> x | _ -> v.Force()
    let get (x: _ Nullable) = x.Value
    let fromOption = Option.toNullable
    let toOption = Option.fromNullable
    let bind f x =
        match x with
        | Null -> Nullable()
        | Value v -> f v
    let hasValue (x: _ Nullable) = x.HasValue
    let isNull (x: _ Nullable) = not x.HasValue
    let count (x: _ Nullable) = if x.HasValue then 1 else 0
    let fold f state x =
        match x with
        | Null -> state
        | Value v -> f state v
    let foldBack f x state =
        match x with
        | Null -> state
        | Value v -> f x state
    let exists p x =
        match x with
        | Null -> false
        | Value v -> p x
    let forall p x = 
        match x with
        | Null -> true
        | Value v -> p x
    let iter f x =
        match x with
        | Null -> ()
        | Value v -> f v
    let map f x =
        match x with
        | Null -> Nullable()
        | Value v -> Nullable(f v)
    let toArray x = 
        match x with
        | Null -> [||]
        | Value v -> [| v |]
    let toList x =
        match x with
        | Null -> []
        | Value v -> [v]
        
    let lift2 f (a: _ Nullable) (b: _ Nullable) =
        if a.HasValue && b.HasValue
            then Nullable(f a.Value b.Value)
            else Nullable()

    let mapBool op a b =
        match a,b with
        | Value x, Value y -> op x y
        | _ -> false

    let inline (+?) a b = (lift2 (+)) a b
    let inline (-?) a b = (lift2 (-)) a b
    let inline ( *?) a b = (lift2 ( *)) a b
    let inline (/?) a b = (lift2 (/)) a b
    let inline (>?) a b = (mapBool (>)) a b
    let inline (>=?) a b = a >? b || a = b
    let inline (<?) a b = (mapBool (<)) a b
    let inline (<=?) a b = a <? b || a = b
    let inline notn (a: bool Nullable) = 
        if a.HasValue 
            then Nullable(not a.Value) 
            else Nullable()
    let inline (&?) a b = 
        let rec and' a b = 
            match a,b with
            | Null, Value y when not y -> Nullable(false)
            | Null, Value y when y -> Nullable()
            | Null, Null -> Nullable()
            | Value x, Value y -> Nullable(x && y)
            | _ -> and' b a
        and' a b

    let inline (|?) a b = notn ((notn a) &? (notn b))

    type Int32 with
        member x.n = Nullable x

    type Double with
        member x.n = Nullable x

    type Single with
        member x.n = Nullable x

    type Byte with
        member x.n = Nullable x

    type Int64 with
        member x.n = Nullable x

    type Decimal with
        member x.n = Nullable x

module State =

    type State<'s,'a> = State of ('s->('a * 's)) with
        static member (?<-) (_      , _Functor:Fmap  ,   State m   ) = fun f -> State(fun s -> let (a, s') = m s in (f a, s'))

    let runState (State x) = x
    type State<'s,'a> with
        static member (?<-) (_      , _Monad  :Return, _:State<_,_>) = fun a -> State(fun s -> (a, s))
        static member (?<-) (State m, _Monad  :Bind  , _:State<_,_>) = fun k -> State(fun s -> let (a, s') = m s in runState(k a) s')

    let mapState  f (State m)  = State(f << m)
    let withState f (State m)  = State(m << f)
    let evalState (State sa) s = fst(sa s)
    let execState (State sa) s = snd(sa s)
    let get   = State (fun s -> (s , s))
    let put x = State (fun _ -> ((), x))

    type StateBuilder() =
        member this.Return(x) : State<'s,'a> = return' x
        member this.Bind(p: State<'s,'a>,rest:'a -> State<'s,'b>) = p >>= rest
        member this.Let (p,rest) = rest p
        member this.ReturnFrom(expr) = expr

        member this.Zero() = this.Return()
        member this.Combine(r1:State<_,_>, r2) = r1 >>= fun () -> r2
        member this.TryWith(m:State<'s,'a>, h:exn -> State<'s,'a>) : State<'s,'a> =
            State(fun env -> try (runState m) env
                             with e -> (runState(h e)) env)

        member this.TryFinally(m:State<'s,'a>, compensation) : State<'s,'a> =
            State(fun env -> try (runState m) env
                             finally compensation())
        member this.Using(res:#IDisposable, body) =
            this.TryFinally(body res, (fun () -> match res with null -> () | disp -> disp.Dispose()))
        member this.Delay(f) = this.Bind(this.Return (), f)
        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                m >>= (fun () -> this.While(guard, m))
        member this.For(sequence:seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                (fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current))))

    let state = new StateBuilder()


    type State_<'a, 's> = 's -> 'a * 's
    
    let getState = fun s -> (s,s)       // remove -> use get
    let putState s = fun _ -> ((),s)    // remove -> use put
    let eval m s = m s |> fst           // remove -> use evalState
    let exec m s = m s |> snd           // remove -> use execState
    let empty = fun s -> ((), s)        // adapt
    let bind k m = fun s -> let (a, s') = m s in (k a) s'   // remove
    
    /// The state monad.
    /// The algorithm is adjusted from my original work off of Brian Beckman's http://channel9.msdn.com/shows/Going+Deep/Brian-Beckman-The-Zen-of-Expressing-State-The-State-Monad/.
    /// The approach was adjusted from Matthew Podwysocki's http://codebetter.com/blogs/matthew.podwysocki/archive/2009/12/30/much-ado-about-monads-state-edition.aspx and mirrors his final result.
    type StateBuilder_() =
        member this.Return(a) : State_<'a,'s> = fun s -> (a,s)
        member this.ReturnFrom(m:State_<'a,'s>) = m
        member this.Bind(m:State_<'a,'s>, k:'a -> State_<'b,'s>) : State_<'b,'s> = bind k m
        member this.Zero() = this.Return ()
        member this.Combine(r1, r2) = this.Bind(r1, fun () -> r2)
        member this.TryWith(m:State_<'a,'s>, h:exn -> State_<'a,'s>) : State_<'a,'s> =
            fun env -> try m env
                       with e -> (h e) env
        member this.TryFinally(m:State_<'a,'s>, compensation) : State_<'a,'s> =
            fun env -> try m env
                       finally compensation()
        member this.Using(res:#IDisposable, body) =
            this.TryFinally(body res, (fun () -> match res with null -> () | disp -> disp.Dispose()))
        member this.Delay(f) = this.Bind(this.Return (), f)
        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                this.Bind(m, (fun () -> this.While(guard, m)))
        member this.For(sequence:seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                (fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current))))

                
    let state_ = new StateBuilder_()
    
    
    open Operators
    
    let inline returnM x = returnM state_ x
    let inline (>>=) m f = bindM state_ m f
    let inline (=<<) f m = bindM state_ m f
    /// Sequential application
    let inline (<*>) f m = applyM state_ state_ f m
    /// Sequential application
    let inline ap m f = f <*> m
    let inline map f m = liftM state_ f m
    let inline (<!>) f m = map f m
    let inline lift2 f a b = returnM f <*> a <*> b
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = lift2 (fun _ z -> z) x y
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = lift2 (fun z _ -> z) x y
    /// Sequentially compose two state actions, discarding any value produced by the first
    let inline (>>.) m f = bindM state_ m (fun _ -> f)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let inline foldM f s = Seq.fold (fun acc t -> acc >>= (flip f) t) (return' s)

module Reader =
    
    type Reader<'r,'a> = Reader of ('r->'a) with
        static member (?<-) (_       , _Functor:Fmap  ,   Reader m   ) = fun f -> Reader(fun r -> f (m r))

    let runReader (Reader x) = x
    type Reader<'s,'a> with
        static member (?<-) (_       , _Monad  :Return, _:Reader<_,_>) = fun a -> Reader(fun _ -> a)
        static member (?<-) (Reader m, _Monad  :Bind  , _:Reader<_,_>) = fun k -> Reader(fun r -> runReader(k (m r)) r)


    let mapReader  f (Reader m) = Reader(f << m)
    let withReader f (Reader m) = Reader(m << f)
    let ask                = Reader id
    let local f (Reader m) = Reader(m << f)

    type ReaderBuilder() =
        member this.Return(x) :Reader<'r,'a> = return' x
        member this.Bind(p:Reader<'r,'a>,rest:'a->Reader<'r,'b>) = p >>= rest
        member this.Let (p,rest) = rest p
        member this.ReturnFrom(expr) = expr

        member this.Zero() = this.Return()
        member this.Combine(r1:Reader<_,_>, r2) = r1 >>= fun () -> r2
        member this.TryWith(m:Reader<'r,'a>, h:exn -> Reader<'r,'a>) : Reader<'r,'a> =
            Reader(fun env -> try (runReader m) env
                              with e -> (runReader(h e)) env)

        member this.TryFinally(m:Reader<'r,'a>, compensation) : Reader<'r,'a> =
            Reader(fun env -> try (runReader m) env
                              finally compensation())
        member this.Using(res:#IDisposable, body) =
            this.TryFinally(body res, (fun () -> match res with null -> () | disp -> disp.Dispose()))
        member this.Delay(f) = this.Bind(this.Return (), f)
        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                m >>= (fun () -> this.While(guard, m))
        member this.For(sequence:seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                (fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current))))

    let reader = new ReaderBuilder()



    type Reader_<'r,'a> = 'r -> 'a

    let bind k m = fun r -> (k (m r)) r
    
    /// The reader monad.
    /// This monad comes from Matthew Podwysocki's http://codebetter.com/blogs/matthew.podwysocki/archive/2010/01/07/much-ado-about-monads-reader-edition.aspx.
    type ReaderBuilder_() =
        member this.Return(a) : Reader_<'r,'a> = fun _ -> a
        member this.ReturnFrom(a:Reader_<'r,'a>) = a
        member this.Bind(m:Reader_<'r,'a>, k:'a -> Reader_<'r,'b>) : Reader_<'r,'b> = bind k m
        member this.Zero() = this.Return ()
        member this.Combine(r1, r2) = this.Bind(r1, fun () -> r2)
        member this.TryWith(m:Reader_<'r,'a>, h:exn -> Reader_<'r,'a>) : Reader_<'r,'a> =
            fun env -> try m env
                       with e -> (h e) env
        member this.TryFinally(m:Reader_<'r,'a>, compensation) : Reader_<'r,'a> =
            fun env -> try m env
                       finally compensation()
        member this.Using(res:#IDisposable, body) =
            this.TryFinally(body res, (fun () -> match res with null -> () | disp -> disp.Dispose()))
        member this.Delay(f) = this.Bind(this.Return (), f)
        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                this.Bind(m, (fun () -> this.While(guard, m)))
        member this.For(sequence:seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                (fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current))))
    let reader_ = new ReaderBuilder_()
    
    let ask_ : Reader_<'r,'r> = id
    let asks f = reader_ {
        let! r = ask_
        return (f r) }
    let local_ (f:'r1 -> 'r2) (m:Reader_<'r2,'a>) : Reader_<'r1, 'a> = f >> m
    
    open Operators
    
    let inline returnM x = returnM reader_ x
    let inline (>>=) m f = bindM reader_ m f
    let inline (=<<) f m = bindM reader_ m f
    /// Sequential application
    let inline (<*>) f m = applyM reader_ reader_ f m
    /// Sequential application
    let inline ap m f = f <*> m
    let inline map f m = liftM reader_ f m
    let inline (<!>) f m = map f m
    let inline lift2 f a b = returnM f <*> a <*> b
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = lift2 (fun _ z -> z) x y
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = lift2 (fun z _ -> z) x y
    /// Sequentially compose two reader actions, discarding any value produced by the first
    let inline (>>.) m f = bindM reader_ m (fun _ -> f)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let foldM f s = 
        Seq.fold (fun acc t -> acc >>= (flip f) t) (returnM s)

module Undo =
    // UndoMonad on top of StateMonad
    open State
    
    let undoable = state_
    
    type 'a History = { 
        Current:'a
        Undos : 'a list
        Redos : 'a list }
    
    let newHistory x = { Current = x; Undos = [x]; Redos = [] }
    let current history = history.Current
    
    let getHistory = getState
    
    let putToHistory x = undoable {
        let! history = getState
        do! putState  { Current = x; 
                        Undos = history.Current :: history.Undos
                        Redos = [] } }

    let exec m s = m s |> snd |> current
    
    let getCurrent<'a> = undoable {
        let! (history:'a History) = getState
        return current history}

    let combineWithCurrent f x = undoable {
        let! currentVal = getCurrent
        do! putToHistory (f currentVal x) }
    
    let undo<'a> = undoable {
        let! (history:'a History) = getState
        match history.Undos with
        | [] -> return false
        | (x::rest) -> 
            do! putState { Current = x;
                           Undos = rest;
                           Redos = history.Current :: history.Redos }
            return true}
    
    let redo<'a> = undoable {
        let! (history:'a History) = getState
        match history.Redos with
        | [] -> return false
        | (x::rest) -> 
            do! putState { Current = x;
                           Undos = history.Current :: history.Undos;
                           Redos = rest }
            return true }

module Writer =
    
    type Writer<'w,'a> = Writer of ('a * 'w) with
        static member        (?<-) (_           , _Functor:Fmap  ,   Writer(a,w)) = fun f -> Writer(f a, w)

    let runWriter (Writer x) = x
    type Writer<'w,'a> with
        static member inline (?<-) (_           , _Monad  :Return, _:Writer<_,_>) = fun a -> Writer(a, mempty())
        static member inline (?<-) (Writer(a, w), _Monad  :Bind  , _:Writer<_,_>) = fun k -> Writer(let (b, w') = runWriter(k a) in (b, mappend w w'))

    let mapWriter f (Writer m)   = Writer(f m)
    let execWriter  (Writer m) s = snd m

    let tell              w       = Writer((),     w)
    let listen(Writer (a, w))     = Writer((a, w), w)
    let pass  (Writer((a, f), w)) = Writer( a,   f w)

    type WriterBuilder() =
        member inline this.Return(x) :Writer<'w,'a> = return' x
        member inline this.Bind(p:Writer<'w,'a>,rest:'a->Writer<'w,'b>) = p >>= rest
        member this.Let (p,rest) = rest p
        member this.ReturnFrom(expr) = expr

        member inline this.Zero() = this.Return()
        member inline this.Combine(r1:Writer<_,_>, r2) = r1 >>= fun () -> r2
        member inline this.Delay(f) = this.Bind(this.Return (), f)
        (* Does it makes sense? There is no underlying function.
        member this.TryWith(m:Writer<'w,'a>, h:exn -> Writer<'w,'a>) : Writer<'w,'a> =
            Writer(fun env -> try (runWriter m) env
                              with e -> (runWriter(h e)) env)

        member this.TryFinally(m:Writer<'w,'a>, compensation) : Writer<'w,'a> =
            Writer(fun env -> try (runWriter m) env
                              finally compensation()) 
        member this.Using(res:#IDisposable, body) =
            this.TryFinally(body res, (fun () -> match res with null -> () | disp -> disp.Dispose()))
        
        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                m >>= (fun () -> this.While(guard, m))
        member this.For(sequence:seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                (fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current)))) *)

    let writer = new WriterBuilder()
    



    open Monoid
        
    type Writer_<'w, 'a> = unit -> 'a * 'w

    let bind (m: _ Monoid_) (k:'a -> Writer_<'w,'b>) (writer:Writer_<'w,'a>) : Writer_<'w,'b> =
        fun () ->
            let (a, w) = writer()
            let (a', w') = (k a)()
            (a', m.mappend w w')

    let returnM (monoid: _ Monoid_) a = 
        fun () -> (a, monoid.mempty)
    
    /// The writer monad.
    /// This monad comes from Matthew Podwysocki's http://codebetter.com/blogs/matthew.podwysocki/archive/2010/02/01/a-kick-in-the-monads-writer-edition.aspx.
    type WriterBuilder_<'w>(monoid: 'w Monoid_) =
        member this.Return(a) : Writer_<'w,'a> = returnM monoid a
        member this.ReturnFrom(w:Writer_<'w,'a>) = w
        member this.Bind(writer, k) = bind monoid k writer
        member this.Zero() = this.Return ()
        member this.TryWith(writer:Writer_<'w,'a>, handler:exn -> Writer_<'w,'a>) : Writer_<'w,'a> =
            fun () -> try writer()
                      with e -> (handler e)()
        member this.TryFinally(writer, compensation) =
            fun () -> try writer()
                      finally compensation()
        member this.Using<'d,'w,'a when 'd :> IDisposable and 'd : null>(resource : 'd, body : 'd -> Writer_<'w,'a>) : Writer_<'w,'a> =
            this.TryFinally(body resource, fun () -> match resource with null -> () | disp -> disp.Dispose())
        member this.Combine(comp1, comp2) = this.Bind(comp1, fun () -> comp2)
        member this.Delay(f) = this.Bind(this.Return (), f)
        member this.While(guard, m) =
            match guard() with
            | true -> this.Bind(m, (fun () -> this.While(guard, m))) 
            | _        -> this.Zero()
        member this.For(sequence:seq<'a>, body:'a -> Writer_<'w,unit>) =
            this.Using(sequence.GetEnumerator(), 
                fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current)))

    let writer_ = WriterBuilder_(Monoid.ListMonoid<string>())

    let tell_   w = fun () -> ((), w)
    let listen_ m = fun () -> let (a, w) = m() in ((a, w), w)
    let pass_   m = fun () -> let ((a, f), w) = m() in (a, f w)
    
    let listens monoid f m = 
        let writer = WriterBuilder_(monoid)
        writer {
            let! (a, b) = m
            return (a, f b) }
    
    let censor monoid (f:'w1 -> 'w2) (m:Writer_<'w1,'a>) : Writer_<'w2,'a> =
        let writer = WriterBuilder_(monoid)
        writer { let! a = m
                 return (a, f)
               } |> pass_

    open Operators
    
    let inline private ret x = returnM writer_ x
    let inline (>>=) m f = bindM writer_ m f
    let inline (=<<) f m = bindM writer_ m f
    /// Sequential application
    let inline (<*>) f m = applyM writer_ writer_ f m
    /// Sequential application
    let inline ap m f = f <*> m
    let inline map f m = liftM writer_ f m
    let inline (<!>) f m = map f m
    let inline lift2 f a b = ret f <*> a <*> b
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = lift2 (fun _ z -> z) x y
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = lift2 (fun z _ -> z) x y
    /// Sequentially compose two state actions, discarding any value produced by the first
    let inline (>>.) m f = bindM writer_ m (fun _ -> f)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let foldM f s = 
        Seq.fold (fun acc t -> acc >>= (flip f) t) (ret s)

module Choice =
    let returnM = Choice1Of2

    let get =
        function
        | Choice1Of2 a -> a
        | Choice2Of2 e -> invalidArg "choice" (sprintf "The choice value was Choice2Of2 '%A'" e)

    let inline protect f x = 
        try
            Choice1Of2 (f x)
        with e -> Choice2Of2 e

    let inline cast (o: obj) = protect unbox o
        
    let ap x f =
        match f,x with
        | Choice1Of2 f, Choice1Of2 x -> Choice1Of2 (f x)
        | Choice2Of2 e, _            -> Choice2Of2 e
        | _           , Choice2Of2 e -> Choice2Of2 e

    /// Sequential application
    let inline (<*>) f x = ap x f

    let map f =
        function
        | Choice1Of2 x -> f x |> Choice1Of2
        | Choice2Of2 x -> Choice2Of2 x

    let inline (<!>) f x = map f x
    let inline lift2 f a b = f <!> a <*> b

    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) a b = lift2 (fun _ z -> z) a b
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) a b = lift2 (fun z _ -> z) a b

    let bind f = 
        function
        | Choice1Of2 x -> f x
        | Choice2Of2 x -> Choice2Of2 x
    
    let inline (>>=) m f = bind f m
    let inline (=<<) f m = bind f m
    /// Sequentially compose two either actions, discarding any value produced by the first
    let inline (>>.) m1 m2 = m1 >>= (fun _ -> m2)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let inline bimap f1 f2 = 
        function
        | Choice1Of2 x -> Choice1Of2 (f1 x)
        | Choice2Of2 x -> Choice2Of2 (f2 x)

    let inline choice f1 f2 = 
        function
        | Choice1Of2 x -> f1 x
        | Choice2Of2 x -> f2 x

    let inline mapSecond f = bimap id f

    type EitherBuilder() =
        member this.Return a = returnM a
        member this.Bind(m,f) = bind f m

    let toOption = Option.fromChoice
    let fromOption o = 
        function
        | Some a -> Choice1Of2 a
        | None -> Choice2Of2 o

    let foldM f s = 
        Seq.fold (fun acc t -> acc >>= (flip f) t) (returnM s)
        // pointfree:
        //Seq.fold (flip f >> bind |> flip) (returnM s)

module Validation =
    open Choice
    open Monoid

    let (|Success|Failure|) = 
        function
        | Choice1Of2 a -> Success a
        | Choice2Of2 e -> Failure e

    /// Sequential application, parameterized by append
    let apa append x f = 
        match f,x with
        | Choice1Of2 f, Choice1Of2 x     -> Choice1Of2 (f x)
        | Choice2Of2 e, Choice1Of2 x     -> Choice2Of2 e
        | Choice1Of2 f, Choice2Of2 e     -> Choice2Of2 e
        | Choice2Of2 e1, Choice2Of2 e2 -> Choice2Of2 (append e1 e2)

    /// Sequential application, parameterized by monoid
    let inline apm (m: _ Monoid_) = apa m.mappend

    type CustomValidation<'a>(monoid: 'a Monoid_) =
        /// Sequential application
        member this.ap x = apm monoid x
        member this.lift2 f a b = returnM f |> this.ap a |> this.ap b
        /// Sequence actions, discarding the value of the first argument.
        member this.apr b a = this.lift2 (fun _ z -> z) a b
        /// Sequence actions, discarding the value of the second argument.
        member this.apl b a = this.lift2 (fun z _ -> z) a b

    let private stringListValidation = CustomValidation(ListMonoid<string>())

    /// Sequential application
    let ap = stringListValidation.ap

    /// Sequential application
    let inline (<*>) f x = ap x f
    let lift2 = stringListValidation.lift2

    /// Sequence actions, discarding the value of the first argument.
    let ( *>) = stringListValidation.apr
    /// Sequence actions, discarding the value of the first argument.
    let ( <*) = stringListValidation.apl

    let seqValidator f = 
        let zero = returnM []
        Seq.map f >> Seq.fold (lift2 (flip FSharpx.List.cons)) zero


module Continuation =

    /// The continuation monad.
    /// The algorithm is from Wes Dyer http://blogs.msdn.com/b/wesdyer/archive/2008/01/11/the-marvels-of-monads.aspx.
    /// The builder approach is from Matthew Podwysocki's excellent Creating Extended Builders series http://codebetter.com/blogs/matthew.podwysocki/archive/2010/01/18/much-ado-about-monads-creating-extended-builders.aspx.
    /// Current implementation from Matt's gist at https://gist.github.com/628956
    type Cont_<'a,'r> = ('a -> 'r) -> (exn -> 'r) -> 'r
    
    let private protect f x cont econt =
        let res = try Choice1Of2 (f x) with err -> Choice2Of2 err
        match res with
        | Choice1Of2 v -> cont v
        | Choice2Of2 v -> econt v
    
    let runCont (c:Cont_<_,_>) cont econt = c cont econt
    let throw exn : Cont_<'a,'r> = fun cont econt -> econt exn
    let callcc (f: ('a -> Cont_<'b,'r>) -> Cont_<'a,'r>) : Cont_<'a,'r> =
        fun cont econt -> runCont (f (fun a -> (fun _ _ -> cont a))) cont econt
    let bind f comp1 = 
        fun cont econt ->
            runCont comp1 (fun a -> protect f a (fun comp2 -> runCont comp2 cont econt) econt) econt     

    type ContinuationBuilder_() =
        member this.Return(a) : Cont_<_,_> = fun cont econt -> cont a
        member this.ReturnFrom(comp:Cont_<_,_>) = comp
        member this.Bind(comp1, f) = bind f comp1
        member this.Catch(comp:Cont_<_,_>) : Cont_<Choice<_, exn>, _> = fun cont econt ->
            runCont comp (fun v -> cont (Choice1Of2 v)) (fun err -> cont (Choice2Of2 err))
        member this.Zero() =
            this.Return ()
        member this.TryWith(tryBlock, catchBlock) =
            this.Bind(this.Catch tryBlock, (function Choice1Of2 v -> this.Return v 
                                                   | Choice2Of2 exn -> catchBlock exn))
        member this.TryFinally(tryBlock, finallyBlock) =
            this.Bind(this.Catch tryBlock, (function Choice1Of2 v -> finallyBlock(); this.Return v 
                                                   | Choice2Of2 exn -> finallyBlock(); throw exn))
        member this.Using(res:#IDisposable, body) =
            this.TryFinally(body res, (fun () -> match res with null -> () | disp -> disp.Dispose()))
        member this.Combine(comp1, comp2) = this.Bind(comp1, (fun () -> comp2))
        member this.Delay(f) = this.Bind(this.Return (), f)
        member this.While(pred, body) =
            if pred() then this.Bind(body, (fun () -> this.While(pred,body))) else this.Return ()
        member this.For(items:seq<_>, body) =
            this.Using(items.GetEnumerator(),
                (fun enum -> this.While((fun () -> enum.MoveNext()), this.Delay(fun () -> body enum.Current))))
    let cont = ContinuationBuilder_()
    
    open Operators
    
    let inline returnM x = returnM cont x
    let inline (>>=) m f = bindM cont m f
    let inline (=<<) f m = bindM cont m f
    /// Sequential application
    let inline (<*>) f m = applyM cont cont f m
    /// Sequential application
    let inline ap m f = f <*> m
    let inline map f m = liftM cont f m
    let inline (<!>) f m = map f m
    let inline lift2 f a b = returnM f <*> a <*> b
    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) x y = lift2 (fun _ z -> z) x y
    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) x y = lift2 (fun z _ -> z) x y
    /// Sequentially compose two continuation actions, discarding any value produced by the first
    let inline (>>.) m f = bindM cont m (fun _ -> f)
    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    let foldM f s = 
        Seq.fold (fun acc t -> acc >>= (flip f) t) (returnM s)

    /// The coroutine type from http://fssnip.net/7M
    type Coroutine() =
        let tasks = new Queue<Cont_<unit,unit>>()

        member this.Put(task) =
            let withYield = cont {
                do! callcc <| fun exit ->
                    task <| fun () ->
                    callcc <| fun c ->
                    tasks.Enqueue(c())
                    exit()
                if tasks.Count <> 0 then
                    do! tasks.Dequeue() }
            tasks.Enqueue(withYield)
            
        member this.Run() =
            runCont (tasks.Dequeue()) ignore raise

module Distribution =
    
    type 'a Outcome = {
        Value: 'a
        Probability : BigRational    }
    
    type 'a Distribution = 'a Outcome seq
    
    // P(A AND B) = P(A | B) * P(B)
    let bind (f: 'a -> 'b Distribution) (dist:'a Distribution) =
        dist 
        |> Seq.map (fun p1 -> 
            f p1.Value
            |> Seq.map (fun p2 -> 
                { Value = p2.Value; 
                    Probability = 
                        p1.Probability * p2.Probability}))
        |> Seq.concat : 'b Distribution
    
    let inline (>>=) dist f = bind f dist
    let inline (=<<) f dist = bind f dist
    
    let returnM (value:'a) =     
        Seq.singleton { Value = value ; Probability = 1N/1N }
            : 'a Distribution
    
    type DistributionMonadBuilder() =
        member this.Bind (r, f) = bind f r
        member this.Return x = returnM x
        member this.ReturnFrom x = x
    
    let distribution = DistributionMonadBuilder()
    
    // Create some helpers
    let toUniformDistribution seq : 'a Distribution =
        let l = Seq.length seq
        seq 
        |> Seq.map (fun e ->
            { Value = e; 
                Probability = 1N / bignum.FromInt l })
    
    let probability (dist:'a Distribution) = 
        dist
        |> Seq.map (fun o -> o.Probability)
        |> Seq.sum
    
    let certainly = returnM
    let impossible<'a> :'a Distribution = toUniformDistribution []
    
    let fairDice sides = toUniformDistribution [1..sides]
    
    type CoinSide = 
        | Heads 
        | Tails
    
    let fairCoin = toUniformDistribution [Heads; Tails]
    
    let filter predicate (dist:'a Distribution) : 'a Distribution =
        dist |> Seq.filter (fun o -> predicate o.Value)
    
    let filterInAnyOrder items dist =
        items
        |> Seq.fold (fun d item -> filter (Seq.exists ((=) (item))) d) dist
    
    let map f (dist:'a Distribution) : 'b Distribution = 
        dist 
        |> Seq.map (fun o -> { Value = f o.Value; Probability = o.Probability })
    
    let selectOne values =
        [for e in values -> e,values |> Seq.filter ((<>) e)] 
        |> toUniformDistribution
    
    let rec selectMany n values =
        match n with 
        | 0 -> certainly ([],values)
        | _ -> 
            distribution {
                let! (x,c1) = selectOne values
                let! (xs,c2) = selectMany (n-1) c1
                return x::xs,c2}
            
    let select n values = 
        selectMany n values     
        |> map (fst >> List.rev)
    
    let remove items = Seq.filter (fun v -> Seq.forall ((<>) v) items)
