using CK.Core;
using CK.Monitoring;

// This can be done but using 
//LogFile.RootLogPath = Path.GetFullPath( "Logs" );
//GrandOutput.EnsureActiveDefault();

var builder = WebApplication.CreateBuilder( args );

builder.Host.UseCKMonitoring();
builder.AddScopedHttpContext();

//try
//{
//    builder.WebHost.UseScopedHttpContext();
//    throw new InvalidOperationException( "NO WAY!" );
//}
//catch( CKException ex )
//{
//    if( ex.Message != "When WebApplicationBuilder is used, the UseScopedHttpContext() must be called directly on the WebApplicationBuilder." )
//    {
//        throw new InvalidOperationException("NO WAY!");
//    }
//}

var app = builder.Build();

app.UseScopedHttpContext();
app.UseGuardRequestMonitor();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.Use( async ( context, next ) =>
{
    var monitor = context.RequestServices.GetService<IActivityMonitor>();
    monitor?.Trace( $"My request: {context.Request.QueryString}" );
    Console.WriteLine( $"Request: {context.Request.QueryString} - {(monitor != null ? "MONITOR" : "NO MONITOR")}" );
    await next();
} );
app.MapGet( "/w", () =>
{
    var forecast = Enumerable.Range( 1, 5 ).Select( index =>
        new WeatherForecast
        (
            DateTime.Now.AddDays( index ),
            Random.Shared.Next( -20, 55 ),
            summaries[Random.Shared.Next( summaries.Length )]
        ) )
        .ToArray();
    return Task.FromResult( forecast );
} );
app.MapGet( "/throw", () => Throw.CKException<int>( "You asked me to throw." ) );

app.Run();

record WeatherForecast( DateTime Date, int TemperatureC, string? Summary )
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
