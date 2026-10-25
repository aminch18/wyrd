using MinApiLib.Endpoints;
using Telecom.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTelecom(builder.Configuration);

var app = builder.Build();
app.MapEndpoints();
app.Run();
