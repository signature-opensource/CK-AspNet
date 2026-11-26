using CK.Core;
using CK.Setup;
using CK.Testing;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppHost.Tests;

[TestFixture]
public class AppHostRunnerTests
{
    public sealed class FakeRunner : IAppHostRunner
    {
        public int Run( IHost host ) => 42;
    }

    public sealed class OtherRunner : IAppHostRunner
    {
        public int Run( IHost host ) => 3712;
    }

    public sealed class NotARunner : IRealObject
    {
    }

    [Test]
    public async Task the_runner_is_found_in_the_generated_code_before_the_host_is_built_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( FakeRunner ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var runner = CKomposableAppHost.FindRunner( map );
        runner.ShouldBeOfType<FakeRunner>();
        runner.Run( null! ).ShouldBe( 42 );
    }

    [Test]
    public async Task without_runner_the_host_runs_itself_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        // A type set without types would discover the whole folder (and its two runners).
        configuration.FirstBinPath.Types.Add( typeof( NotARunner ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        CKomposableAppHost.FindRunner( map ).ShouldBeNull();
    }

    [Test]
    public async Task an_App_has_at_most_one_runner_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( FakeRunner ), typeof( OtherRunner ) );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            var r = await configuration.RunAsync();
            r.Status.ShouldBe( RunStatus.Failed );
            texts.ShouldContain( t => t.Contains( nameof( FakeRunner ) ) && t.Contains( nameof( OtherRunner ) ), string.Join( System.Environment.NewLine, texts ) );
        }
    }

    [Test]
    public void a_failure_of_Run_is_an_exit_code_not_an_unhandled_exception()
    {
        // The entry assembly of a test (the test runner) has no generated code: the host can't be built.
        CKomposableAppHost.Run( [] ).ShouldBe( 1 );
    }
}
