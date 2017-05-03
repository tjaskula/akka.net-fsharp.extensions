// sets the current directory to be same as the script directory
System.IO.Directory.SetCurrentDirectory (__SOURCE_DIRECTORY__)

#r @"./packages/FAKE.4.61.0/tools/FakeLib.dll"
#r "System.Configuration.dll"

open Fake

open System

let authors = ["Tomasz Jaskula (@tjaskula)"]

// project name and description
let projectName = "Akka.NET.FSharp.API.Extensions"
let projectDescription = "Set of extensions to the Akka.NET F# API that are not available in the standard library. This includes actor lifecycle managment and stateful actors."
let projectSummary = "Set of extensions to the official Akka.NET F# API."

// directories
let buildDir = "./src/ComposeIt.Akka.FSharp.Extensions/bin"
let packagingRoot = "./packaging/"
let packagingDir = packagingRoot @@ "AkkaFSharpExtensions"

let buildMode = getBuildParamOrDefault "buildMode" "Release"

MSBuildDefaults <- { 
    MSBuildDefaults with 
        ToolsVersion = Some "14.0"
        Verbosity = Some MSBuildVerbosity.Minimal }

Target "Clean" (fun _ ->
    CleanDirs [buildDir; packagingRoot; packagingDir]
)

let setParams defaults = {
    defaults with
        ToolsVersion = Some("14.0")
        Targets = ["Build"]
        Properties =
            [
                "Configuration", buildMode
            ]
    }

Target "BuildApp" (fun _ ->
    build setParams "./src/FSharp.Extensions.sln"
        |> DoNothing
)

Target "CreatePackage" (fun _ ->

    let net45Dir = packagingDir @@ "lib/net45/"

    CleanDirs [net45Dir]

    CopyFile net45Dir (buildDir @@ "Release/ComposeIt.Akka.FSharp.Extensions.dll")
    CopyFile net45Dir (buildDir @@ "Release/ComposeIt.Akka.FSharp.Extensions.XML")
    CopyFile net45Dir (buildDir @@ "Release/ComposeIt.Akka.FSharp.Extensions.pdb")

    NuGet (fun p -> 
        {p with
            Authors = authors
            Project = projectName
            Description = projectDescription                               
            OutputPath = packagingRoot
            Summary = projectSummary
            WorkingDir = packagingDir
            Version = "0.2.2.0"
            Dependencies =
                ["Akka", GetPackageVersion "./packages/" "Akka"
                 "Akka.FSharp", GetPackageVersion "./packages/" "Akka.FSharp"
                 "FsPickler", GetPackageVersion "./packages/" "FsPickler"
                 "FSPowerPack.Core.Community", GetPackageVersion "./packages/" "FSPowerPack.Core.Community"
                 "FSPowerPack.Linq.Community", GetPackageVersion "./packages/" "FSPowerPack.Linq.Community"
                 "Newtonsoft.Json", GetPackageVersion "./packages/" "Newtonsoft.Json"]
            Files = [
                     (@"lib\**\*.*", Some "lib", None)
                     (@"..\..\Install.ps1", Some "tools", None)]
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" }) 
            "Akka.FSharp.Extensions.nuspec"
)

"Clean"
   ==> "BuildApp"

"BuildApp"
   ==> "CreatePackage"

RunTargetOrDefault "CreatePackage"