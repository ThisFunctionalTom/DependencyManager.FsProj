namespace DependencyManager.FsProj

open System
open System.IO

/// A marker attribute to tell FCS that this assembly contains a Dependency Manager, or
/// that a class with the attribute is a DependencyManager
[<AttributeUsage(AttributeTargets.Assembly ||| AttributeTargets.Class , AllowMultiple = false)>]
type DependencyManagerAttribute() =
    inherit Attribute()

[<RequireQualifiedAccess>]
module String =
    let startsWith (search: string) (str: string) = str.StartsWith search
    let trim (str: string) = str.Trim()
    let split separator (str: string) = str.Split(separator)
    let notEmptyOrWhiteSpace (str: string) = not (String.IsNullOrWhiteSpace str)

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
            .Split(PATHSeparator)
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.map (fun s -> s.Trim())
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

[<RequireQualifiedAccess>]        
module Sha256 =
    open System.Security.Cryptography
    
    let ofBytes (bytes: byte[]) : byte[] = SHA256.Create().ComputeHash(bytes)
    let ofUTF8String (str: string) : byte[] = 
        System.Text.Encoding.UTF8.GetBytes  str
        |> ofBytes

[<RequireQualifiedAccess>]        
module Bytes =
    let toHexString (bytes: byte[]) = 
        bytes
        |> Array.map (sprintf "%02x")
        |> String.concat ""