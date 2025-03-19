using BigRedProf.Data.Core;
using BigRedProf.Stories.Api.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// my stuff
StartUpHelper.AddCorsService(builder.Services);
StartUpHelper.ConfigureKestrel(builder.Services);
StartUpHelper.InjectDependencies(builder.Services);
StartUpHelper.AddSignalRService(builder.Services);

var app = builder.Build();

// my stuff
StartUpHelper.UseResponseCompressionForSignalR(app);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

// my stuff
StartUpHelper.UseCors(app);
StartUpHelper.ConfigureSignalR(app);

app.Run();
