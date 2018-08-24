// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r "paket:
nuget Fake.Core.Target
nuget Fake.Api.GitHub
nuget Fake.Core.ReleaseNotes
nuget Fake.DotNet.AssemblyInfoFile
nuget Fake.DotNet.MSBuild
nuget Fake.DotNet.Cli
nuget Fake.DotNet.MSBuild
nuget Fake.DotNet.NuGet
nuget Fake.DotNet.Paket
nuget Fake.DotNet.FSFormatting
nuget Fake.DotNet.Testing.MSpec
nuget Fake.DotNet.Testing.XUnit2
nuget Fake.DotNet.Testing.NUnit
nuget Fake.Tools.Git
nuget xunit.runner.console
nuget Octokit //"
#load "./.fake/build.fsx/intellisense.fsx"

open System.IO
open Fake.Core
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.DotNet

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
let gitRaw = Environment.environVarOrDefault "gitRaw" "https://raw.github.com/tjaskula"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// Generate assembly info files with the right version & up-to-date information       
Target.create "AssemblyInfo" (fun _ ->
    let setProjectDetails project = 
        Xml.pokeInnerText project "//Project/PropertyGroup/Version" release.AssemblyVersion    
        Xml.pokeInnerText project "//Project/PropertyGroup/PackageReleaseNotes" (release.Notes |> String.concat "\n")
    !! "src/**/*.fsproj" |> Seq.iter setProjectDetails
)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the 
// src folder to support multiple project outputs
Target.create "CopyBinaries" (fun _ ->
    !! "src/**/*.??proj"
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin/Release", "bin" @@ (System.IO.Path.GetFileNameWithoutExtension f)))
    |>  Seq.iter (fun (fromDir, toDir) -> Shell.copyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
)

// --------------------------------------------------------------------------------------
// Build library & test project
Target.create "Build" (fun _ ->
    !! solutionFile
    |> MSBuild.runRelease id "" "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner
// Doesn't work because of the issue https://github.com/fsharp/FAKE/issues/2094
open Fake.DotNet.Testing

open Fake.DotNet.Testing.XUnit2
Target.create "RunTests" (fun _ ->
    !! testAssemblies
    |> XUnit2.run (fun p ->
        { p with
            TimeOut = System.TimeSpan.FromMinutes 20.
            XmlOutputPath = Some "TestResults.xml"
            ToolPath = "~/.nuget/packages/xunit.runner.console/2.4.0/tools/net452/xunit.console.exe"})
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" <| fun _ ->
    let pack = 
        DotNet.pack(fun p -> 
            { p with
                OutputPath = Some "bin"
                Configuration = DotNet.Release
                Common = { p.Common with CustomParams = Some "--include-symbols" }})
    
    !! "src/**/*.fsproj" |> Seq.iter pack


Target.create "PublishNuget" (fun _ ->
    Paket.push(fun p -> 
        { p with
            WorkingDir = "bin"
            DegreeOfParallelism = 1 })
)

(*
#load ".fake/build.fsx/paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
*)
open Octokit
open Fake.Tools
open Fake.Api
open Fake.DotNet

Target.create "Release" (fun _ ->
    Git.Staging.stageAll ""
    Git.Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    Git.Branches.push ""

    Git.Branches.tag "" release.NugetVersion
    Git.Branches.pushTag "" "origin" release.NugetVersion
    
    // release on github
    GitHub.createClient (Environment.environVarOrDefault "github-user" "") (Environment.environVarOrDefault "github-pw" "")
    |> GitHub.draftNewRelease gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    // TODO: |> uploadFile "PATH_TO_FILE"    
    |> GitHub.publishDraft
    |> Async.RunSynchronously
)

Target.create "BuildPackage" (fun _ -> ())

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override
open Fake.Core.TargetOperators
Target.create "All" (fun _ -> ())

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
  //==> "RunTests"
  ==> "All"

"All" 
  ==> "NuGet"
  ==> "BuildPackage"

Target.runOrDefault "All"