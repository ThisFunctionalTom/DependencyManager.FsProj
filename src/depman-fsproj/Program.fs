open System
open System.IO
open DependencyManager.FsProj

let showUsage () =
    let installDir = 
        typeof<FsProjDependencyManager>.Assembly.Location 
        |> System.IO.Path.GetDirectoryName
    let jsonFriendlyInstallDir = installDir.Replace("\\", "/")
    printfn "To use with fsi run: "
    printfn $"dotnet fsi --compilertool:{installDir} <script-file.fsx>"
    printfn ""
    printfn "To use with ionide add following to your .vscode/settings.json:"
    printfn "\"FSharp.fsiExtraParameters\": ["
    printfn $"    \"--compilertool:{jsonFriendlyInstallDir}\""
    printfn "]"

let generateLoadScript (projectPaths: string []) =
    let currentDir = Directory.GetCurrentDirectory()
    let projectPaths = List.ofArray projectPaths |> List.map (fun path -> Path.Combine(currentDir, path) |> Path.GetFullPath)
    let dotnetExe = Extensions.Paths.getDotnetRoot()
    printfn $"ProjectPaths: {projectPaths}"
    let projects = projectPaths |> FsProjDependencyManager.withAllProjectDependencies
    printfn $"All projects: {projects.Length}"
    let packages = FsProjDependencyManager.getPackageReferencesForProjects projects
    printfn $"Packages: {packages.Length}"
    let sources = FsProjDependencyManager.getSourceFilesForProjects projects
    printfn $"Sources: {sources.Length}"

    List.concat [
        packages |> List.map FsProjDependencyManager.toNugetPackageReference
        sources |> List.map FsProjDependencyManager.toLoadSourceLine
    ]
    |> String.concat Environment.NewLine
    |> printfn "%s"

[<EntryPoint>]
let main argv =
    if Array.isEmpty argv 
    then showUsage ()
    else generateLoadScript argv
    0