using CK.Core;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppHost.Tests;

[TestFixture]
public class InstanceTests
{
    static ConfigurationManager Create( params (string Key, string Value)[] values )
    {
        var c = new ConfigurationManager();
        var d = new Dictionary<string, string?>();
        foreach( var (k, v) in values ) d[k] = v;
        c.AddInMemoryCollection( d );
        return c;
    }

    // The App name is the host assembly's without ".Host": this test assembly gives "CK.AppHost.Tests".
    static readonly System.Reflection.Assembly _host = typeof( InstanceTests ).Assembly;

    [Test]
    public void the_configuration_of_a_fleet_is_the_one_of_its_instance()
    {
        var c = Create( ("Instances:0", "Red"), ("Instances:1", "Blue"),
                        ("Instance", "blue"),
                        ("CK-AppIdentity:FullName", "Sample/$Trolley-{Name}/#Dev"),
                        ("CK-AppIdentity:Local:HttpEndpoint:Urls", "http://localhost:{5400+Index}"),
                        ("CK-AppIdentity:Parties:0:Instances:0", "Paris"),
                        ("CK-AppIdentity:Parties:0:FullName", "Shop-{Name}/$Shop-{Name}-of-{CK.AppHost.Tests.Name}"),
                        ("CK-Monitoring:GrandOutput:Handlers:TextFile:Path", "Text/{Name}") );
        CKomposableAppHost.ApplyInstance( TestHelper.Monitor, c, _host );
        c["CK-AppIdentity:FullName"].ShouldBe( "Sample/$Trolley-Blue/#Dev" );
        c["CK-AppIdentity:Local:HttpEndpoint:Urls"].ShouldBe( "http://localhost:5402" );
        c["CK-Monitoring:GrandOutput:Handlers:TextFile:Path"].ShouldBe( "Text/Blue" );
        // A tenant template: its own placeholders are resolved by CK.AppIdentity, the App's qualified ones here.
        c["CK-AppIdentity:Parties:0:FullName"].ShouldBe( "Shop-{Name}/$Shop-{Name}-of-Blue" );
    }

    [Test]
    public void the_instance_can_be_its_index()
    {
        var c = Create( ("InstanceCount", "3"), ("Instance", "2"), ("Path", "Logs/{Name}-{Index}") );
        CKomposableAppHost.ApplyInstance( TestHelper.Monitor, c, _host );
        c["Path"].ShouldBe( "Logs/2-2" );
    }

    [Test]
    public void an_Opened_fleet_accepts_a_dynamic_index()
    {
        var c = Create( ("InstanceCount", "2"), ("MaxInstanceCount", "4"), ("Instance", "3"), ("Path", "Logs/{Name}-{5400+Index}") );
        CKomposableAppHost.ApplyInstance( TestHelper.Monitor, c, _host );
        c["Path"].ShouldBe( "Logs/3-5403" );
    }

    [Test]
    public void a_host_that_is_not_a_fleet_is_unchanged()
    {
        var c = Create( ("Path", "Logs/{Name}") );
        CKomposableAppHost.ApplyInstance( TestHelper.Monitor, c, _host );
        c["Path"].ShouldBe( "Logs/{Name}" );
    }

    [TestCase( "Instance=Red", "it is not a fleet" )]
    [TestCase( "InstanceCount=2", "its instance must be specified" )]
    [TestCase( "InstanceCount=2;Instance=Green", "Unknown instance 'Green'" )]
    [TestCase( "InstanceCount=2;Instance=3", "Unknown instance '3'" )]
    [TestCase( "InstanceCount=2;MaxInstanceCount=4;Instance=5", "the dynamic instances 3 to 4" )]
    [TestCase( "MaxInstanceCount=1;Instance=Red", "It has no configured instance, and the dynamic instance 1" )]
    public void instance_errors( string config, string expected )
    {
        var values = new List<(string, string)>();
        foreach( var kv in config.Split( ';' ) )
        {
            var p = kv.Split( '=' );
            values.Add( (p[0], p[1]) );
        }
        var c = Create( values.ToArray() );
        Should.Throw<InvalidOperationException>( () => CKomposableAppHost.ApplyInstance( TestHelper.Monitor, c, _host ) )
              .Message.ShouldContain( expected );
    }
}
