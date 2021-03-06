// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2014 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

namespace IntelliFactory.Reactive

open WebSharper

type private IDisposable = System.IDisposable
type private IObservable<'T> = System.IObservable<'T>
type private IObserver<'T> = System.IObserver<'T>
type private IO<'T> = IObservable<'T>

/// Interface specifying necessary functionality over IObservable used by the
/// formlet library.
type IReactive =
    [<Name "Return">]
    abstract member Return<'T> : 'T -> IO<'T>
    [<Name "Never">]
    abstract member Never<'T> : unit -> IO<'T>
    [<Name "Select">]
    abstract member Select : IO<'T>  -> ('T -> 'U) -> IO<'U>
    [<Name "Concat">]
    abstract member Concat : IO<'T> -> IO<'T> -> IO<'T>
    [<Name "Merge">]
    abstract member Merge : IO<'T> -> IO<'T> -> IO<'T>
    [<Name "Switch">]
    abstract member Switch : IO<IO<'T>> -> IO<'T>
    [<Name "SelectMany">]
    abstract member SelectMany : IO<IO<'T>> -> IO<'T>
    [<Name "CollectLatest">]
    abstract member CollectLatest : IO<IO<'T>> -> IO<seq<'T>>
    [<Name "CombineLatest">]
    abstract member CombineLatest : IO<'T> -> IO<'U> -> ('T -> 'U -> 'S) -> IO<'S>
    [<Name "Heat">]
    abstract member Heat : IO<'T> -> IO<'T>
    [<Name "Aggregate">]
    abstract member Aggregate : IO<'T> -> 'S -> ('S -> 'T -> 'S) -> IO<'S>
    [<Name "Choose">]
    abstract member Choose : IO<'T> -> ('T -> option<'U>) -> IO<'U>
    [<Name "Where">]
    abstract member Where : IO<'T> -> ('T -> bool) -> IO<'T>
    [<Name "Drop">]
    abstract member Drop : IO<'T> -> int -> IO<'T>
    [<Name "Sequence">]
    abstract member Sequence : seq<IO<'T>> -> IO<seq<'T>>

#if ZAFIR
type private Disposable =
    [<JavaScript>]
    static member New d =
        { new IDisposable with 
            member this.Dispose() = d () 
        }

type private Observer<'T> =
    [<JavaScript>]
    static member New onNext onComplete =
        { new IObserver<'T> with
            member this.OnNext t = onNext t
            member this.OnCompleted() = onComplete()
            member this.OnError err = ()
        }

type private Observable<'T> =
    [<JavaScript; Inline>]
    static member New f =
        { new IObservable<'T> with
            member this.Subscribe o = f o
        }

#else
/// Implementation of IDisposable
type private Disposable =
    {
        Dispose : unit -> unit
    }

    interface IDisposable with

        [<JavaScript>]
        member this.Dispose () =
            this.Dispose ()

    [<JavaScript>]
    static member New d =
        {Dispose = d} :> IDisposable

/// Implementation of IObserver
type private Observer<'T> =
    {
        OnNext : 'T -> unit
        OnCompleted : unit -> unit
    }

    interface IObserver<'T> with

        [<JavaScript>]
        member this.OnNext t =
            this.OnNext t

        [<JavaScript>]
        member this.OnCompleted() =
            this.OnCompleted()

        [<JavaScript>]
        member this.OnError err =
            ()

    [<JavaScript>]
    static member New onNext onComplete =
        {
            OnNext = onNext
            OnCompleted = onComplete
        } :> IObserver<'T>

/// Implementation of IObserverable
type private Observable<'T> =
    {
        OnSubscribe : IObserver<'T> -> IDisposable
    }

    interface IObservable<'T> with

        [<JavaScript>]
        member this.Subscribe o =
            this.OnSubscribe o

    [<JavaScript>]
    [<Name "SubscribeWith">]
    member this.Subscribe (onNext: 'T -> unit) (onComplete: unit -> unit) =
        Observer.New onNext onComplete
        |> this.OnSubscribe

    [<JavaScript>]
    static member New f =
        {OnSubscribe = f} :> IObservable<'T>
#endif

/// Implementation for "hot stream".
/// Subscribers to hot streams will only observe the latest
/// and future values.
[<JavaScript>]
type HotStream<'T> =
    {
        Latest : ref<option<'T>>
        Event : Event<'T>
    }

    interface IObservable<'T> with
        member this.Subscribe o =
            if this.Latest.Value.IsSome then
                o.OnNext this.Latest.Value.Value
            this.Event.Publish.Subscribe o

    member this.Trigger v =
        this.Latest := Some v
        this.Event.Trigger v

    static member New() : HotStream<'T> =
        {
            Latest = ref None
            Event = Event<_>()
        }

    static member New<'T>(x : 'T) =
        {
            Latest = ref <| Some x
            Event = new Event<'T>()
        }

[<JavaScript>]
module Reactive =

    let Return<'T> (x: 'T) : IObservable<'T> =
        let f (o : IObserver<'T>) =
            o.OnNext x
            o.OnCompleted ()
            Disposable.New ignore
        Observable.New f

    let Never<'T>() : IObservable<'T> =
        Observable.New(fun _ -> Disposable.New ignore)

    let Select (io: IObservable<'T>) (f: 'T -> 'U) : IO<'U> =
        Observable.New (fun o1 ->
            io.Subscribe(fun v -> o1.OnNext (f v)))

    let Where(io: IObservable<'T>) (f: 'T -> bool) : IObservable<'T> =
        Observable.New <| fun o1 ->
            io.Subscribe(fun v ->
                if f v then
                    o1.OnNext v)

    let Choose (io: IObservable<'T>) (f: 'T -> option<'U>) : IO<'U> =
        Observable.New <| fun o1 ->
            io.Subscribe(fun v ->
                match f v with
                | Some v -> o1.OnNext v
                | None -> ())

    let Drop (io: IObservable<'T>) (count: int) : IObservable<'T> =
        Observable.New <| fun o1 ->
            let index = ref 0
            io.Subscribe(fun v ->
                incr index
                if index.Value > count then
                    o1.OnNext v)

    let Merge (io1: IObservable<'T>) (io2: IObservable<'T>) : IObservable<'T> =
        Observable.New <| fun o ->
            let completed1 = ref false
            let completed2 = ref false
            let disp1 =
                Observer.New o.OnNext <| fun () ->
                    completed1 := true
                    if completed1.Value && completed2.Value then
                        o.OnCompleted ()
                |> io1.Subscribe
            let disp2 =
                Observer.New o.OnNext <| fun () ->
                    completed2 := true
                    if completed1.Value && completed2.Value then
                        o.OnCompleted ()
                |> io2.Subscribe
            Disposable.New (fun () -> disp1.Dispose(); disp2.Dispose())

    let Concat (io1: IObservable<'T>) (io2: IObservable<'T>) =
        Observable.New <| fun o ->
            let innerDisp = ref None
            let outerDisp =
                Observer.New o.OnNext <| fun () ->
                    innerDisp := Some (io2.Subscribe o)
                |> io1.Subscribe
            Disposable.New(fun () ->
                if innerDisp.Value.IsSome then
                    innerDisp.Value.Value.Dispose ()
                outerDisp.Dispose())

    let Range (start: int) (count: int) : IObservable<int> =
        Observable.New <| fun o ->
            for i = start to start + count do
                o.OnNext i
            Disposable.New ignore

    let CombineLatest (io1: IObservable<'T>) (io2: IO<'U>)
        (f: 'T -> 'U -> 'S) : IObservable<'S> =
        Observable.New <| fun o ->
            let lv1 = ref None
            let lv2 = ref None
            let update () =
                match !lv1, !lv2 with
                | Some v1, Some v2 -> o.OnNext (f v1 v2)
                | _ -> ()
            let o1 =
                let onNext x =
                    lv1 := Some x
                    update ()
                Observer.New onNext ignore
            let o2 =
                let onNext y =
                    lv2 := Some y
                    update ()
                Observer.New onNext ignore
            let d1 = io1.Subscribe o1
            let d2 = io2.Subscribe o2
            Disposable.New (fun () -> d1.Dispose() ; d2.Dispose())

    let CombineLast (io1: IObservable<'T>) (io2: IO<'U>)
        (f: 'T -> 'U -> 'S) : IObservable<'S> =
        Observable.New <| fun o ->
            let lv1s = System.Collections.Generic.Queue<'T>()
            let lv2s = System.Collections.Generic.Queue<'U>()
            let update () =
                if lv1s.Count > 0 && lv2s.Count > 0 then
                    let v1 = lv1s.Dequeue()
                    let v2 = lv2s.Dequeue()
                    o.OnNext (f v1 v2)
            let o1 =
                let onNext x =
                    lv1s.Enqueue(x)
                    update ()
                Observer.New onNext ignore
            let o2 =
                let onNext y =
                    lv2s.Enqueue(y)
                    update ()
                Observer.New onNext ignore
            let d1 = io1.Subscribe o1
            let d2 = io2.Subscribe o2
            Disposable.New (fun () -> d1.Dispose() ; d2.Dispose())

    let Switch (io: IObservable<IObservable<'T>>) : IObservable<'T> =
        Observable.New (fun o ->
            let disp =
                let index = ref 0
                let disp : option<IDisposable> ref = ref None
                // Subscribe to outer stream
                io.Subscribe(fun (o1 : IObservable<'T>) ->
                    // Update counter
                    incr index
                    // Dispose old observer
                    if disp.Value.IsSome then
                        disp.Value.Value.Dispose ()
                    let currentIndex = index.Value
                    let d =
                        o1.Subscribe (fun v ->
                            if currentIndex = index.Value then
                                o.OnNext(v)
                        ) |> Some
                    // Update dispose
                    disp := d)
            disp)

    let SelectMany (io: IObservable<IObservable<'T>>) : IObservable<'T> =
        Observable.New (fun o ->
            let disp = ref ignore
            // Subscribe to outer stream
            let d =
                io.Subscribe(fun (o1 : IObservable<'T>) ->
                    // Subscribe to inner stream
                    let d = o1.Subscribe o.OnNext
                    disp := fun () ->
                        disp.Value ()
                        d.Dispose ())
            Disposable.New (fun () ->
                disp.Value ()
                d.Dispose ()))

    let Aggregate (io: IObservable<'T>) (seed: 'S) (acc: 'S -> 'T -> 'S) =
        Observable.New (fun o ->
            let state = ref seed
            io.Subscribe(fun value ->
                state := acc state.Value value
                o.OnNext(state.Value)))

    let CollectLatest (outer: IObservable<IObservable<'T>>)
        : IObservable<seq<'T>> =
        Observable.New (fun o ->
            let dict = System.Collections.Generic.Dictionary<int, 'T>()
            let index = ref 0
            // Outer subscription
            outer.Subscribe(fun (inner: IObservable<'T>) ->
                incr index
                let currentIndex = index.Value
                // Inner subscription
                inner.Subscribe(fun value ->
                    dict.[currentIndex] <- value
                    seq {for pair in dict do yield pair.Value}
                    |> o.OnNext)
                |> ignore))

    let Sequence (ios: seq<IObservable<'T>>) : IObservable<seq<'T>> =
        let rec sequence (ios: list<IObservable<'T>>) =
            match ios with
            | [] -> Return []
            | x :: xs ->
                let rest = sequence xs
                CombineLatest x rest (fun x y -> x :: y)
        Select (sequence (List.ofSeq ios)) Seq.ofList

    let Heat (io: IObservable<'T>) : IObservable<'T> =
        let s = HotStream<_>.New()
        let disp = io.Subscribe s.Trigger
        s :> IObservable<'T>

    /// Implementation of IReactive
    type private Reactive[<JavaScript>]() =

        interface IReactive with

            member this.Return x =
                Return x

            member this.Never () =
                Never ()

            member this.Select io f =
                Select io f

            member this.Choose io f =
                Choose io f

            member this.Where io f =
                Where io f

            member this.Concat io1 io2 =
                Concat io1 io2

            member this.Merge io1 io2 =
                Merge io1 io2

            member this.Switch io =
                Switch io

            member this.SelectMany io =
                SelectMany io

            member this.CollectLatest io =
                CollectLatest io

            member this.CombineLatest io1 io2 f =
                CombineLatest io1 io2 f

            member this.Heat io =
                Heat io

            member this.Aggregate io s a =
                Aggregate io s a

            member this.Drop io count =
                Drop io count

            member this.Sequence ios =
                Sequence ios

    /// IReactive object.
    let Default = Reactive() :> IReactive
