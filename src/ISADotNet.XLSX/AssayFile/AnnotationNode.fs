﻿namespace ISADotNet.XLSX.AssayFile

open ISADotNet
open ISADotNet.XLSX
open AnnotationColumn


module ProcessInput = 

    let getHeader inp =
        match inp with
        | ProcessInput.Sample s ->      
            "Sample Name"
        | ProcessInput.Source s ->      
            "Source Name"
        | ProcessInput.Data d when d.DataType.IsSome && d.DataType.Value = DataFile.DerivedDataFile ->        
            "Derived Data File Name"
        | ProcessInput.Data d when d.DataType.IsSome && d.DataType.Value = DataFile.ImageFile ->        
            "Image File Name"
        | ProcessInput.Data d when d.DataType.IsSome && d.DataType.Value = DataFile.RawDataFile ->        
            "Raw Data File Name"
        | ProcessInput.Data d ->        
            "Data File Name"
        | ProcessInput.Material m ->  
            "Material Name"

module ProcessOutput = 

    let getHeader outp =
        match outp with
        | ProcessOutput.Sample s ->      
            "Sample Name"
        | ProcessOutput.Data d when d.DataType.IsSome && d.DataType.Value = DataFile.DerivedDataFile ->        
            "Derived Data File Name"
        | ProcessOutput.Data d when d.DataType.IsSome && d.DataType.Value = DataFile.ImageFile ->        
            "Image File Name"
        | ProcessOutput.Data d when d.DataType.IsSome && d.DataType.Value = DataFile.RawDataFile ->        
            "Raw Data File Name"
        | ProcessOutput.Data d ->        
            "Data File Name"
        | ProcessOutput.Material m ->  
            "Material Name"

