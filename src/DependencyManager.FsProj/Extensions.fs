module Extensions

open System
open System.IO

type String with
    member path.EnsureTrailer =
        if path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar) then
            path
        else
            path + string Path.DirectorySeparatorChar

[<RequireQualifiedAccess>]
module String =
    let startsWith (search: string) (str: string) = str.StartsWith search
    let trim (str: string) = str.Trim()

let (|StartsWith|_|) (search: string) (str: string) = 
    if String.startsWith search str then Some StartsWith else None

module Set =
    let collect f s =
        s
        |> Set.fold (fun sources projPath -> Set.union sources (f projPath |> Set.ofSeq)) Set.empty

module Paths =
    open System.Runtime.InteropServices
    
    let private isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    let private isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
    let private isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
    let private isUnix = isLinux || isMac

    let private dotnetBinaryName =
        if isUnix then
            "dotnet"
        else
            "dotnet.exe"

    let private potentialDotnetHostEnvVars =
        [ "DOTNET_HOST_PATH", id // is a full path to dotnet binary
          "DOTNET_ROOT", (fun s -> Path.Combine(s, dotnetBinaryName)) // needs dotnet binary appended
          "DOTNET_ROOT(x86)", (fun s -> Path.Combine(s, dotnetBinaryName)) ] // needs dotnet binary appended

    let private existingEnvVarValue envVarValue =
        match envVarValue with
        | null
        | "" -> None
        | other -> Some other

    let private tryFindFromEnvVar () =
        potentialDotnetHostEnvVars
        |> List.tryPick (fun (envVar, transformer) ->
            match Environment.GetEnvironmentVariable envVar |> existingEnvVarValue with
            | Some varValue -> Some(transformer varValue |> FileInfo)
            | None -> None)

    let private PATHSeparator =
        if isUnix then
            ':'
        else
            ';'

    let private tryFindFromPATH () =
        System
            .Environment
            .GetEnvironmentVariable("PATH")
            .Split(PATHSeparator, StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.tryPick (fun d ->
            let fi = Path.Combine(d, dotnetBinaryName) |> FileInfo

            if fi.Exists then
                Some fi
            else
                None)


    let private tryFindFromDefaultDirs () =
        let windowsPath = $"C:\\Program Files\\dotnet\\{dotnetBinaryName}"
        let macosPath = $"/usr/local/share/dotnet/{dotnetBinaryName}"
        let linuxPath = $"/usr/share/dotnet/{dotnetBinaryName}"

        let tryFindFile p =
            let f = FileInfo p

            if f.Exists then
                Some f
            else
                None

        if isWindows then
            tryFindFile windowsPath
        else if isMac then
            tryFindFile macosPath
        else if isLinux then
            tryFindFile linuxPath
        else
            None

    /// <summary>
    /// provides the path to the `dotnet` binary running this library, respecting various dotnet <see href="https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_root-dotnet_rootx86%5D">environment variables</see>.
    /// Also probes the PATH and checks the default installation locations
    /// </summary>
    let dotnetRoot =
        lazy (tryFindFromEnvVar () |> Option.orElseWith tryFindFromPATH |> Option.orElseWith tryFindFromDefaultDirs)

    let getDotnetRoot () =
        dotnetRoot.Value
        |> Option.defaultWith (fun _ -> 
            failwith "No dotnet binary could be found via the DOTNET_HOST_PATH or DOTNET_ROOT environment variables, the PATH environment variable, or the default install locations")

module Sha256 =
    open System.Security.Cryptography
    
    let ofBytes (bytes: byte[]) : byte[] = SHA256.Create().ComputeHash(bytes)