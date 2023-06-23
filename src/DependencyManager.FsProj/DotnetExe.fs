module DotnetExe

open System
open System.IO
open System.Diagnostics

open Extensions

type PackageInfo = {
    Name: string
    Version: string
}

module Lines =
    let tryGetLines (isHeaderLine, isLine) (lines: string[]) =
        lines 
        |> Array.tryFindIndex isHeaderLine
        |> Option.map (fun start ->
            lines.[start+1..]
            |> Array.takeWhile isLine)

type DotnetExe(projPath: string) =
    let dotnetExe = Paths.getDotnetRoot()
    let projDir = DirectoryInfo (Path.GetDirectoryName projPath)
    let projFile = Path.GetFileName projPath
    
    let execDotnet args =
        let info = ProcessStartInfo()
        info.WorkingDirectory <- projDir.FullName
        info.FileName <- dotnetExe.FullName

        for arg in args do
            info.ArgumentList.Add arg

        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        use p = System.Diagnostics.Process.Start(info)
        let output =
            seq {
                while not p.StandardOutput.EndOfStream do
                    yield p.StandardOutput.ReadLine()
                while not p.StandardError.EndOfStream do
                    yield p.StandardError.ReadLine()
            }
            |> Seq.toArray
        p.WaitForExit()
        output

    member _.GetVersion () =
        execDotnet [ "--version" ]
        |> Array.head
        |> Version

    member _.Restore () =
        execDotnet [ "restore"; projFile ]

    member this.ListPackages () =
        let listPackages() = 
            execDotnet [ "list"; projFile; "package"; "--include-transitive"]
        let parseTopLevelPackageInfo (line: string) =
            let parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries)
            { Name = parts.[1]; Version = parts.[parts.Length-1] }
        let parseTransitivePackageInfo (line: string) =
            let parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries)
            { Name = parts.[1]; Version = parts.[parts.Length-1] }
        let lines =
            let notRestored (lines: string[]) =
                lines 
                |> Array.exists (fun line -> line.Contains "Please run restore before running this command.") 
            let lines = listPackages()
            if notRestored lines then
                this.Restore() |> ignore
                listPackages()
            else
                lines
            |> Array.map String.trim
        let topLevelStart = String.startsWith "Top-level Package"
        let transitiveStart = String.startsWith "Transitive Package"
        let packageLine = String.startsWith "> "
        let topLevelPackages =
            lines
            |> Lines.tryGetLines (topLevelStart, packageLine)
            |> Option.map (Array.map parseTopLevelPackageInfo >> List.ofArray)
            |> Option.defaultValue []
        let transitivePackages =
            lines
            |> Lines.tryGetLines (transitiveStart, packageLine)
            |> Option.map (Array.map parseTransitivePackageInfo >> List.ofArray)
            |> Option.defaultValue []
        List.append topLevelPackages transitivePackages

    member this.ListReferences () =
        let lines = execDotnet [ "list"; projPath; "reference" ]
        let toAbsolutePath (projPath: string) =
            if Path.IsPathRooted projPath 
            then projPath 
            else Path.GetFullPath (Path.Combine(projDir.FullName, projPath))
        let header = "Project reference(s)"
        let anyLine (str: string) = true
        if lines.Length = 1 && lines.[0].Contains("There are no Project to Project references in project") 
        then List.empty
        else
            lines
            |> Lines.tryGetLines (String.startsWith header, anyLine)
            |> Option.map (fun referenceLines -> 
                referenceLines 
                |> Array.map toAbsolutePath
                |> List.ofArray)
            |> Option.defaultWith (fun () -> failwith $"Could not find references header. Line starting with {header} not found")