open System
open System.IO

[<EntryPoint>]
let main argv =
    let baseDir = Directory.GetCurrentDirectory()
    let projects =
        List.ofArray argv
        |> List.map Path.GetFullPath
    let projOptions, notifications = ProjectLoader.loadProjects baseDir projects

    ProjectLoader.printResult projOptions notifications

    0