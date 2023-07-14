module ProjectLoader

open System
open System.IO
open Ionide.ProjInfo
open Ionide.ProjInfo.Types

module DotNet =
    let restore (dotnetExe: FileInfo) projPath =
        printfn $"Restoring '{projPath}'..."
        System.Diagnostics.Process.Start(dotnetExe.FullName, [ "restore"; projPath ]).WaitForExit()

module SdkSetup =
    let getSdkFor (dir: DirectoryInfo) =
        match Paths.dotnetRoot.Value with
        | None -> failwith "Could not find dotnet exe"
        | Some exe ->
            match SdkDiscovery.versionAt dir exe with
            | Error err -> failwith $"Could not find .NET SDK version for directory {dir.FullName}"
            | Ok sdkVersionAtPath ->
                let sdks = SdkDiscovery.sdks exe
                let matchingSdks = sdks |> Array.skipWhile (fun { Version = v } -> v < sdkVersionAtPath)
                match matchingSdks with
                | [||] -> failwith $"Could not find .NET SDK {sdkVersionAtPath}. Please install it."
                | found -> exe, Array.head found

let loadProjects baseDir (projects: string list) =
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

    loadProjects baseDir projects

let printResult (projOptions: ProjectOptions list) notifications =
    if projOptions.IsEmpty then
        printfn "-----------------------"
        printfn "Notifications:"
        Array.iter (printfn "%s") notifications
        printfn "-----------------------"
    let msBuildAssemblies =
        AppDomain.CurrentDomain.GetAssemblies()
        |> Seq.filter (fun ass -> ass.FullName.Contains("Microsoft.Build"))
    for ass in msBuildAssemblies do
        printfn $"{ass.FullName}"
        printfn $"{ass.Location}"
    printfn "-----------------------"
    printfn "Loaded projects:"
    for p in projOptions do
        printfn $"{p.ProjectFileName}"