/// Functions for parsing nodes and node values of an annotation table
///
/// The distinction between columns and nodes is made, as some columns are just used to give additional information for other columns. These columns are grouped together as one node
/// e.g a "Term Source REF" column after a "Parameter" Column adds info to the Parameter Column
///
/// On the other hand, some colums are stand alone nodes, e.g. "Sample Name"
module AnnotationNode = 
    
    type NodeHeader = ColumnHeader seq

    /// Splits the headers of an annotation table into nodes
    ///
    /// The distinction between columns and nodes is made, as some columns are just used to give additional information for other columns. These columns are grouped together as one node
    /// e.g a "Term Source REF" column after a "Parameter" Column adds info to the Parameter Column
    ///
    /// On the other hand, some colums are stand alone nodes, e.g. "Sample Name"
    let splitIntoNodes (headers : seq<string>) =
        headers
        |> Seq.groupWhen false (fun header -> 
            match (AnnotationColumn.ColumnHeader.fromStringHeader header).Kind with
            | "Unit"                    -> false
            | "Term Source REF"         -> false
            | "Term Accession Number"   -> false
            | _ -> true
        )

    /// If the headers of a node depict a unit, returns a function for parsing the values of the matrix to this unit
    let tryGetUnitGetterFunction (headers:string seq) =

        Seq.tryPick tryParseUnitHeader headers
        |> Option.map (fun h -> 
            let unitNameGetter matrix i = 
                Dictionary.tryGetString (i,h.HeaderString) matrix       
            let termAccessionGetter =
                match Seq.tryPick (tryParseTermAccessionNumberHeader h) headers with
                | Some h ->
                    fun matrix i -> 
                        match Dictionary.tryGetString (i,h.HeaderString) matrix with
                        | Some "user-specific" -> None
                        | Some v -> Some v
                        | _ -> None 
                | None -> fun _ _ -> None
            let termSourceGetter =
                match Seq.tryPick (tryParseTermSourceReferenceHeader h) headers with
                | Some h ->
                    fun matrix i -> 
                        match Dictionary.tryGetString (i,h.HeaderString) matrix with
                        | Some "user-specific" -> None
                        | Some v -> Some v
                        | _ -> None 
                | None -> fun _ _ -> None
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                OntologyAnnotation.fromString 
                    (unitNameGetter matrix i |> Option.defaultValue "")
                    (termSourceGetter matrix i |> Option.defaultValue "")
                    (termAccessionGetter matrix i |> Option.defaultValue "")  
        )
    
    /// If the headers of a node depict a value header (parameter,factor,characteristic), returns the category and a function for parsing the values of the matrix to the values
    let tryGetValueGetter (columnOrder : int) hasUnit (valueHeader : ColumnHeader) (headers:string seq) =
        let category1, termAccessionGetter =
            match Seq.tryPick (tryParseTermAccessionNumberHeader valueHeader) headers with
            | Some h ->
                h.Term,
                fun (matrix:System.Collections.Generic.Dictionary<int*string,string>) (i:int) -> 
                    match Dictionary.tryGetString (i,h.HeaderString) matrix with
                    | Some "user-specific" -> None
                    | Some v -> Some v
                    | _ -> None 
            | None -> None, fun _ _ -> None
        let category2, termSourceGetter =
            match Seq.tryPick (tryParseTermSourceReferenceHeader valueHeader) headers with
            | Some h ->
                h.Term,
                fun matrix i -> 
                    match Dictionary.tryGetString (i,h.HeaderString) matrix with
                    | Some "user-specific" -> None
                    | Some v -> Some v
                    | _ -> None 
            | None -> None, fun _ _ -> None
    
        let category =           
            // Merge "Term Source REF" (TSR) and "Term Accession Number" (TAN) from different OntologyAnnotations
            mergeOntology valueHeader.Term category1 |> mergeOntology category2
            |> Option.map (fun oa -> 
                oa.Comments |> Option.defaultValue []
                |> API.CommentList.add (ValueIndex.createOrderComment columnOrder)
                |> API.OntologyAnnotation.setComments oa
            )


        let valueGetter = 
            fun matrix i ->
                let value = 
                    match Dictionary.tryGetString (i,valueHeader.HeaderString) matrix with
                    | Some "user-specific" -> None
                    // Trim() should remove any accidental whitespaces at the beginning or end of a term
                    | Some v -> Some v
                    | _ -> None 

                // Set termAcession and termSource of the value to None if they are the same as the header. 
                // This is done as Swate fills empty with the header but these values should not be transferred to the isa model
                let termAccession,termSource = 
                    if hasUnit then 
                        None, None
                    else
                        match termAccessionGetter matrix i,termSourceGetter matrix i,category with
                        | Some a, Some s,Some c ->
                            match c.TermAccessionNumber,c.TermSourceREF with
                            | Some ca, Some cs when a.Contains ca && s.Contains cs ->
                                None,None
                            | _ -> Some a, Some s
                        | (a,s,c) -> a,s
                Value.fromOptions 
                    value
                    termSource
                    termAccession
        category,valueGetter

    /// If the headers of a node depict a component, returns a function for parsing the values of the matrix to the values of this component
    let tryGetComponentGetter (columnOrder : int) (headers:string seq) =
        Seq.tryPick tryParseComponentHeader headers
        |> Option.map (fun h -> 
            let unitGetter = tryGetUnitGetterFunction headers
                  
            let category,valueGetter = tryGetValueGetter columnOrder unitGetter.IsSome h headers                              
                            
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                Component.fromOptions
                    (valueGetter matrix i)
                    (unitGetter |> Option.map (fun f -> f matrix i))
                    category                   
        )

    /// If the headers of a node depict a protocolType, returns a function for parsing the values of the matrix to the values of this type
    let tryGetProtocolTypeGetter (columnOrder : int) (headers:string seq) =
        Seq.tryPick tryParseProtocolTypeHeader headers
        |> Option.map (fun h -> 

            let order = ValueIndex.createOrderComment columnOrder

            let termAccessionGetter =
                match Seq.tryPick (tryParseTermAccessionNumberHeader h) headers with
                | Some h ->
                    fun matrix i -> 
                        match Dictionary.tryGetString (i,h.HeaderString) matrix with
                        | Some "user-specific" -> None
                        | Some v -> Some v
                        | _ -> None 
                | None -> fun _ _ -> None

            let termSourceGetter =
                match Seq.tryPick (tryParseTermSourceReferenceHeader h) headers with
                | Some h ->
                    fun matrix i -> 
                        match Dictionary.tryGetString (i,h.HeaderString) matrix with
                        | Some "user-specific" -> None
                        | Some v -> Some v
                        | _ -> None 
                | None -> fun _ _ -> None

            fun matrix i ->
                let value = Dictionary.tryGetString (i,h.HeaderString) matrix |> Option.defaultValue ""

                let termAccession = termAccessionGetter matrix i |> Option.defaultValue ""
                let termSource = termSourceGetter matrix i       |> Option.defaultValue ""

                OntologyAnnotation.fromStringWithComments value termSource termAccession [order]
        )

    /// If the headers of a node depict a protocolType, returns a function for parsing the values of the matrix to the values of this type
    let tryGetProtocolREFGetter (columnOrder : int) (headers:string seq) =
        Seq.tryPick tryParseProtocolREFHeader headers
        |> Option.map (fun h -> 

            fun matrix i ->
                Dictionary.tryGetString (i,h.HeaderString) matrix |> Option.defaultValue ""
        )

    /// If the headers of a node depict a parameter, returns the parameter and a function for parsing the values of the matrix to the values of this parameter
    let tryGetParameterGetter (columnOrder : int) (headers:string seq) =
        Seq.tryPick tryParseParameterHeader headers
        |> Option.map (fun h -> 
            let unitGetter = tryGetUnitGetterFunction headers
                  
            let category,valueGetter = tryGetValueGetter columnOrder unitGetter.IsSome h headers                              
                
            let parameter = category |> Option.map (Some >> ProtocolParameter.make None)

            parameter,
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                ProcessParameterValue.make 
                    parameter
                    (valueGetter matrix i)
                    (unitGetter |> Option.map (fun f -> f matrix i))
        )
    
    /// If the headers of a node depict a factor, returns the factor and a function for parsing the values of the matrix to the values of this factor
    let tryGetFactorGetter (columnOrder : int) (headers:string seq) =
        Seq.tryPick tryParseFactorHeader headers
        |> Option.map (fun h -> 
            let unitGetter = tryGetUnitGetterFunction headers
            
            let category,valueGetter = tryGetValueGetter columnOrder unitGetter.IsSome h headers    
                    
            let factor = 
                category
                |> Option.map (fun oa ->  
                    Factor.make None (oa.Name |> Option.map AnnotationValue.toString) (Some oa) None
                )
            
            factor,
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                FactorValue.make 
                    None
                    factor
                    (valueGetter matrix i)
                    (unitGetter |> Option.map (fun f -> f matrix i))
        )

    /// If the headers of a node depict a characteristic, returns the characteristic and a function for parsing the values of the matrix to the values of this characteristic
    let tryGetCharacteristicGetter (columnOrder : int) (headers:string seq) =
        Seq.tryPick tryParseCharacteristicsHeader headers
        |> Option.map (fun h -> 
            let unitGetter = tryGetUnitGetterFunction headers
                  
            let category,valueGetter = tryGetValueGetter columnOrder unitGetter.IsSome h headers    
                    
            let characteristic = category |> Option.map (Some >> MaterialAttribute.make None)            
            
            characteristic,
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                MaterialAttributeValue.make 
                    None
                    characteristic
                    (valueGetter matrix i)
                    (unitGetter |> Option.map (fun f -> f matrix i))
        )

    /// If the headers of a node depict a sample name, returns a function for parsing the values of the matrix to the sample names
    let tryGetDataFileGetter (headers:string seq) =
        Seq.tryPick tryParseDataFileName headers
        |> Option.map (fun h -> 

            let dataType = 
                if h.Kind = "Image File" then Some DataFile.ImageFile
                elif h.Kind = "Raw Data File" then Some DataFile.RawDataFile
                elif h.Kind = "Derived Data File" then Some DataFile.DerivedDataFile 
                else Some DataFile.RawDataFile

            let numberComment = h.Number |> Option.map (string >> (Comment.fromString "Number") >> List.singleton)
            
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                
                Data.make
                    None
                    (Dictionary.tryGetString (i,h.HeaderString) matrix)
                    dataType
                    numberComment
        )

    /// If the headers of a node depict a sample name, returns a function for parsing the values of the matrix to the sample names
    let tryGetSampleNameGetter (headers:string seq) =
        Seq.tryPick tryParseSampleName headers
        |> Option.map (fun h -> 
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                Dictionary.tryGetString (i,h.HeaderString) matrix
        )

    /// If the headers of a node depict a source name, returns a function for parsing the values of the matrix to the source names
    let tryGetSourceNameGetter (headers:string seq) =
        Seq.tryPick tryParseSourceName headers
        |> Option.map (fun h -> 
            fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->
                Dictionary.tryGetString (i,h.HeaderString) matrix
        )
    
    /// Returns true, if the headers contain a value node: characteristic, parameter or factor
    let isValueNode (headers:string seq) =
        (Seq.exists (tryParseFactorHeader >> Option.isSome) headers)
        ||
        (Seq.exists (tryParseCharacteristicsHeader >> Option.isSome) headers)
        ||
        (Seq.exists (tryParseParameterHeader >> Option.isSome) headers)
        ||
        (Seq.exists (tryParseComponentHeader >> Option.isSome) headers)
        ||
        (Seq.exists (tryParseProtocolTypeHeader >> Option.isSome) headers)
        ||
        (Seq.exists (tryParseProtocolREFHeader >> Option.isSome) headers)

