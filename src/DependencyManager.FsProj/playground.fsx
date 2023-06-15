#load "Extensions.fs"
#load "DotnetExe.fs"
// #load "DependencyManager.FsProj.fs"

open System
open Extensions
open DotnetExe
// open DependencyManager.FsProj

// let dotnetExe = Paths.dotnetRoot.Value.Value

let project = @"C:\Users\tomas\sources\WelsSmasher\WelsSmasher.Core.Tests\WelsSmasher.Core.Tests.fsproj"
let dotnet =  DotnetExe("./test-projects/SimpleLib/SimpleLib.fsproj")

let getPackageReferencesForProjects (projects: string list) =
    let packagesIncludedWithFsi =
        [ "FSharp.Core"; "Microsoft.NETCore.Platforms"; "NETStandard.Library" ]
    projects 
    |> List.collect (fun proj -> DotnetExe(proj).ListPackages())
    |> List.distinct
    |> List.filter (fun package -> packagesIncludedWithFsi |> List.contains package.Name |> not)

getPackageReferencesForProjects ["./test-projects/SimpleLib/SimpleLib.fsproj"]

dotnet.GetVersion() |> printfn "Version: %A"
dotnet.Restore() |> printfn "Restore: %A"
dotnet.ListReferences() |> printfn "References: %A"
dotnet.ListPackages() |> printfn "Packages: %A"
// let projects = [ project ]

// [ @"C:\Users\tomas\sources\DependencyManager.FsProj\test-projects\MultiDependentProjects\Project3\Project3.fsproj" ]
// |> FsProjDependencyManager.withAllProjectDependencies dotnetExe
// |> FsProjDependencyManager.getPackageReferencesForProjects dotnetExe
//|> FsProjDependencyManager.getSourceFilesForProjects 
