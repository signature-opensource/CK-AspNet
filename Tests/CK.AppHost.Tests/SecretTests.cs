using CK.Core;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
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

    static string NewSecretsFolder( string name )
    {
        var folder = Path.Combine( TestHelper.TestProjectFolder, "Obj", "Secrets", name );
        if( Directory.Exists( folder ) ) Directory.Delete( folder, recursive: true );
        Directory.CreateDirectory( folder );
        return folder;
    }

    [Test]
    public void a_folder_of_secrets_gives_one_secret_per_file()
    {
        var folder = NewSecretsFolder( "OnePerFile" );
        File.WriteAllText( Path.Combine( folder, "KeyRing" ), "ring-key\n" );
        File.WriteAllText( Path.Combine( folder, "Enlist__Worker2" ), "ckt1.ticket" );
        File.WriteAllText( Path.Combine( folder, "Crlf" ), "two lines\r\nkept\r\n" );
        // A Kubernetes secret volume: "..data" and the timestamped folder are ignored, so is a hidden file.
        Directory.CreateDirectory( Path.Combine( folder, "..data" ) );
        File.WriteAllText( Path.Combine( folder, ".hidden" ), "ignored" );
        var c = Create( ("Local:KeyManagement:KeyRingKey:$SECRET", "KeyRing"),
                        ("Local:KeyManagement:EnlistTicket:$SECRET?", "Enlist:Worker2"),
                        ("Other:$SECRET", "Crlf") );
        CKomposableAppHost.AddSecretsDirectories( TestHelper.Monitor, c, v => v == CKomposableAppHost.SecretsDirectoryVariable ? folder : null );
        CKomposableAppHost.ApplySecrets( TestHelper.Monitor, c );
        c["Local:KeyManagement:KeyRingKey"].ShouldBe( "ring-key", "One final line break is removed." );
        c["Local:KeyManagement:EnlistTicket"].ShouldBe( "ckt1.ticket", "\"__\" separates the segments of the name." );
        c["Other"].ShouldBe( "two lines\r\nkept" );
        c["Secrets:.hidden"].ShouldBeNull();
    }

    [Test]
    public void the_secrets_folders_win_over_the_environment_and_the_second_folder_wins()
    {
        var credentials = NewSecretsFolder( "Credentials" );
        var volume = NewSecretsFolder( "Volume" );
        File.WriteAllText( Path.Combine( credentials, "A" ), "from-credentials" );
        File.WriteAllText( Path.Combine( credentials, "B" ), "from-credentials" );
        File.WriteAllText( Path.Combine( volume, "B" ), "from-volume" );
        // The environment variables are a source added before (Secrets__A).
        var c = Create( ("Secrets:A", "from-environment"), ("Secrets:C", "from-environment") );
        CKomposableAppHost.AddSecretsDirectories( TestHelper.Monitor, c, v => v switch
        {
            CKomposableAppHost.CredentialsDirectoryVariable => credentials,
            CKomposableAppHost.SecretsDirectoryVariable => volume,
            _ => null
        } );
        c["Secrets:A"].ShouldBe( "from-credentials" );
        c["Secrets:B"].ShouldBe( "from-volume" );
        c["Secrets:C"].ShouldBe( "from-environment" );
    }

    [Test]
    public void an_invalid_secrets_folder_throws()
    {
        var c = new ConfigurationManager();
        var missing = Path.Combine( TestHelper.TestProjectFolder, "Obj", "Secrets", "Missing" );
        Util.Invokable( () => CKomposableAppHost.AddSecretsDirectory( TestHelper.Monitor, c, missing, "test" ) )
            .ShouldThrow<InvalidOperationException>().Message.ShouldContain( "doesn't exist" );

        var empty = NewSecretsFolder( "EmptyValue" );
        File.WriteAllText( Path.Combine( empty, "A" ), "\n" );
        Util.Invokable( () => CKomposableAppHost.AddSecretsDirectory( TestHelper.Monitor, c, empty, "test" ) )
            .ShouldThrow<InvalidOperationException>().Message.ShouldContain( "is empty" );

        var badName = NewSecretsFolder( "BadName" );
        File.WriteAllText( Path.Combine( badName, "Bad Name" ), "x" );
        Util.Invokable( () => CKomposableAppHost.AddSecretsDirectory( TestHelper.Monitor, c, badName, "test" ) )
            .ShouldThrow<InvalidOperationException>().Message.ShouldContain( "Invalid secret file 'Bad Name'" );
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
