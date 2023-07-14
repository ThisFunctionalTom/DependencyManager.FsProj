module Extensions

open System
open System.IO
open Ionide.ProjInfo

type String with
    member path.EnsureTrailer =
        if path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar) then
            path
        else
            path + string Path.DirectorySeparatorChar

module SdkSetup =
    open System.Reflection
    open System.Runtime.Loader

    let private resolveFromSdkRoot (sdkRoot: DirectoryInfo) : Func<AssemblyLoadContext, AssemblyName, Assembly> =
        Func<AssemblyLoadContext, AssemblyName, Assembly> (fun assemblyLoadContext assemblyName ->
            let paths =
                [ Path.Combine(sdkRoot.FullName, assemblyName.Name + ".dll")
                  Path.Combine(sdkRoot.FullName, "en", assemblyName.Name + ".dll") ]

            match paths |> List.tryFind File.Exists with
            | Some path -> assemblyLoadContext.LoadFromAssemblyPath path
            | None -> null)

    let mutable private resolveHandler: Func<AssemblyLoadContext, AssemblyName, Assembly> =
        null

    let setupResolveHandler sdkRoot =
        if resolveHandler <> null then
            AssemblyLoadContext.Default.remove_Resolving resolveHandler

        resolveHandler <- resolveFromSdkRoot sdkRoot
        AssemblyLoadContext.Default.add_Resolving resolveHandler


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

module DotNet =
    let restore (dotnetExe: FileInfo) projPath =
        printfn $"Restoring '{projPath}'..."
        System.Diagnostics.Process.Start(dotnetExe.FullName, [ "restore"; projPath ]).WaitForExit()