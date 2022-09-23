﻿namespace ISADotNet.XLSX.AssayFile

open ISADotNet.XLSX
open ISADotNet

/// Functions for parsing an annotation table to the described processes
module AnnotationTable = 

    open ISADotNet.QueryModel

    /// Returns the protocol described by the headers and a function for parsing the values of the matrix to the processes of this protocol
    let getProcessGetter (processNameRoot : string) (nodes : seq<seq<string>>) =
    
        let valueNodes =
            nodes
            |> Seq.filter (AnnotationNode.isValueNode)
            |> Seq.indexed

        let characteristics,characteristicValueGetters =
            valueNodes |> Seq.choose (fun (i,n) -> AnnotationNode.tryGetCharacteristicGetter i n)
            |> Seq.fold (fun (cl,cvl) (c,cv) -> c.Value :: cl, cv :: cvl) ([],[])
            |> fun (l1,l2) -> List.rev l1, List.rev l2
        let factors,factorValueGetters =
            valueNodes |> Seq.choose (fun (i,n) -> AnnotationNode.tryGetFactorGetter i n)
            |> Seq.fold (fun (fl,fvl) (f,fv) -> f.Value :: fl, fv :: fvl) ([],[])
            |> fun (l1,l2) -> List.rev l1, List.rev l2
        let parameters,parameterValueGetters =
            valueNodes |> Seq.choose (fun (i,n) -> AnnotationNode.tryGetParameterGetter i n)
            |> Seq.fold (fun (pl,pvl) (p,pv) -> p.Value :: pl, pv :: pvl) ([],[])
            |> fun (l1,l2) -> List.rev l1, List.rev l2
    
        let componentGetters =
            valueNodes 
            |> Seq.choose (fun (i,n) -> AnnotationNode.tryGetComponentGetter i n)
            |> Seq.toList

        let protocolTypeGetter = 
            valueNodes 
            |> Seq.tryPick (fun (i,n) -> AnnotationNode.tryGetProtocolTypeGetter i n)

        let protocolREFGetter = 
            valueNodes 
            |> Seq.tryPick (fun (i,n) -> AnnotationNode.tryGetProtocolREFGetter i n)

        let dataFileGetter = nodes |> Seq.tryPick AnnotationNode.tryGetDataFileGetter

        let inputGetter,outputGetter =
            match nodes |> Seq.tryPick AnnotationNode.tryGetSourceNameGetter with
            | Some inputNameGetter ->
                let outputNameGetter = nodes |> Seq.tryPick AnnotationNode.tryGetSampleNameGetter
                let inputGetter = 
                    fun matrix i -> 
                        let source = 
                            Source.make
                                None
                                (inputNameGetter matrix i)
                                (characteristicValueGetters |> List.map (fun f -> f matrix i) |> Option.fromValueWithDefault [])
                        if dataFileGetter.IsSome then 
                            [source;source]
                        else 
                            [source]
                
                let outputGetter =
                    fun matrix i -> 
                        let data = dataFileGetter |> Option.map (fun f -> f matrix i)
                        let outputName = 
                            match outputNameGetter |> Option.bind (fun o -> o matrix i) with
                            | Some s -> Some s
                            | None -> 
                                match data with
                                | Some data -> data.Name
                                | None -> None
                        let sample =
                            Sample.make
                                None
                                outputName
                                None
                                (factorValueGetters |> List.map (fun f -> f matrix i) |> Option.fromValueWithDefault [])
                                (inputGetter matrix i |> List.distinct |> Some)
                        if data.IsSome then 
                            [ProcessOutput.Sample sample; ProcessOutput.Data data.Value]
                        else 
                            [ProcessOutput.Sample sample]                      
                (fun matrix i -> inputGetter matrix i |> List.map ProcessInput.Source |> Some),outputGetter
            | None ->
                let inputNameGetter = nodes |> Seq.head |> AnnotationNode.tryGetSampleNameGetter
                let outputNameGetter = nodes |> Seq.last |> AnnotationNode.tryGetSampleNameGetter
                let inputGetter = 

                    fun matrix i ->      
                        let source = 
                            inputNameGetter
                            |> Option.map (fun ing ->
                                Sample.make
                                    None
                                    (ing matrix i)
                                    (characteristicValueGetters |> List.map (fun f -> f matrix i) |> Option.fromValueWithDefault [])
                                    None
                                    None
                                |> ProcessInput.Sample
                            )   
                        match source with
                        | Some source when dataFileGetter.IsSome -> Some [source;source]
                        | Some source -> Some  [source]
                        | None -> None
                            

                let outputGetter =
                    fun matrix i -> 
                        let data = dataFileGetter |> Option.map (fun f -> f matrix i)
                        let outputName = 
                            match outputNameGetter |> Option.bind (fun o -> o matrix i) with
                            | Some s -> Some s
                            | None -> 
                                match data with
                                | Some data -> data.Name
                                | None -> None
                        let sample =
                            Sample.make
                                None
                                outputName
                                None
                                (factorValueGetters |> List.map (fun f -> f matrix i) |> Option.fromValueWithDefault [])
                                None
                        if data.IsSome then 
                            [ProcessOutput.Sample sample; ProcessOutput.Data data.Value]
                        else 
                            [ProcessOutput.Sample sample]  
                inputGetter,outputGetter
    
        
        
        fun (matrix : System.Collections.Generic.Dictionary<(int * string),string>) i ->

            let pn = processNameRoot |> Option.fromValueWithDefault ""

            let protocol : Protocol = 
                Protocol.make 
                    None
                    (protocolREFGetter |> Option.mapOrDefault pn (fun f -> f matrix i))
                    (protocolTypeGetter |> Option.map (fun f -> f matrix i))
                    None
                    None
                    None
                    (Option.fromValueWithDefault [] parameters)
                    (componentGetters |> List.map (fun f -> f matrix i) |> Option.fromValueWithDefault [])
                    None
                |> fun p -> p.SetRowIndex i

            Process.make 
                None 
                pn 
                (Some protocol) 
                (parameterValueGetters |> List.map (fun f -> f matrix i) |> Option.fromValueWithDefault [])
                None
                None
                None
                None          
                (inputGetter matrix i)
                (outputGetter matrix i |> Some)
                None

    /// Merges processes with the same parameter values, grouping the input and output files
    let mergeIdenticalProcesses processNameRoot (processes : seq<Process>) =
        let protocols = 
            processes 
            |> Seq.groupBy (fun p -> p.ExecutesProtocol.Value.Name.Value)
            |> Seq.map (fun (n,ps) -> 
                let protocols = ps |> Seq.map (fun p -> p.ExecutesProtocol.Value)
                protocols
                |> Seq.map (fun p -> p.ProtocolType,p.Components)
                |> Seq.reduce (fun (pt,c) (pt',c') -> 
                    if pt <> pt' then failwithf "For the protocol with the name %s, two different protocol Types %O and %O were given, which is not allowed" n pt pt'
                    if c <> c' then failwithf "For the protocol with the name %s, two different component lists %O and %O were given, which is not allowed" n c c'
                    pt,c
                ) |> ignore
                n,protocols |> Seq.toList |> Protocol.mergeIndicesToRange
            )
            |> Map.ofSeq
        processes
        |> Seq.groupBy (fun p -> p.ExecutesProtocol.Value.Name.Value, p.ParameterValues)
        |> Seq.mapi (fun i ((name,_),processGroup) ->
            processGroup
            |> Seq.reduce (fun p1 p2 ->
                let mergedInputs = List.append (p1.Inputs |> Option.defaultValue []) (p2.Inputs |> Option.defaultValue []) |> Option.fromValueWithDefault []
                let mergedOutputs = List.append (p1.Outputs |> Option.defaultValue []) (p2.Outputs |> Option.defaultValue []) |> Option.fromValueWithDefault []
                {p1 with Inputs = mergedInputs; Outputs = mergedOutputs}
            )
            |> fun pr -> {pr with ExecutesProtocol = Some (protocols.[name]); Name = Some (Process.composeName processNameRoot i)}
        )

    /// Create a sample from a source
    let sampleOfSource (s:Source) =
        Sample.make s.ID s.Name s.Characteristics None None

    /// Create a sample from a source
    let sourceOfSample (s:Sample) =
        Source.make s.ID s.Name s.Characteristics

    /// Updates the sample information in the given processes with the information of the samples in the given referenceProcesses.
    ///
    /// If the processes contain a source with the same name as a sample in the referenceProcesses. Additionally transforms it to a sample
    let private updateSamplesBy (referenceProcesses : Process seq) (processes : Process seq) = 
        let samples = 
            referenceProcesses
            |> Seq.collect (fun p -> 
                let inputs =
                    p.Inputs 
                    |> Option.defaultValue [] 
                    |> Seq.choose (function | ProcessInput.Sample x -> Some(x.Name,true, x) | ProcessInput.Source x -> Some (x.Name,false,sampleOfSource x)| _ -> None)
                let outputs =
                    p.Outputs 
                    |> Option.defaultValue [] 
                    |> Seq.choose (function | ProcessOutput.Sample x -> Some(x.Name,true, x) | _ -> None)
                Seq.append inputs outputs
                |> Seq.distinct
                )
            |> Seq.filter (fun (name,_,samples) -> name <> None && name <> (Some ""))
            |> Seq.groupBy (fun (name,_,samples) -> name)
            |> Seq.map (fun (name,samples) -> 
                let aggregatedSample = 
                    samples 
                    |> Seq.map (fun (name,_,s) -> s) 
                    |> Seq.reduce (fun s1 s2 -> if s1 = s2 then s1 else API.Update.UpdateByExistingAppendLists.updateRecordType s1 s2)
                if Seq.exists (fun (name,isSample,s) -> isSample) samples then
                    name, ProcessInput.Sample aggregatedSample
                else name, ProcessInput.Source (sourceOfSample aggregatedSample)          
            )
            |> Map.ofSeq
    
        let updateInput (i:ProcessInput) =
            match i with 
            | ProcessInput.Source x ->      match Map.tryFind x.Name samples with   | Some s -> s | None -> ProcessInput.Source x
            | ProcessInput.Sample x ->      match Map.tryFind x.Name samples with   | Some s -> s | None -> ProcessInput.Sample x
            | ProcessInput.Data x ->        ProcessInput.Data x
            | ProcessInput.Material x ->    ProcessInput.Material x
        let updateOutput (o:ProcessOutput) =                         
            match o with                                             
            | ProcessOutput.Sample x ->     match Map.tryFind x.Name samples with   | Some (ProcessInput.Sample x) -> ProcessOutput.Sample x | _ -> ProcessOutput.Sample x
            | ProcessOutput.Data x ->       ProcessOutput.Data x
            | ProcessOutput.Material x ->   ProcessOutput.Material x
        processes
        |> Seq.map (fun p -> 
           {p with
                Inputs = p.Inputs |> Option.map (List.map updateInput)
                Outputs = p.Outputs |> Option.map (List.map updateOutput)
           }
        )

    /// Updates the sample information in the given processes with the information of the samples in the given referenceProcesses.
    ///
    /// If the processes contain a source with the same name as a sample in the referenceProcesses. Additionally transforms it to a sample
    let updateSamplesByReference (referenceProcesses : Process seq) (processes : Process seq) = 
        processes
        |> updateSamplesBy referenceProcesses

    /// Updates the sample information in the given processes with the information of the samples in the given referenceProcesses.
    ///
    /// If the processes contain a source with the same name as a sample in the referenceProcesses. Additionally transforms it to a sample
    let updateSamplesByThemselves (processes : Process seq) =
        processes
        |> updateSamplesBy processes


