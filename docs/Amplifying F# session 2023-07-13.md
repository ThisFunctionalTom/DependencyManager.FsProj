# Amplifying F# Session (2023-07-13)

## The Idea

![Idea](DependencyManagerIdea1.drawio.svg)

or maybe

![Idea](DependencyManagerIdea2.drawio.svg)


## How F# DependencyManagers work?

- [Interface](../../fsharp/src/FSharp.DependencyManager.Nuget/FSharp.DependencyManager.fsi)

### Implementations

- [Nuget](../../fsharp/src/FSharp.DependencyManager.Nuget/FSharp.DependencyManager.fs)
- [Paket](../../Paket/src/FSharp.DependencyManager.Paket/PaketDependencyManager.fs)

## History of DependencyManager.FsProj

- [Original Project from Chris](https://github.com/ionide/DependencyManager.FsProj)
- [Forked](https://github.com/ThisFunctionalTom/DependencyManager.FsProj)
  - Deployment as [dotnet tool](https://www.nuget.org/packages/DependencyManager.FsProj/)
  ```pwsh
  dotnet tool install --global depman-fsproj
  ```

## Known problems

- Not working with .NET 7 and newest Ionide.ProjInfo
- Deployment is still not simple
- fsproj target framework and fsi framework missmatch  (FSharp.Core for example)

## Decisions

- 

## TODOs

- [ ] 