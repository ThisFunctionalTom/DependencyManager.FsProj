namespace DependencyManager.FsProj

open System
open System.Diagnostics
open System.Text
open System.IO
open System.Xml.Linq
open Extensions
open DotnetExe

/// A marker attribute to tell FCS that this assembly contains a Dependency Manager, or
/// that a class with the attribute is a DependencyManager
[<AttributeUsage(AttributeTargets.Assembly ||| AttributeTargets.Class , AllowMultiple = false)>]
type DependencyManagerAttribute() =
    inherit Attribute()

module Attributes =
    [<assembly: DependencyManagerAttribute()>]
    do ()

type ScriptExtension = string
type HashRLines = string seq
type TFM = string

/// The results of ResolveDependencies
type ResolveDependenciesResult (success: bool, stdOut: string array, stdError: string array, resolutions: string seq, sourceFiles: string seq, roots: string seq) =

    /// Succeded?
    member _.Success = success

    /// The resolution output log
    member _.StdOut = stdOut

    /// The resolution error log (* process stderror *)
    member _.StdError = stdError

    /// The resolution paths
    member _.Resolutions = resolutions

    /// The source code file paths
    member _.SourceFiles = sourceFiles

    /// The roots to package directories
    member _.Roots = roots

module FsProjDependencyManager =

    let withAllProjectDependencies projects =
        let rec loop (depProjs: string list) (toProcess: string list) =
            match toProcess with
            | [] -> depProjs
            | projPath :: rest ->
                let dotnetExe = DotnetExe(projPath)
                let newProjs =
                    dotnetExe.ListReferences()
                    |> fun newProjs -> newProjs |> List.filter (fun p -> depProjs |> List.contains p |> not)
                loop (List.append depProjs newProjs |> List.distinct) (List.append rest newProjs |> List.distinct)
        loop projects projects

    let getSourceFilesForProject (projPath: string) =
        let projDir = Path.GetDirectoryName projPath
        let toAbsolutePath (projPath: string) =
            if Path.IsPathRooted projPath then projPath else Path.GetFullPath (Path.Combine(projDir, projPath))
        let doc = XDocument.Load(projPath)
        doc.Descendants("Compile") 
        |> Seq.map (fun node -> node.Attribute("Include").Value)
        |> List.ofSeq
        |> List.map toAbsolutePath

    let getSourceFilesForProjects (projects: string list) =
        projects 
        |> List.collect getSourceFilesForProject
        |> List.distinct

    let getPackageReferencesForProjects (projects: string list) =
        let packagesIncludedWithFsi =
            [ "FSharp.Core"; "Microsoft.NETCore.Platforms"; "NETStandard.Library" ]
        projects 
        |> List.collect (fun proj -> DotnetExe(proj).ListPackages())
        |> List.distinct
        |> List.filter (fun package -> packagesIncludedWithFsi |> List.contains package.Name |> not)

    let toNugetPackageReference (package: PackageInfo) =
        $"""#r @"nuget: {package.Name}, {package.Version}" """
    
    let toLoadSourceLine sourceFile = 
        $"""#load @"{sourceFile}" """

