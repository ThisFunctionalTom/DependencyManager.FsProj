#r "nuget: Ionide.ProjInfo"
#r "nuget: Ionide.ProjInfo.ProjectSystem"
#r "nuget: Ionide.ProjInfo.Sln"

#load "src/DependencyManager.FsProj/Extensions.fs"
#load "src/DependencyManager.FsProj/DependencyManager.FsProj.fs"

open System
open System.IO

open Extensions
open DependencyManager.FsProj
open Ionide.ProjInfo

let baseDir = __SOURCE_DIRECTORY__
let (</>) p1 p2 = Path.Combine(p1, p2)

let testDir = baseDir </> "test-projects"
let simpleLib = testDir </> "SimpleLib" </> "SimpleLib.fsproj"
let projects = [ simpleLib ]

/// <summary></summary>
let projOptions = FsProjDependencyManager.loadProjects baseDir projects

let dotnetExe, sdk = DirectoryInfo baseDir |> SdkSetup.getSdkFor
let toolsPath = SdkSetup.setupForSdk (dotnetExe, sdk)
let loader : IWorkspaceLoader =  WorkspaceLoader.Create toolsPath
projects |> List.iter (DotNet.restore dotnetExe)
loader.LoadProjects projects |> List.ofSeq


