using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder( args );
builder.UseCKMonitoring();
builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
builder.Services.AddScoped<IParallelLogger>( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );

var app = builder.CKBuild();

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
