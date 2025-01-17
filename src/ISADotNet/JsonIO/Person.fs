namespace ISADotNet.Json

open ISADotNet
open System.Text.Json
open System.IO

module Person = 

    let fromString (s:string) = 
        JsonSerializer.Deserialize<Person>(s,JsonExtensions.options)

    let toString (p:Person) = 
        JsonSerializer.Serialize<Person>(p,JsonExtensions.options)

    let fromFile (path : string) = 
        File.ReadAllText path 
        |> fromString

    let toFile (path : string) (p:Person) = 
        File.WriteAllText(path,toString p)