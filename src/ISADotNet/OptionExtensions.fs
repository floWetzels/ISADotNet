﻿namespace ISADotNet

[<RequireQualifiedAccess>]
module Option =
 
    /// If the value matches the default, a None is returned, else a Some is returned
    let fromValueWithDefault d v =
        if d = v then None
        else Some v

    /// Applies the function f on the value of the option if it exists, else applies it on the default value. If the result value matches the default, a None is returned
    let mapDefault (d : 'T) (f: 'T -> 'T) (o : 'T option) =
        match o with
        | Some v -> f v
        | None   -> f d
        |> fromValueWithDefault d

    /// Applies the function f on the value of the option if it exists, else returns the default value. 
    let mapOrDefault (d : 'T Option) (f: 'U -> 'T) (o : 'U option) =
        match o with
        | Some v -> Some (f v)
        | None   -> d
