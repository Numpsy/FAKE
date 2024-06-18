#r "paket:
nuget Fake.Core.Target
nuget Microsoft.Data.SQLite //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Microsoft.Data.Sqlite

let openConn (path:string) =
    let builder = SqliteConnectionStringBuilder()
    builder.DataSource <- path
    let conn = new SqliteConnection(builder.ToString())
    conn.Open()
    conn

module Imports =
    open System.Runtime.InteropServices
    [<DllImport("kernel32.dll")>]
    extern uint32 GetCurrentProcessId()
    [<DllImport("unknown_dependency.dll")>]
    extern uint32 UnknownFunctionInDll()
    [<DllImport("e_sqlite3")>]
    extern uint32 Fake_ShouldNotExistExtryPoint()

// Default target
Target.create "Default" (fun _ ->
  Trace.trace "Hello World from FAKE"
  if Environment.isWindows then
    // #2342: make sure defaults PATH dependencies still work, see https://github.com/fsharp/FAKE/issues/2342
    printfn "Current process: %d" (Imports.GetCurrentProcessId())
  
  use conn = openConn "temp.db"
  ()
)
Target.create "FailWithUnknown" (fun _ ->
  printfn "UnknownFunctionInDll: %d" (Imports.UnknownFunctionInDll())
)
Target.create "FailWithMissingEntry" (fun _ ->
  printfn "Fake_ShouldNotExistExtryPoint: %d" (Imports.Fake_ShouldNotExistExtryPoint())
)

// start build
Target.runOrDefault "Default"
