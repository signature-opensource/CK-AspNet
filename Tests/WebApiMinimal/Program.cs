var builder = WebApplication.CreateBuilder( args );

// Didn't find a way to detect the buplicated call :-(
// The IApplicationbuilder.Properties and IHostBuilder.Properties are two
// different dictionaries...


builder.UseScopedHttpContext();
builder.WebHost.UseScopedHttpContext();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet( "/weatherforecast", () =>
{
    var forecast = Enumerable.Range( 1, 5 ).Select( index =>
        new WeatherForecast
        (
            DateTime.Now.AddDays( index ),
            Random.Shared.Next( -20, 55 ),
            summaries[Random.Shared.Next( summaries.Length )]
        ) )
        .ToArray();
    return forecast;
} );

app.Run();

record WeatherForecast( DateTime Date, int TemperatureC, string? Summary )
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