module QRow =
    open FsSpreadsheet.DSL
    open ISADotNet.QueryModel

    let renumberHeaders (headers : string list) = 
        
        let counts = System.Collections.Generic.Dictionary<string, int ref>()
        let renumberHeader num (h : string) = 
            counts.[h] <- ref num
            if h = "Unit" then
                $"Unit (#{num})"
            elif h.EndsWith ")" then
                h.Replace(")",$"#{num})")
            elif h.EndsWith "]" then
                h.Replace("]",$"#{num}]")
            else h
        headers
        |> List.map (fun header ->

            match Dictionary.tryGetValue header counts with
            | Some count -> 
                count := !count + 1 
                renumberHeader !count header
            | _ -> 
                counts.[header] <- ref 1
                header
        )

    let toHeaderRow (hasProtocolREF : bool) (hasProtocolType : bool) (rows : QueryModel.QRow list) =
        try 

            let outputType = 
                rows
                |> List.fold (fun outputType r -> 
                    match outputType,r.OutputType with
                    | Some t1, Some t2 when t1 = t2 -> Some t1
                    | Some t1, Some t2 -> failwithf "OutputTypes %A and %A do not match" t1 t2
                    | None, t2 -> t2
                    | t1, None -> t1
                    | _ -> None
                ) None
                |> Option.map IOType.toHeader
                |> Option.defaultValue IOType.defaultOutHeader 

            let valueHeaders,valueMappers = 
                rows
                |> List.collect (fun r -> r.Vals)
                |> List.groupBy (fun v -> v.HeaderText)
                |> List.sortBy (fun (h,vs) -> vs |> List.choose (fun v -> v.TryValueIndex()) |> List.append [System.Int32.MaxValue] |> List.min)
                |> List.map (fun (h,vs) -> 
                    let v = 
                        vs
                        |> List.reduce (fun v1 v2 ->
                            match v1.TryUnit,v2.TryUnit with
                            | Some u1, Some u2 when u1 = u2 -> v1
                            | None, None -> v1
                            | Some u1, Some u2 -> failwithf "Units %s and %s of value with header %s do not match" u1.NameText u2.NameText h
                            | Some u1, None -> failwithf "Units %s and None of value with header %s do not match" u1.NameText h
                            | None, Some u2 -> failwithf "Units None and %s of value with header %s do not match" u2.NameText h
                        )
                    let h = ISAValue.toHeaders v
                    let f (vs : ISAValue list) = 
                        vs 
                        |> List.tryPick (fun v' -> if v'.HeaderText = v.HeaderText then Some (ISAValue.toValues v') else None)
                        |> Option.defaultValue (List.init h.Length (fun _ -> ""))
                    h,f
                )
                |> List.unzip
        
            row {
                IOType.defaultInHeader
                if hasProtocolREF then "Protocol REF"
                if hasProtocolType then 
                    "Protocol Type"
                    "Term Source REF (MS:1000031)"
                    "Term Accession Number (MS:1000031)"
                for v in (valueHeaders |> List.concat |> renumberHeaders ) do v
                outputType
            }
            ,valueMappers
        with
        | err -> failwithf "Could not parse headers of row: \n%s" err.Message

    let toValueRow i hasRef hasProtocolType (protocolRef : string option) (protocolType : OntologyAnnotation option) (valueMappers : (ISAValue list -> string list) list) (r : QueryModel.QRow) =
        let protocolVals =
            [
                if hasRef then protocolRef |> Option.defaultValue ""
                if hasProtocolType then yield! protocolType |> Option.map ProtocolType.toValues |> Option.defaultValue ["";"";""]
            ]
        try
            row {
                r.Input
                for v in protocolVals do v
                for v in valueMappers |> List.collect (fun f -> f r.Vals) do v
                r.Output
            }
        with
        | err -> failwithf "Could not parse values of row %i: \n%s" (i+1) err.Message

module QSheet =

    open FsSpreadsheet.DSL
    open ISADotNet.QueryModel

    let toSheet i (s : QueryModel.QSheet) =
        let hasRef,refs = 
            if s.Protocols |> List.exists (fun p -> p.Name.IsSome && p.Name.Value <> s.SheetName) then
                if s.Protocols.Length = 1 then
                    true, ForAll s.Protocols.Head.Name.Value
                else
                    true, 
                    s.Protocols 
                    |> List.collect (fun p -> 
                        let f,t = p.GetRowRange()
                        List.init (t-f+1) (fun i -> i + f, p.Name.Value)
                    )
                    |> Map.ofList
                    |> ForSpecific
            else 
                false, ForSpecific Map.empty
        let hasProtocolType,protcolTypes = 
            if s.Protocols |> List.exists (fun p -> p.ProtocolType.IsSome) then
                if s.Protocols.Length = 1 then
                    true, ForAll s.Protocols.Head.ProtocolType.Value
                else
                    true, 
                    s.Protocols 
                    |> List.collect (fun p -> 
                        let f,t = p.GetRowRange()
                        List.init (t-f+1) (fun i -> i + f, p.ProtocolType.Value)
                    )
                    |> Map.ofList
                    |> ForSpecific
            else 
                false, ForSpecific Map.empty
        try 
            let headers,mappers = QRow.toHeaderRow hasRef hasProtocolType s.Rows
            sheet s.SheetName {
                table $"annotationTable{i}" {
                    headers
                    for (i,r) in Seq.indexed s do QRow.toValueRow i hasRef hasProtocolType (refs.TryGet(i)) (protcolTypes.TryGet(i)) mappers r
                }
            }
        with
        | err -> failwithf "Could not parse sheet %s: \n%s" s.SheetName err.Message