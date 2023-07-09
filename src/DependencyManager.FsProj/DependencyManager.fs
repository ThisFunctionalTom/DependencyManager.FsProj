namespace DependencyManager.FsProj

open System
open System.IO
open System.Xml.Linq

type ScriptExtension = string
type TFM = string

module FsProjDependencyManager =

    [<assembly: DependencyManager>]
    do ()

    let getAllDependentProjects projects =
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

    let toNugetPackageReference (package: PackageReference) =
        $"""#r @"nuget: {package.Name}, {package.Version}" """
    
    let toLoadSourceLine (sourceFile: string) = 
        $"""#load @"{sourceFile}" """

/// The results of ResolveDependencies
type ResolveDependenciesResult
    (
        success: bool,
        stdOut: string array,
        stdError: string array,
        resolutions: string seq,
        sourceFiles: string seq,
        roots: string seq
    ) =

    /// Succeded?
    member _.Success = success

    /// The resolution output log
    member _.StdOut = stdOut

    /// The resolution error log (* process stderror *)
    member _.StdError = stdError

    /// The resolution paths - the full paths to selected resolved dll's.
    /// In scripts this is equivalent to #r @"c:\somepath\to\packages\ResolvedPackage\1.1.1\lib\netstandard2.0\ResolvedAssembly.dll"
    member _.Resolutions = resolutions

    /// The source code file paths
    member _.SourceFiles = sourceFiles

    /// The roots to package directories
    ///     This points to the root of each located package.
    ///     The layout of the package manager will be package manager specific.
    ///     however, the dependency manager dll understands the nuget package layout
    ///     and so if the package contains folders similar to the nuget layout then
    ///     the dependency manager will be able to probe and resolve any native dependencies
    ///     required by the nuget package.
    ///
    /// This path is also equivalent to
    ///     #I @"c:\somepath\to\packages\ResolvedPackage\1.1.1\"
    member _.Roots = roots

/// the type _must_ take an optional output directory
[<DependencyManager>]
type FsProjDependencyManager(outputDirectory: string option, useResultsCache: bool) =
    let cacheDirectory =
        let createDirectory directory =
            lazy
                try
                    if not (Directory.Exists(directory)) then
                        Directory.CreateDirectory(directory) |> ignore
                    directory
                with _ ->
                    directory

        // Calculate the working directory for dependency management
        //   if a path wasn't supplied to the dependency manager then use the temporary directory as the root
        //   if a path was supplied if it was rooted then use the rooted path as the root
        //   if the path wasn't supplied or not rooted use the temp directory as the root.
        let specialDir =
            let getProfilePath =
                // If it has a directory seperator remove it
                let path = Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)

                if
                    (path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    || (path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                then
                    path.Substring(0, path.Length - 1)
                else
                    path
            // Build path to cache root
            $"{getProfilePath}/.packagemanagement/fsproj"

        let root =
            match outputDirectory with
            | Some v when Path.IsPathRooted(v) -> v
            | Some v -> Path.Combine(specialDir, v)
            | _ -> specialDir

        createDirectory (Path.Combine(root, "Cache"))

    let emitFile path content =
        try
            File.WriteAllText(path, content)
        with _ -> ()

    new(outputDirectory: string option) = FsProjDependencyManager(outputDirectory, true)

    member val Name = "FsProj Dependency Manager" with get

    member val Key = "fsproj" with get

    member _.HelpMessages = [|
        """    #r "fsproj: ./src/MyProj/MyProj.fsproj"; // loads all sources and packages from project MyProj.fsproj"""
    |]

    member _.ClearResultsCache() =
        Directory.Delete(cacheDirectory.Value, true)
        Directory.CreateDirectory(cacheDirectory.Value) |> ignore

    member _.ResolveDependencies(scriptDirectory: string, scriptName: string, scriptExt: string, packageManagerTextLines: (string * string) seq, targetFrameworkMoniker: string, runtimeIdentifier: string, timeout: int) =
        try
            let allProjects = 
                packageManagerTextLines
                |> List.ofSeq
                |> List.distinct
                |> List.map (fun (_, line) -> Path.Combine(scriptDirectory, line) |> Path.GetFullPath)
                |> FsProjDependencyManager.getAllDependentProjects
            
            let projWriteTimes =
                allProjects
                |> List.map (fun proj -> FileInfo(proj))
                |> List.map (fun fi -> $"{fi.FullName}|{fi.LastWriteTimeUtc}")
            
            let loadScriptPath = 
                let hash = 
                    projWriteTimes 
                    |> String.concat Environment.NewLine 
                    |> Sha256.ofUTF8String
                    |> Bytes.toHexString
                    
                Path.Combine(cacheDirectory.Value, $"load-dependencies-{Path.GetFileNameWithoutExtension scriptName}-{hash}.fsx")

            if useResultsCache && File.Exists loadScriptPath then
                let output = 
                    [|
                        $"Using cached load script \"{loadScriptPath}\""
                        for line in File.ReadAllLines loadScriptPath do
                            $"  >{line}"
                    |]
                ResolveDependenciesResult(true, output, [||], [], [ ], []) :> obj
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
                        "Generated new load script \"{loadScriptPath}\""
                        for line in File.ReadAllLines loadScriptPath do
                            $"  >{line}"
                    |]

                ResolveDependenciesResult(true, output, [||], [], [ loadScriptPath ], []) :> obj
        with e -> 
            ResolveDependenciesResult(false, [||], [| e.ToString() |], [], [], []) :> obj