namespace Walker.Core

open System.Threading

/// A thread-safe reference to an immutable value, changed only by pure functions.
/// A change can run more than once under contention, so it must have no side effects.
[<Sealed>]
type Atom<'State when 'State: not struct>(initial: 'State) =
    let mutable current = initial

    member _.Value = Volatile.Read(&current)

    /// Replaces the state and returns the result computed alongside it.
    member this.Modify(change: 'State -> 'State * 'Result) : 'Result =
        let seen = this.Value
        let next, result = change seen

        if obj.ReferenceEquals(Interlocked.CompareExchange(&current, next, seen), seen) then
            result
        else
            this.Modify change

    member this.Update(change: 'State -> 'State) : unit =
        this.Modify(fun state -> change state, ())
