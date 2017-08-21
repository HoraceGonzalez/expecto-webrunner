// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r @"packages/build-tools/FAKE/tools/FakeLib.dll"

//System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open Fake.UserInputHelper
open Fake.Testing
open Fake.Testing.Expecto

open System
open System.IO
open System.Diagnostics

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "Expecto.WebRunner"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "A web-based test runner for expecto"

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = summary

// List of author names (for NuGet package)
let authors = [ "Horace Gonzalez" ]

let tags = "expecto test runner"

// File system information
let solutionFile  = "Expecto.WebRunner.sln"

// define test executables
let testExecutables = "tests/**/bin/Release/*Tests*.exe"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "RealtyShares"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "expecto-webrunner"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/RealtyShares"

// Read release notes & version info from RELEASE_NOTES.md
let release = 
    File.ReadLines "RELEASE_NOTES.md" 
    |> ReleaseNotesHelper.parseReleaseNotes

let buildDir = "./bin/"

let nugetDir = "./nuget/"

// Disable writing to default Fake.Errors.txt files, which causes resource contention while multiple jenkins processes are running
MSBuildLoggers <- []

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Helper for starting processes. This custom implementation is necessary because FAKE kills all the ones started with ExecProcess
let startProcess execPath (workingDir:string option) (args:string option) =
    let pinfo = ProcessStartInfo()
    pinfo.FileName <- execPath
    pinfo.CreateNoWindow <- false
    pinfo.UseShellExecute <- true
    pinfo.WindowStyle <- System.Diagnostics.ProcessWindowStyle.Normal
    pinfo.WorkingDirectory <- defaultArg workingDir currentDirectory
    match args with
    | Some args -> pinfo.Arguments <- args
    | None -> ()
    try
        System.Console.OutputEncoding <- System.Text.Encoding.UTF8
    with exn ->
        logfn "Failed setting UTF8 console encoding, ignoring error... %s." exn.Message

    if isMono && pinfo.FileName.ToLowerInvariant().EndsWith(".exe") then
        pinfo.Arguments <- "--debug \"" + pinfo.FileName + "\" " + pinfo.Arguments
        pinfo.FileName <- monoPath
    System.Diagnostics.Process.Start(pinfo) |> ignore

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|) (projFileName:string) =
    match projFileName with
    | f when f.EndsWith("fsproj") -> Fsproj
    | f when f.EndsWith("csproj") -> Csproj
    | f when f.EndsWith("vbproj") -> Vbproj
    | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ Attribute.Title (projectName)
          Attribute.Product project
          Attribute.Description summary ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath,
          projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName)
        )

    [!! "src/**/*.??proj"; !! "tests/**/*.??proj"]
    |> Seq.collect id
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
        match projFileName with
        | Fsproj -> CreateFSharpAssemblyInfo (folderName @@ "AssemblyInfo.fs") attributes
        | Csproj -> CreateCSharpAssemblyInfo ((folderName @@ "Properties") @@ "AssemblyInfo.cs") attributes
        | Vbproj -> CreateVisualBasicAssemblyInfo ((folderName @@ "My Project") @@ "AssemblyInfo.vb") attributes)
)

// a custom version of Fake.Paket.Restore that uses the new --group paket cli param
let paketRestore group =
    let parameters = Paket.PaketRestoreDefaults()
    use __ = traceStartTaskUsing "PaketRestore" parameters.WorkingDir

    let restoreResult =
        ExecProcess (fun info ->
            info.FileName <- parameters.ToolPath
            info.WorkingDirectory <- parameters.WorkingDir
            info.Arguments <- sprintf "restore --group %s" group ) parameters.TimeOut

    if restoreResult <> 0 then failwithf "Error during restore %s." parameters.WorkingDir

Target "Restore" (fun _ ->
    paketRestore "main"
)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the
// src folder to support multiple project outputs
Target "CopyBinaries" (fun _ ->
    !! "src/**/*.??proj"
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin/Release", buildDir @@ (System.IO.Path.GetFileNameWithoutExtension f)))
    |>  Seq.iter (fun (fromDir, toDir) -> CopyDir toDir fromDir (fun _ -> true))
)

// Copy assets
Target "CopyAssets" (fun _ ->
    !! "src/**/assets"
    |> Seq.map (fun f -> (System.IO.Path.GetFullPath f), buildDir @@ (System.IO.Path.GetFileName f))
    |> Seq.iter (fun (fromDir, toDir) -> CopyDir toDir fromDir (fun _ -> true))
)

Target "Start" (fun _ ->
    startProcess 
        (currentDirectory @@ "src/Expecto.WebRunner.Server/bin/Debug/Expecto.WebRunner.Server.exe") 
        (Some <| currentDirectory @@ "src/Expecto.WebRunner.Server/")
        None
)

// --------------------------------------------------------------------------------------
// Clean build results

let cleanBuildArtifacts() =
    [!! "src/**/bin"; !! "src/**/obj";
     !! "tests/**/bin"; !! "tests/**/obj"]
    |> Seq.collect id
    |> CleanDirs

Target "Clean" (fun _ ->
    CleanDirs [buildDir; "temp"]
)

Target "CleanBuildArtifacts" cleanBuildArtifacts

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    MSBuild
        ""
        "Rebuild"
        ([
            ("Configuration", "Release")
            ("Verbosity", "minimal")
            #if MONO
            ("DefineConstants", "MONO")
            #endif
        ])
        (!! solutionFile)
        |> ignore
)

Target "GeneratePaketLoadScripts" (fun _ ->
    let paketPath = (findToolFolderInSubPath "paket.exe" (currentDirectory @@ ".paket")) @@ "paket.exe"
    ProcessHelper.Shell.Exec(paketPath,"generate-load-scripts --framework net461 --type fsx",currentDirectory) |> ignore
)

Target "Debug" (fun _ ->
    if hasBuildParam "Clean"
        then cleanBuildArtifacts()

    MSBuild
        ""
        "Build"
        ([
            ("Configuration", "Debug")
            ("Verbosity", "minimal")
            #if MONO
            ("DefineConstants", "MONO")
            #endif
        ])
        (!! solutionFile)
        |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target "RunTests" (fun _ ->
    !! testExecutables
    |> Expecto (fun p ->
        { p with
            Debug = false
            Parallel = true
            ListTests = false
            Summary = true
        })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun () ->
    let nugetToolsDir = nugetDir @@ "tools"
    let nugetLibDir = nugetDir @@ "lib"
    let nugetLib451Dir = nugetLibDir @@ "net461"

    CleanDir nugetToolsDir
    CleanDir nugetLibDir
    DeleteDir nugetLibDir

    !! (buildDir @@ "**/*.*") |> Copy nugetToolsDir
        
    let setParams p =
        {p with
            Authors = authors
            Project = project
            Description = description
            Version = release.NugetVersion
            OutputPath = nugetDir
            WorkingDir = nugetDir
            Summary = summary
            ReleaseNotes = release.Notes |> toLines
            Tags = tags
            Dependencies = p.Dependencies
            Publish = false }

    NuGet setParams (sprintf "%s.nuspec" project))
   
// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Restore"
  ==> "GeneratePaketLoadScripts"
  ==> "Debug"
  ==> "Start"

"Clean"
  ==> "Restore"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyAssets"
  ==> "CopyBinaries"
  ==> "RunTests"
  ==> "All"

"CopyBinaries"
 ==> "NuGet"

RunTargetOrDefault "All"
