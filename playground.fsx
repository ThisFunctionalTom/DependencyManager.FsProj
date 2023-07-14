#r "nuget: Ionide.ProjInfo"
#I "./TestIonideProjInfo/bin/Debug/net7.0"
#r "./TestIonideProjInfo/bin/Debug/net7.0/TestIonideProjInfo.dll"

open System
open System.IO

let (</>) p1 p2 = Path.Combine(p1, p2)
let baseDir = __SOURCE_DIRECTORY__
let testDir = baseDir </> "test-projects"
let simpleLib = testDir </> "SimpleLib" </> "SimpleLib.fsproj"
let projects = [ simpleLib ] |> List.map Path.GetFullPath

let projOptions, notifications = ProjectLoader.loadProjects baseDir projects

ProjectLoader.printResult projOptions notifications