module ISAValue =

    open ISADotNet.QueryModel

    let toHeaders (v : QueryModel.ISAValue) =
        try 
            let ont = v.Category.ShortAnnotationString
            if v.HasUnit then
                [v.HeaderText;"Unit";$"Term Source REF ({ont})";$"Term Accession Number ({ont})"]
            else
                [v.HeaderText;$"Term Source REF ({ont})";$"Term Accession Number ({ont})"]
        with
        | err -> failwithf "Could not parse headers of value with name %s: \n%s" v.HeaderText err.Message

    let toValues (v : QueryModel.ISAValue) =    
        try
            if v.HasUnit then
                if v.HasValue then
                    [v.ValueText;v.Unit.NameText;v.Unit.TermSourceREFString;v.Unit.TermAccessionString]
                else 
                    ["";v.Unit.NameText;v.Unit.TermSourceREFString;v.Unit.TermAccessionString]
            else
                match v.TryValue with
                | Some (Ontology oa) ->
                    [oa.NameText;oa.TermSourceREFString;oa.TermAccessionString]
                | Some _ ->
                    [v.ValueText;"";""]
                | None ->
                    ["";"";""]
        with
        | err -> failwithf "Could not parse headers of value with name %s: \n%s" v.HeaderText err.Message

module ProtocolType =

    open ISADotNet.QueryModel

    let headers =
        [
            "Protocol Type"
            "Term Source REF (MS:1000031)"
            "Term Accession Number (MS:1000031)"
        ]

    let toValues (v : OntologyAnnotation) =    
        [
            v.NameText
            v.TermSourceREF |> Option.defaultValue "user-specific"
            v.TermAccessionNumber |> Option.defaultValue "user-specific"
        ]

module IOType =

    let toHeader (io : QueryModel.IOType) =
        match io with
        | QueryModel.IOType.Source ->      
            "Source Name"
        | QueryModel.IOType.Sample ->      
            "Sample Name"
        | QueryModel.IOType.RawData ->      
            "Raw Data File"
        | QueryModel.IOType.ProcessedData ->      
            "Derived Data File"
        | QueryModel.IOType.Material ->      
            "Material Name"
        | QueryModel.IOType.Data ->      
            "Data File Name"

    let defaultInHeader = toHeader QueryModel.IOType.Source

    let defaultOutHeader = toHeader QueryModel.IOType.Sample