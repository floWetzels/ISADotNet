﻿namespace ISADotNet.XLSX.StudyFile

open System.Collections.Generic
open FsSpreadsheet.ExcelIO

open ISADotNet


/// Functions for parsing an ISAXLSX Study File
///
/// This is based on the ISA.Tab Format: https://isa-specs.readthedocs.io/en/latest/isatab.html#assay-table-file
///
/// But with the table being modified according to the SWATE tool: https://github.com/nfdi4plants/Swate
///
/// Additionally, the file can contain several sheets containing parameter tables and a sheet containing additional study metadata
module Study =

    open FsSpreadsheet.DSL

    /// Returns a stuy from a sparseMatrix represntation of an study.xlsx sheet
    ///
    /// processNameRoot is the sheetName (or the protocol name you want to use)
    ///
    /// matrixHeaders are the column headers of the table
    ///
    /// sparseMatrix is a sparse representation of the sheet table, with the first part of the key being the column header and the second part being a zero based row index
    let fromSparseMatrix (processNameRoot:string) matrixHeaders (sparseMatrix : Dictionary<int*string,string>) = 
        let processes = ISADotNet.XLSX.AssayFile.Process.fromSparseMatrix processNameRoot matrixHeaders sparseMatrix
        let characteristics = API.ProcessSequence.getCharacteristics processes
        let factors = API.ProcessSequence.getFactors processes
        let protocols = API.ProcessSequence.getProtocols processes
        Study.create(CharacteristicCategories = characteristics,Factors = factors, Protocols = protocols, ProcessSequence = processes)

    /// Returns a study from a sequence of sparseMatrix representations of study.xlsx sheets
    ///
    /// See "fromSparseMatrix" function for parameter documentation
    let fromSparseMatrices (sheets : (string*(string seq)*Dictionary<int*string,string>) seq) = 
        let processes =
            sheets
            |> Seq.collect (fun (name,matrixHeaders,matrix) -> ISADotNet.XLSX.AssayFile.Process.fromSparseMatrix name matrixHeaders matrix)
            |> ISADotNet.XLSX.AssayFile.AnnotationTable.updateSamplesByThemselves 
            |> Seq.toList
        let characteristics = API.ProcessSequence.getCharacteristics processes
        let factors = API.ProcessSequence.getFactors processes
        let protocols = API.ProcessSequence.getProtocols processes
        Study.create(CharacteristicCategories = characteristics,Factors = factors, Protocols = protocols, ProcessSequence = processes)

// Diesen Block durch JS ersetzen ----> 

    /// Create a new ISADotNet.XLSX study file constisting of two sheets. The first has the name of the studyIdentifier and is meant to store parameters used in the study. The second stores additional study metadata
    let init study studyIdentifier path =
        try 
            Spreadsheet.initWithSst studyIdentifier path
            |> MetaData.init "Study" study
            |> Spreadsheet.close
        with
        | err -> failwithf "Could not init study file: %s" err.Message

    /// Reads a study from an xlsx spreadsheetdocument
    ///
    /// As factors and protocols are used for the investigation file, they are returned individually
    let fromSpreadsheet (doc:DocumentFormat.OpenXml.Packaging.SpreadsheetDocument) = 
        try
            let sst = Spreadsheet.tryGetSharedStringTable doc

            let tryIncludeSST sst cell = 
                try 
                    Cell.includeSharedStringValue (Option.get sst) cell
                with | _ -> cell

            // Reading the "Study" metadata sheet. Here metadata 
            let studyMetaData = 
                match Spreadsheet.tryGetSheetBySheetName "Study" doc with
                | Some sheet ->
                    sheet
                    |> SheetData.getRows
                    |> Seq.map (Row.mapCells (tryIncludeSST sst))
                    |> Seq.map (Row.getIndexedValues None >> Seq.map (fun (i,v) -> (int i) - 1, v))
                    |> MetaData.fromRows
                
                | None -> 
                    printfn "Cannot retrieve metadata: Study file does not contain \"Study\" sheet."
                    Study.empty        
        
            // All sheetnames in the spreadsheetDocument
            let sheetNames = 
                Spreadsheet.getWorkbookPart doc
                |> Workbook.get
                |> Sheet.Sheets.get
                |> Sheet.Sheets.getSheets
                |> Seq.map Sheet.getName
        
            let study =
                sheetNames
                |> Seq.collect (fun sheetName ->                    
                    match Spreadsheet.tryGetWorksheetPartBySheetName sheetName doc with
                    | Some wsp ->
                        match ISADotNet.XLSX.AssayFile.Table.tryGetByDisplayNameBy (fun s -> s.StartsWith "annotationTable") wsp with
                        | Some table -> 
                            // Extract the sheetdata as a sparse matrix
                            let sheet = Worksheet.getSheetData wsp.Worksheet
                            let headers = Table.getColumnHeaders table
                            let m = Table.toSparseValueMatrix sst sheet table
                            Seq.singleton (sheetName,headers,m)     
                        | None -> Seq.empty
                    | None -> Seq.empty                
                )
                |> fromSparseMatrices // Feed the sheets (represented as sparse matrices) into the study parser function
            
            API.Update.UpdateByExisting.updateRecordType studyMetaData study // Merges the study containing the sutdy meta data and the study containing the processes retrieved from the sheets
        with
        | err -> failwithf "Could not read study from spreadsheet: %s" err.Message

    /// Parses the study file
    let fromFile (path:string) =
        try
            let doc = Spreadsheet.fromFile path false
            try
                fromSpreadsheet doc
            finally
                Spreadsheet.close doc
        with
        | err -> failwithf "Could not read study from file with path \"%s\": %s" path err.Message

    /// Parses the study file
    let fromStream (stream:#System.IO.Stream) = 
        try
            let doc = Spreadsheet.fromStream stream false
            try
                fromSpreadsheet doc
            finally
                Spreadsheet.close doc
        with
        | err -> failwithf "Could not read study from stream: %s" err.Message

    
    let toFile (p : string) (study : Study) =
        try
        let s = QueryModel.QStudy.fromStudy study
        let wb = 
            workbook {
                for (i,s) in List.indexed s.Sheets do ISADotNet.XLSX.AssayFile.QSheet.toSheet i s
                sheet "Study" {
                    for r in MetaData.toDSLSheet study do r
                }
            }
        wb.Value.Parse().ToFile(p)
        with
        | err -> failwithf "Could not write Study to Xlsx file in path \"%s\": \n\t%s" p err.Message

    /// ---->  Bis hier