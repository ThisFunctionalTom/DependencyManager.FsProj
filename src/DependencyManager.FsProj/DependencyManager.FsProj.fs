namespace DependencyManager.FsProj

open System
open System.Diagnostics
open System.IO
open Ionide.ProjInfo
open Ionide.ProjInfo.Types
open Extensions

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
    let loadProjects baseDir (projects: string list) =
        let notifications = ResizeArray()
        let dotnetExe, sdk = DirectoryInfo baseDir |> SdkSetup.getSdkFor
        notifications.Add $"Using SDK version: {sdk.Version}"
        let toolsPath = Init.init (DirectoryInfo baseDir) (Some dotnetExe)
        let loader : IWorkspaceLoader =  WorkspaceLoader.Create toolsPath
        use _ = loader.Notifications.Subscribe(fun n -> notifications.Add $"%A{n}")
        projects |> List.iter (DotNet.restore dotnetExe)
        let binlogDir = DirectoryInfo (Path.Combine(baseDir,"binlogDir"))
        let projects = loader.LoadProjects(projects, [], BinaryLogGeneration.Within binlogDir) |> List.ofSeq
        projects, notifications.ToArray()

    let sortByDependencies (projs: ProjectOptions list) =
        let map = projs |> List.map (fun p -> p.ProjectFileName, p) |> Map.ofList
        let edges =
            projs
            |> List.map (fun p -> p.ProjectFileName, p.ReferencedProjects |> List.map (fun pr -> pr.ProjectFileName))

        let rec loop sorted toSort =
            match toSort |> List.partition (snd >> List.isEmpty) with
            | [], [] -> sorted
            | [], _ -> failwith "Cycle found"
            | noDeps, withDeps ->
                let projsNoDeps = noDeps |> List.map fst
                let sorted' = sorted @ projsNoDeps
                let removeDeps (p, deps) =
                    p, deps |> List.filter (fun d -> not (projsNoDeps |> List.contains d))
                let toSort' =
                    withDeps |> List.map removeDeps
                loop sorted' toSort'

        loop [] edges
        |> List.map (fun projName -> Map.find projName map)

    let getPackageReferences projects =
        projects
        |> List.collect (fun proj -> proj.PackageReferences)
        |> List.map (fun pref -> pref.FullPath)
        |> List.distinct

    let getSourceFiles projects =
        projects
        |> sortByDependencies
        |> List.collect (fun proj -> proj.SourceFiles)
        |> List.distinct

    let toHashRLine package = "#r @\"" + package + "\""
    let toLoadSourceLine sourceFile = "#load @\"" + sourceFile + "\""

    let getErrors (projects: ProjectOptions seq) =
        let notRestored =
            projects
            |> Seq.filter (fun proj -> not proj.ProjectSdkInfo.RestoreSuccess)
            |> Seq.map (fun proj -> proj.ProjectFileName)

        if not (Seq.isEmpty notRestored) then
            notRestored
            |> Seq.append ["Please restore following projects with 'dotnet restore': "]
            |> Array.ofSeq
        else [||]

/// the type _must_ take an optional output directory
[<DependencyManager>]
type FsProjDependencyManager(outputDirectory: string option) =
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

    let generateDebugOutput (notifications: string[]) (projects: ProjectOptions list) packageReferences sourceFiles =
        [|
            "==============================="
            yield! notifications
            "-------------------------------"
            "Projects:"
            for project in projects do
                project.ProjectFileName
            ""
            "Package references:"
            yield! packageReferences
            ""
            "Sources: "
            yield! sourceFiles
            ""
            "==============================="
        |]

    member val Key = "fsproj" with get
    member val Name = "FsProj Dependency Manager" with get
    member _.HelpMessages = [|
        """    #r "fsproj: ./src/MyProj/MyProj.fsproj"; // loads all sources and packages from project MyProj.fsproj"""
    |]

    member _.ResolveDependencies(scriptDir: string, mainScriptName: string, scriptName: string, packageManagerTextLines: HashRLines, targetFramework: TFM) : ResolveDependenciesResult =
        try
            let projectPaths =
                packageManagerTextLines
                |> List.ofSeq
                |> List.map (fun line -> Path.Combine(scriptDir, line) |> Path.GetFullPath)
            let projects, notifications = FsProjDependencyManager.loadProjects scriptDir projectPaths
            let packageReferences = FsProjDependencyManager.getPackageReferences projects
            let sourceFiles = FsProjDependencyManager.getSourceFiles projects

            let stdError = FsProjDependencyManager.getErrors projects
            let output = generateDebugOutput notifications projects packageReferences sourceFiles
            //let output = [||]

            ResolveDependenciesResult(true, output, stdError, packageReferences, sourceFiles, [])
        with e ->
            ResolveDependenciesResult(false, [||], [| e.ToString() |], [], [], [])