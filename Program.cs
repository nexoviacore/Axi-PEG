using Serilog;
using AxExtend;
using AxExtend.Interface;
using AxPeg.Exceptions;
using AxPeg.Lib;
using AxPeg.Lib.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddScoped<IAxExtend, AxExtend.AxExtend>();
builder.Services.AddScoped<IRedisCacheService, RedisCacheService>();
builder.Services.AddScoped<IRabbitMQPublisher, RabbitMQPublisher>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<AxPeg.Repositories.Interfaces.IStoreDataRepository, AxPeg.Repositories.StoreDataRepository>();
builder.Services.AddScoped<AxPeg.Services.Interfaces.IAxPegService, AxPeg.Services.AxPegService>();
builder.Services.AddScoped<AxPeg.Services.Interfaces.IAxPegActionsService, AxPeg.Services.AxPegActionsService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

try
{
    Log.Information("Starting Web Host...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