/// the type _must_ take an optional output directory
[<DependencyManager>]
type FsProjDependencyManager(outputDirectory: string option) =
    let userProfile =
        let res = Environment.GetEnvironmentVariable("USERPROFILE")
        if System.String.IsNullOrEmpty res then
            Environment.GetEnvironmentVariable("HOME")
        else res
    let workingDirectory =
        // Calculate the working directory for dependency management
        //   if a path wasn't supplied to the dependency manager then use the temporary directory as the root
        //   if a path was supplied if it was rooted then use the rooted path as the root
        //   if the path wasn't supplied or not rooted use the temp directory as the root.
        let directory =
            let path = Path.Combine(Process.GetCurrentProcess().Id.ToString() + "--"+ Guid.NewGuid().ToString())
            match outputDirectory with
            | None -> Path.Combine(Path.GetTempPath(), path)
            | Some v ->
                if Path.IsPathRooted(v) then Path.Combine(v, path)
                else Path.Combine(Path.GetTempPath(), path)

        lazy
            try
                if not (Directory.Exists(directory)) then
                    Directory.CreateDirectory(directory) |> ignore
                directory
            with | _ -> directory

    let emitFile path content =
        try
            File.WriteAllText(path, content)
        with _ -> ()

    let generateLoadScript loadScriptPath allProjects =
        // let sourceFiles =
        //     FsProjDependencyManager.getSourceFilesForProjects allProjects
        // let loadLines =
        //     sourceFiles 
        //     |> List.map FsProjDependencyManager.toLoadSourceLine
        let packages = 
            FsProjDependencyManager.getPackageReferencesForProjects allProjects
        let packageReferences = 
            packages
            |> List.map FsProjDependencyManager.toNugetPackageReference

        let loadScriptContent = 
            //List.append packageReferences loadLines
            packageReferences
            |> String.concat Environment.NewLine
        emitFile loadScriptPath loadScriptContent

    let generateDebugOutput scriptDir mainScriptName scriptName packageManagerTextLines targetFramework (projects: string list) outputScriptPath loadScriptContent =
        [|
            $"================================"
            $"WorkingDirectory: {workingDirectory.Value}"
            $"ScriptDir: {scriptDir}"
            $"MainScriptName: {mainScriptName}"
            $"ScriptName: {scriptName}"
            $"PackageManagerTextLines:"
            for line in packageManagerTextLines do
                $"  >{line}"
            $"TargetFramework: {targetFramework}"
            $"OutputScriptPath: {outputScriptPath}"
            $"================================"
            // $"All projects:"
            // for proj in projects do
            //     $" > {proj}"            
            // $"Load script content:"
            // $"--------------------------------"
            // $"{loadScriptContent}"
            // $"--------------------------------"
        |]

    let mutable generatedFile : string = null

    member val Key = "fsproj" with get
    member val Name = "FsProj Dependency Manager" with get
    member _.HelpMessages = [|
        """    #r "fsproj: ./src/MyProj/MyProj.fsproj"; // loads all sources and packages from project MyProj.fsproj"""
    |]

    member _.ResolveDependencies(scriptDir: string, mainScriptName: string, scriptName: string, packageManagerTextLines: HashRLines, targetFramework: TFM) : ResolveDependenciesResult =
        try
            let allProjects = 
                packageManagerTextLines
                |> List.ofSeq
                |> List.distinct
                |> List.map (fun line -> Path.Combine(scriptDir, line) |> Path.GetFullPath)
                |> FsProjDependencyManager.withAllProjectDependencies
            
            let projWriteTimes =
                allProjects
                |> List.map (fun proj -> FileInfo(proj))
                |> List.map (fun fi -> $"{fi.FullName}|{fi.LastWriteTimeUtc}")
            
            let loadScriptPath = 
                let hash = 
                    projWriteTimes 
                    |> String.concat Environment.NewLine 
                    |> Encoding.UTF8.GetBytes 
                    |> Sha256.ofBytes
                    |> Array.map (sprintf "%02x")
                    |> String.concat ""
                Path.Combine(workingDirectory.Value, $"load-dependencies-{Path.GetFileNameWithoutExtension mainScriptName}-{hash}.fsx")

            let workdirFiles = Directory.GetFiles(workingDirectory.Value)
            let fileInfo = FileInfo(loadScriptPath)

            if generatedFile = loadScriptPath then
                let output = 
                    [|
                        $"WorkingDir: {workingDirectory.Value}"
                        $"Cached: {loadScriptPath}"
                        $"LastWriteTime: {fileInfo.LastWriteTime}"
                        $"MainScriptName: {mainScriptName}"
                        $"ScriptName: {scriptName}"
                        "LoadScript:"
                        for line in File.ReadAllLines loadScriptPath do
                            $"  >{line}"
                    |]
                ResolveDependenciesResult(true, output, [||], [], [ ], [])
            else
                let sourceFiles = 
                    FsProjDependencyManager.getSourceFilesForProjects allProjects
                    |> List.map FsProjDependencyManager.toLoadSourceLine
                
                let packageReferences = 
                    FsProjDependencyManager.getPackageReferencesForProjects allProjects
                    |> List.map FsProjDependencyManager.toNugetPackageReference

                let loadScriptContent = 
                    List.append packageReferences sourceFiles
                    |> String.concat Environment.NewLine                    

                emitFile loadScriptPath loadScriptContent
                
                let output = 
                    [|
                        $"WorkingDir: {workingDirectory.Value}"
                        $"LastGeneratted: {generatedFile}"
                        $"Generated: {loadScriptPath}"
                        $"WorkingDirContents:"
                        for file in workdirFiles do
                            $"  >{file}"
                        $"MainScriptName: {mainScriptName}"
                        $"ScriptName: {scriptName}"
                        "LoadScript:"
                        for line in File.ReadAllLines loadScriptPath do
                            $"  >{line}"
                    |]

                generatedFile <- loadScriptPath

                //let output = generateDebugOutput scriptDir mainScriptName scriptName packageManagerTextLines targetFramework allProjects loadScriptPath loadScriptContent
                //ResolveDependenciesResult(true, output, [||], [], [ loadScriptPath; yield! sourceFiles ], [])
                ResolveDependenciesResult(true, output, [||], [], [ loadScriptPath ], [])
        with e -> 
            printfn "exception while resolving dependencies: %s" (string e)
            ResolveDependenciesResult(false, [||], [| e.ToString() |], [], [], [])