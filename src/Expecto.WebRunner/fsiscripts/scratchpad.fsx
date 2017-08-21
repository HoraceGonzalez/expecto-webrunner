System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

#r @"../../../packages/build-tools/FAKE/tools/FakeLib.dll"

#load @"../../../.paket/load/net461/main.group.fsx"

open Fake
open Fake.Testing
open Fake.Testing.Expecto

let projectDir = "c:/code/realtyshares.new/bizapps"

let filter = "tests/**/bin/Debug/*Tests*.exe"
let testAssemblies = projectDir @@ filter

!! testAssemblies 
|>  Seq.map (fun f -> f)

|>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin/Release", "bin" @@ (System.IO.Path.GetFileNameWithoutExtension f)))
    

filesInDirMatchingRecursive testAssemblies projectDir

!! testAssemblies
|>  Seq.map (fun f -> f)
    