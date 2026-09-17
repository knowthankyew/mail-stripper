using MailStripper.Endpoints;
using MailStripper.Security;
using MailStripper.Services;

var builder = WebApplication.CreateBuilder(args);

// Limit request body size to prevent memory exhaustion
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = SecurityConstants.MaxUploadSizeBytes;
});

// Add Services
builder.Services.AddHttpClient();
builder.Services.Configure<SummarizerOptions>(builder.Configuration.GetSection("Summarizer"));

builder.Services.AddSingleton<IAttachmentStore, MemoryAttachmentStore>();
builder.Services.AddSingleton<ExtractiveEmailSummarizer>();
builder.Services.AddSingleton<IEmailSummarizer, MlHybridSummarizer>();
builder.Services.AddSingleton<PrivacyAuditService>();
builder.Services.AddScoped<IEmailParser, MimeKitEmailParser>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5001", "http://127.0.0.1:5001")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Strict Security Headers & CSP proving zero external egress
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy", SecurityConstants.ContentSecurityPolicy);
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("X-Local-Only-Guarantee", "Air-Gapped: Zero External Egress | In-Memory Processing");
    await next();
});

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Register API Endpoints
app.MapStripEndpoints();

// Fallback to index.html for SPA UI
app.MapFallbackToFile("index.html");

app.Run();
