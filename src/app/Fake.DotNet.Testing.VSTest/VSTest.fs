﻿namespace Fake.DotNet.Testing

open Fake.Core
open Fake.Testing.Common
open BlackFox.VsWhere
open System
open System.IO
open System.Text

/// <summary>
/// Contains tasks to run <a href="https://msdn.microsoft.com/en-us/library/ms182486.aspx">VSTest</a> unit tests.
/// </summary>
[<RequireQualifiedAccess>]
module VSTest =

    let private guessVSTestPaths () =
        let vsTestRelativePath = @"Common7\IDE\CommonExtensions\Microsoft\TestWindow"

        VsInstances.getAll ()
        |> List.map (fun vs -> Fake.IO.Path.combine vs.InstallationPath vsTestRelativePath)

    let private vsTestExe =
        if Environment.isMono then
            failwith "VSTest is not supported on the mono platform"
        else
            "vstest.console.exe"

    /// Option which allow to specify if a VSTest error should break the build.
    type ErrorLevel = TestRunnerErrorLevel

    /// <summary>
    /// Parameter type to configure
    /// <a href="https://msdn.microsoft.com/en-us/library/jj155800.aspx">VSTest.Console.exe</a>
    /// </summary>
    type VSTestParams =
        {
            /// Path to the run settings file to run tests with additional settings such as data collectors (optional).
            SettingsPath: string

            /// Names of the tests that should be run (optional).
            Tests: seq<string>

            /// Enables parallel test execution (optional).
            Parallel: bool

            /// Enables code coverage collection (optional).
            EnableCodeCoverage: bool

            /// Run the tests in an isolated process (optional).
            InIsolation: bool

            /// Use installed VSIX extensions in VSTest (optional).
            UseVsixExtensions: bool

            /// Target platform architecture for test execution (optional). Valid options include "x86", "x64" and "ARM".
            Platform: string

            /// Target .NET framework version to use for test execution (optional).
            Framework: string

            /// Run tests that match the given expression (optional). Cannot be used with the Tests argument
            TestCaseFilter: string

            /// The logger to use for test results (optional).
            Logger: string

            /// List discovered tests from the given container path (optional).
            ListTestsPath: string

            /// List installed test discoverers (optional).
            ListDiscoverers: bool

            /// List installed test executors (optional).
            ListExecutors: bool

            /// List installed loggers (optional).
            ListLoggers: bool

            /// List installed settings providers (optional).
            ListSettingsProviders: bool

            /// Path to VSTest.Console.exe (optional). By default the default install location is searched.
            ToolPath: string

            /// Working directory (optional).
            WorkingDir: string

            /// A timeout for the test runner (optional).
            TimeOut: TimeSpan

            /// Error level for controlling how VSTest failures should break the build (optional).
            ErrorLevel: ErrorLevel

            /// Path to test adapter e.g. xUnit (optional)
            TestAdapterPath: string
        }

    /// VSTest default parameters.
    let private VSTestDefaults =
        { SettingsPath = null
          Tests = []
          Parallel = false
          EnableCodeCoverage = false
          InIsolation = true
          UseVsixExtensions = false
          Platform = null
          Framework = null
          TestCaseFilter = null
          Logger = null
          ListTestsPath = null
          ListDiscoverers = false
          ListExecutors = false
          ListLoggers = false
          ListSettingsProviders = false
          ToolPath =
            match ProcessUtils.tryFindFile (guessVSTestPaths ()) vsTestExe with
            | Some path -> path
            | None -> ""
          WorkingDir = null
          TimeOut = TimeSpan.MaxValue
          ErrorLevel = ErrorLevel.Error
          TestAdapterPath = null }

    /// <summary>
    /// Builds the command line arguments from the given parameter record and the given assemblies.
    /// </summary>
    let internal buildArgs (parameters: VSTestParams) (assemblies: string seq) =
        let testsToRun =
            if not (Seq.isEmpty parameters.Tests) then
                sprintf @"/Tests:%s" (parameters.Tests |> String.separated ",")
            else
                null

        StringBuilder()
        |> StringBuilder.appendFileNamesIfNotNull assemblies
        |> StringBuilder.appendIfNotNull parameters.SettingsPath "/Settings:"
        |> StringBuilder.appendIfTrue (not (isNull testsToRun)) testsToRun
        |> StringBuilder.appendIfTrue parameters.Parallel "/Parallel"
        |> StringBuilder.appendIfTrue parameters.EnableCodeCoverage "/EnableCodeCoverage"
        |> StringBuilder.appendIfTrue parameters.InIsolation "/InIsolation"
        |> StringBuilder.appendIfTrue parameters.UseVsixExtensions "/UseVsixExtensions:true"
        |> StringBuilder.appendIfNotNull parameters.Platform "/Platform:"
        |> StringBuilder.appendIfNotNull parameters.Framework "/Framework:"
        |> StringBuilder.appendIfNotNull parameters.TestCaseFilter "/TestCaseFilter:"
        |> StringBuilder.appendIfNotNull parameters.Logger "/Logger:"
        |> StringBuilder.appendIfNotNull parameters.ListTestsPath "/ListTests:"
        |> StringBuilder.appendIfTrue parameters.ListDiscoverers "/ListDiscoverers"
        |> StringBuilder.appendIfTrue parameters.ListExecutors "/ListExecutors"
        |> StringBuilder.appendIfTrue parameters.ListLoggers "/ListLoggers"
        |> StringBuilder.appendIfTrue parameters.ListSettingsProviders "/ListSettingsProviders"
        |> StringBuilder.appendIfNotNull parameters.TestAdapterPath "/TestAdapterPath:"
        |> StringBuilder.toText

    let internal createProcess createTempFile (setParams: VSTestParams -> VSTestParams) (assemblies: string[]) =
        let parameters = VSTestDefaults |> setParams

        if Array.isEmpty assemblies then
            failwith "VSTest: cannot run tests (the assembly list is empty)."

        let tool = parameters.ToolPath
        let generatedArgs = buildArgs parameters assemblies

        let path = createTempFile ()
        let argLine = Args.toWindowsCommandLine [ (sprintf "@%s" path) ]

        CreateProcess.fromRawCommandLine tool argLine
        |> CreateProcess.withWorkingDirectory parameters.WorkingDir
        |> CreateProcess.withTimeout parameters.TimeOut
        |> CreateProcess.addOnSetup (fun () ->
            File.WriteAllText(path, generatedArgs)
            Trace.trace (sprintf "Saved args to '%s' with value: %s" path generatedArgs))
        |> CreateProcess.addOnFinally (fun () -> File.Delete path)
        |> CreateProcess.addOnExited (fun _ exitCode ->
            if exitCode > 0 && parameters.ErrorLevel <> ErrorLevel.DontFailBuild then
                let message =
                    sprintf "%sVSTest test run failed with exit code %i" Environment.NewLine exitCode

                Trace.traceError message
                failwith message)

    /// <summary>
    /// Runs the VSTest command line tool (VSTest.Console.exe) on a group of assemblies.
    /// </summary>
    ///
    /// <param name="setParams">Function used to manipulate the default VSTestParams values.</param>
    /// <param name="assemblies">Sequence of one or more assemblies containing Microsoft Visual Studio Unit
    /// Test Framework unit tests.</param>
    ///
    /// <example>
    /// <code lang="fsharp">
    /// Target.create "Test" (fun _ ->
    ///         !! (testDir + @"\*.Tests.dll")
    ///           |> VSTest.run (fun p -> { p with SettingsPath = "Local.RunSettings" })
    ///     )
    /// </code>
    /// </example>
    let run (setParams: VSTestParams -> VSTestParams) (assemblies: string seq) =
        let assemblies = assemblies |> Seq.toArray
        let details = assemblies |> String.separated ", "
        use disposable = Trace.traceTask "VSTest" details
        createProcess Path.GetTempFileName setParams assemblies |> Proc.run

        disposable.MarkSuccess()
