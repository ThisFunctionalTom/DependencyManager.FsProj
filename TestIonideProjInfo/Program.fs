open System
open System.IO
open DependencyManager.FsProj

let (</>) p1 p2 = Path.Combine(p1, p2)
let baseDir = @"C:\Users\leko.tomas\private-source\DependencyManager.FsProj\"

let testDir = baseDir </> "test-projects"
let simpleLib = testDir </> "SimpleLib" </> "SimpleLib.fsproj"
let projects = [ simpleLib ]

let loadProjects projects =
    let projOptions, notifications = FsProjDependencyManager.loadProjects baseDir projects

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
    projOptions

loadProjects projects |> List.iter (fun projOpts -> printfn $"{projOpts.ProjectFileName}")