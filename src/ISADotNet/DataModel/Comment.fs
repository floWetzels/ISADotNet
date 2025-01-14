namespace ISADotNet

open System.Text.Json.Serialization

type EMail = string

type Comment = 
    {
        [<JsonPropertyName(@"@id")>]
        ID : URI option
        [<JsonPropertyName(@"name")>]
        Name : string option
        [<JsonPropertyName(@"value")>]
        Value : string option
    }

    static member make id name value : Comment =
        {
            ID      = id
            Name    = name
            Value   = value
        }

    static member create(?Id,?Name,?Value) : Comment =
        Comment.make Id Name Value

    static member fromString name value =
        Comment.create (Name=name,Value=value)
    
    static member toString (comment : Comment) =
        Option.defaultValue "" comment.Name, Option.defaultValue "" comment.Value



type Remark = 
    {
        Line : int 
        Value : string
    }
    
    static member make line value  : Remark = 
        {
            Line = line 
            Value = value      
        }

    static member create(line,value) : Remark = 
        Remark.make line value

    static member toTuple (remark : Remark ) =
        remark.Line, remark.Value