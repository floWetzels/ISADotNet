namespace ISADotNet.Json

open ISADotNet
open System.Text.Json
open System.IO

module MaterialAttribute =

    let fromString (s:string) = 
        JsonSerializer.Deserialize<MaterialAttribute>(s,JsonExtensions.options)

    let toString (m:MaterialAttribute) = 
        JsonSerializer.Serialize<MaterialAttribute>(m,JsonExtensions.options)

    let fromFile (path : string) = 
        File.ReadAllText path 
        |> fromString

    let toFile (path : string) (m:MaterialAttribute) = 
        File.WriteAllText(path,toString m)

module MaterialAttributeValue =

    let fromString (s:string) = 
        JsonSerializer.Deserialize<MaterialAttributeValue>(s,JsonExtensions.options)

    let toString (m:MaterialAttributeValue) = 
        JsonSerializer.Serialize<MaterialAttributeValue>(m,JsonExtensions.options)

    let fromFile (path : string) = 
        File.ReadAllText path 
        |> fromString

    let toFile (path : string) (m:MaterialAttributeValue) = 
        File.WriteAllText(path,toString m)