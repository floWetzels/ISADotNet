namespace ISADotNet

open System.Text.Json.Serialization

type Publication = 
    {
        [<JsonPropertyName(@"@id")>]
        ID : URI option
        [<JsonPropertyName(@"pubMedID")>]
        PubMedID : URI option
        [<JsonPropertyName(@"doi")>]
        DOI : string option
        [<JsonPropertyName(@"authorList")>]
        Authors : string option
        [<JsonPropertyName(@"title")>]
        Title : string option
        [<JsonPropertyName(@"status")>]
        Status : OntologyAnnotation option
        [<JsonPropertyName(@"comments")>]
        Comments : Comment list option
    }

    static member make id pubMedID doi authors title status comments =
        {
            ID          = id
            PubMedID    = pubMedID
            DOI         = doi
            Authors     = authors
            Title       = title
            Status      = status
            Comments    = comments
        }

    static member create (?Id,?PubMedID,?Doi,?Authors,?Title,?Status,?Comments) : Publication =
       Publication.make Id PubMedID Doi Authors Title Status Comments

    static member empty =
        Publication.create()

