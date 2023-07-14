
// #load "src/DependencyManager.FsProj/Extensions.fs"
// #load "src/DependencyManager.FsProj/DependencyManager.FsProj.fs"

#r "nuget: Ionide.ProjInfo"
#I "./TestIonideProjInfo/bin/Debug/net7.0"
#r "./TestIonideProjInfo/bin/Debug/net7.0/TestIonideProjInfo.dll"

open System
open System.IO

open Extensions
open DependencyManager.FsProj

let baseDir = __SOURCE_DIRECTORY__
let (</>) p1 p2 = Path.Combine(p1, p2)

let testDir = baseDir </> "test-projects"
let simpleLib = testDir </> "SimpleLib" </> "SimpleLib.fsproj"
let projects = [ simpleLib ]

let loadProjects projects =
    let projOptions, notifications = FsProjDependencyManager.loadProjects baseDir projects

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
    printfn "Projects:"
    for p in projOptions do
        printfn $"{p.ProjectFileName}"
    projOptions

loadProjects projects