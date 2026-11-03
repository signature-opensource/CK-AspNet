using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppHost.Tests;

/// <summary>
/// The "build" files of CK.AppHost are only imported through a package reference: these tests build small projects in a
/// temporary folder (outside this repository: its Directory.Build.props must not apply) that import them by path.
/// </summary>
[TestFixture]
public class HostProjectTargetsTests
{
    static NormalizedPath BuildFolder => TestHelper.SolutionFolder.Combine( "CK.AppHost/build" );

    static NormalizedPath CreateRoot() => new NormalizedPath( Path.Combine( Path.GetTempPath(), "CKAppHost-" + Guid.NewGuid().ToString( "N" ) ) );

    // The builder writes the generated code in the "$StObjGen" folder of its host after its own build (like CK.AppBuilder).
    static void CreateBuilder( NormalizedPath root, string name, string hostName )
    {
        var project = root.AppendPart( name );
        Directory.CreateDirectory( project );
        File.WriteAllText( project.AppendPart( $"{name}.csproj" ), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Target Name="WriteGeneratedCode" AfterTargets="Build">
                <WriteLinesToFile File="..\{hostName}\$StObjGen\G0.cs" Lines="static class Generated %7B public const int Value = 42%3B %7D" Overwrite="true" />
              </Target>
            </Project>
            """ );
        File.WriteAllText( project.AppendPart( "Builder.cs" ), "class Builder { }" );
    }

    static NormalizedPath CreateHost( NormalizedPath root, string name, string? builderName, string outputType = "Exe", string? extraProperties = null )
    {
        var project = root.AppendPart( name );
        Directory.CreateDirectory( project );
        var reference = builderName != null
                        ? $"""<ProjectReference Include="..\{builderName}\{builderName}.csproj" ReferenceOutputAssembly="false" Private="false" />"""
                        : "";
        File.WriteAllText( project.AppendPart( $"{name}.csproj" ), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{BuildFolder.AppendPart( "CK.AppHost.props" ).Path}" />
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                {extraProperties}
              </PropertyGroup>
              <ItemGroup>
                {reference}
              </ItemGroup>
              <Import Project="{BuildFolder.AppendPart( "CK.AppHost.targets" ).Path}" />
            </Project>
            """ );
        // It compiles only when the generated code is compiled.
        File.WriteAllText( project.AppendPart( "Program.cs" ), "return Generated.Value == 42 ? 0 : 1;" );
        // The client applications are not part of the project: this doesn't compile.
        Directory.CreateDirectory( project.AppendPart( "Clients" ) );
        File.WriteAllText( project.Combine( "Clients/NotCSharp.cs" ), "This is not C#." );
        return project;
    }

    static async Task<(int ExitCode, string Output)> DotNetAsync( string arguments )
    {
        var info = new ProcessStartInfo( "dotnet", arguments )
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start( info ).ShouldNotBeNull();
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, output);
    }

    static Task<(int ExitCode, string Output)> BuildAsync( NormalizedPath project ) => DotNetAsync( $"build \"{project.Path}\" -v q -nologo" );

    [Test]
    [CancelAfter( 180000 )]
    public async Task a_clean_build_of_the_host_compiles_the_code_generated_by_its_builder_Async()
    {
        var root = CreateRoot();
        try
        {
            CreateBuilder( root, "Demo.Builder", "Demo.Host" );
            var host = CreateHost( root, "Demo.Host", "Demo.Builder" );
            var (exitCode, output) = await BuildAsync( host );
            exitCode.ShouldBe( 0, output );
            var (runCode, runOutput) = await DotNetAsync( $"run --project \"{host.Path}\" --no-build" );
            runCode.ShouldBe( 0, runOutput );
            // The host is a party (its manifest in the "#Dev" folder: the targets of CK.AppIdentity.Hosting).
            var (_, party) = await DotNetAsync( $"msbuild \"{host.AppendPart( "Demo.Host.csproj" ).Path}\" -getProperty:CKAppIdentityParty -nologo" );
            party.Trim().ShouldBe( "true" );
            // It runs in its project folder (its content root).
            var (_, workingDirectory) = await DotNetAsync( $"msbuild \"{host.AppendPart( "Demo.Host.csproj" ).Path}\" -getProperty:RunWorkingDirectory -nologo" );
            new NormalizedPath( workingDirectory.Trim() ).ShouldBe( host );
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }

    [Test]
    [CancelAfter( 180000 )]
    public async Task the_builder_is_named_after_the_host_or_by_CKAppHostBuilder_Async()
    {
        var root = CreateRoot();
        try
        {
            CreateBuilder( root, "Demo.Builder", "Demo.Host" );
            var host = CreateHost( root, "Demo.Host", builderName: null );
            var (exitCode, output) = await BuildAsync( host );
            exitCode.ShouldNotBe( 0 );
            output.ShouldContain( "'Demo.Host' must reference its builder" );

            CreateBuilder( root, "Other.Builder", "Server" );
            var server = CreateHost( root, "Server", "Other.Builder" );
            (exitCode, output) = await BuildAsync( server );
            exitCode.ShouldNotBe( 0 );
            output.ShouldContain( "doesn't end with '.Host'" );

            server = CreateHost( root, "Server", "Other.Builder", extraProperties: "<CKAppHostBuilder>Other.Builder</CKAppHostBuilder>" );
            (exitCode, output) = await BuildAsync( server );
            exitCode.ShouldBe( 0, output );
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }

    [Test]
    [CancelAfter( 120000 )]
    public async Task the_host_is_an_executable_Async()
    {
        var root = CreateRoot();
        try
        {
            CreateBuilder( root, "Demo.Builder", "Demo.Host" );
            var host = CreateHost( root, "Demo.Host", "Demo.Builder", outputType: "Library" );
            var (exitCode, output) = await BuildAsync( host );
            exitCode.ShouldNotBe( 0 );
            output.ShouldContain( "must be an executable" );
        }
        finally
        {
            TestHelper.CleanupFolder( root, ensureFolderAvailable: false );
        }
    }
}
