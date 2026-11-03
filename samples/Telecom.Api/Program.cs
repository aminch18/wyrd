using MinApiLib.Endpoints;
using Telecom.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTelecom(builder.Configuration);

var app = builder.Build();
app.MapEndpoints(typeof(Telecom.TelecomModule).Assembly);
app.Run();
