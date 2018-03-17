// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System
open System.IO

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "Akka.FSharp.Extensions"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Set of extensions to the official Akka.NET F# API."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "Set of extensions to the Akka.NET F# API that are not available in the standard library. This includes actor lifecycle managment and stateful actors."

// List of author names (for NuGet package)
let authors = [ "Tomasz Jaskula" ]

// Tags for your project (for NuGet package)
let tags = "akka.net fsharp"

// File system information 
let solutionFile  = "FSharp.Extensions.sln"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/Akka.FSharp.Extensions.Tests/bin/Release/**/*Tests.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "tjaskula" 
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "akka.net-fsharp.extensions"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/tjaskula"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" <| fun _ ->
    let setProjectDetails project = 
        XmlPokeInnerText project "//Project/PropertyGroup/Version" release.AssemblyVersion    
        XmlPokeInnerText project "//Project/PropertyGroup/PackageReleaseNotes" (release.Notes |> String.concat "\n")

    !! "src/**/*.fsproj" |> Seq.iter setProjectDetails

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the 
// src folder to support multiple project outputs
Target "CopyBinaries" (fun _ ->
    !! "src/**/*.??proj"
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin/Release", "bin" @@ (System.IO.Path.GetFileNameWithoutExtension f)))
    |>  Seq.iter (fun (fromDir, toDir) -> CopyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    !! solutionFile
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner
open Fake.Testing.XUnit2
Target "RunTests" (fun _ ->
    !! testAssemblies
    |> xUnit2 (fun p ->
        { p with
            TimeOut = TimeSpan.FromMinutes 20.
            XmlOutputPath = Some "TestResults.xml"
            ToolPath = "packages/xunit.runner.console/tools/net452/xunit.console.exe" })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" <| fun _ ->
    let pack project = 
        DotNetCli.Pack(fun p -> 
            { p with
                Project = project
                OutputPath = "bin"
                Configuration = "Release"
                AdditionalArgs = ["--include-symbols"] })
    
    !! "src/**/*.fsproj" |> Seq.iter pack


Target "PublishNuget" (fun _ ->
    Paket.Push(fun p -> 
        { p with
            WorkingDir = "bin"
            DegreeOfParallelism = 1 })
)

#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

Target "Release" (fun _ ->
    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion
    
    // release on github
    createClient (getBuildParamOrDefault "github-user" "") (getBuildParamOrDefault "github-pw" "")
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    // TODO: |> uploadFile "PATH_TO_FILE"    
    |> releaseDraft
    |> Async.RunSynchronously
)

Target "BuildPackage" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
  ==> "All"

"All" 
  ==> "NuGet"
  ==> "BuildPackage"

RunTargetOrDefault "All"