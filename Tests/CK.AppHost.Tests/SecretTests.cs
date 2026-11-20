using CK.Core;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppHost.Tests;

[TestFixture]
public class SecretTests
{
    static ConfigurationManager Create( params (string Key, string Value)[] values )
    {
        var c = new ConfigurationManager();
        var d = new Dictionary<string, string?>();
        foreach( var (k, v) in values ) d[k] = v;
        c.AddInMemoryCollection( d );
        return c;
    }

    [Test]
    public void a_handle_is_replaced_by_the_value_of_its_secret()
    {
        var c = Create( ("CK-AppIdentity:Local:AuthCenter:Oidc:Google:ClientSecret:$SECRET", "Google:ClientSecret"),
                        ("SqlServer:DefaultDatabaseConnectionString:$SECRET", "Db"),
                        ("Secrets:Google:ClientSecret", "g-secret"),
                        ("Secrets:Db", "Server=.;Database=Db") );
        CKomposableAppHost.ApplySecrets( TestHelper.Monitor, c );
        c["CK-AppIdentity:Local:AuthCenter:Oidc:Google:ClientSecret"].ShouldBe( "g-secret" );
        c["SqlServer:DefaultDatabaseConnectionString"].ShouldBe( "Server=.;Database=Db" );
    }

    [Test]
    public void the_missing_secrets_are_all_reported()
    {
        var c = Create( ("A:$SECRET", "One"), ("B:$SECRET", "Two"), ("C:$SECRET", "one"), ("Secrets:Three", "3") );
        Util.Invokable( () => CKomposableAppHost.ApplySecrets( TestHelper.Monitor, c ) )
            .ShouldThrow<InvalidOperationException>()
            .Message.ShouldStartWith( "Missing secret(s) 'One', 'Two':" );
    }

    [Test]
    public void a_missing_optional_secret_leaves_its_path_absent()
    {
        var c = Create( ("A:$SECRET?", "One"), ("B:$SECRET?", "Two"), ("C:$SECRET", "Three"), ("Secrets:Two", "2"), ("Secrets:Three", "3") );
        CKomposableAppHost.ApplySecrets( TestHelper.Monitor, c );
        c["A"].ShouldBeNull();
        c["B"].ShouldBe( "2" );
        c["C"].ShouldBe( "3" );
    }

    [Test]
    public async Task a_failure_of_RunAsync_is_an_exit_code_not_an_unhandled_exception_Async()
    {
        // The entry assembly of a test (the test runner) has no generated code: the host can't be built.
        var previous = Environment.ExitCode;
        try
        {
            await CKomposableAppHost.RunAsync( [] );
            Environment.ExitCode.ShouldBe( 1 );
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Test]
    public void an_invalid_name_throws()
    {
        var c = Create( ("A:$SECRET", "Bad Name") );
        Util.Invokable( () => CKomposableAppHost.ApplySecrets( TestHelper.Monitor, c ) )
            .ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain( "Invalid secret name 'Bad Name'" );
    }

    [Test]
    public void a_secrets_file_is_the_Secrets_section()
    {
        var folder = Path.Combine( Path.GetTempPath(), "CK.AppHost.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( folder );
        var file = Path.Combine( folder, ConfigurationSecret.DevFileName );
        File.WriteAllText( file, """{ "Google": { "ClientSecret": "g-secret" } }""" );
        var c = Create( ("X:$SECRET", "Google:ClientSecret") );
        CKomposableAppHost.AddSecretsFile( TestHelper.Monitor, c, file );
        c["Secrets:Google:ClientSecret"].ShouldBe( "g-secret" );
        CKomposableAppHost.ApplySecrets( TestHelper.Monitor, c );
        c["X"].ShouldBe( "g-secret" );

        // A missing file adds nothing.
        CKomposableAppHost.AddSecretsFile( TestHelper.Monitor, c, Path.Combine( folder, "None.json" ) );
    }
}
