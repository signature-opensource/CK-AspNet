using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;

namespace CK.AppHost.Tests;

[TestFixture]
public class EnvironmentTests
{
    [TestCase( "--environment=Staging", true )]
    [TestCase( "--environment", true )]
    [TestCase( "/environment=Staging", true )]
    [TestCase( "environment=Staging", true )]
    [TestCase( "--Instance=1", false )]
    public void the_command_line_gives_the_environment_of_the_host( string arg, bool expected )
    {
        Assume.That( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "DOTNET_ENVIRONMENT" ) )
                     && string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "ASPNETCORE_ENVIRONMENT" ) ) );
        CKomposableAppHost.HasEnvironmentName( [arg] ).ShouldBe( expected );
    }

    [TestCase( "--contentRoot=C:/Somewhere", true )]
    [TestCase( "--contentRoot", true )]
    [TestCase( "--environment=Staging", false )]
    public void the_command_line_gives_the_content_root_of_the_host( string arg, bool expected )
    {
        Assume.That( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "DOTNET_CONTENTROOT" ) )
                     && string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "ASPNETCORE_CONTENTROOT" ) ) );
        CKomposableAppHost.HasContentRoot( [arg] ).ShouldBe( expected );
    }
}
