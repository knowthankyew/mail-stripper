using MailStripper.Endpoints;
using MailStripper.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Services
builder.Services.AddHttpClient();
builder.Services.Configure<SummarizerOptions>(builder.Configuration.GetSection("Summarizer"));

builder.Services.AddSingleton<IAttachmentStore, MemoryAttachmentStore>();
builder.Services.AddSingleton<ExtractiveEmailSummarizer>();
builder.Services.AddSingleton<IEmailSummarizer, MlHybridSummarizer>();
builder.Services.AddScoped<IEmailParser, MimeKitEmailParser>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Register API Endpoints
app.MapStripEndpoints();

// Fallback to index.html for SPA UI
app.MapFallbackToFile("index.html");

app.Run();
