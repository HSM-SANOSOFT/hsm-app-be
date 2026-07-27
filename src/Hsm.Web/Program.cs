using Hsm.Application.System;
using Hsm.Contracts.Ui;
using Hsm.Infrastructure;
using Hsm.Web.Components;
using Hsm.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Store-port adapters (EF Core/Npgsql, S3, Meilisearch, Redis cache) bound
// from configuration (plan U9).
builder.Services.AddHsmInfrastructure(builder.Configuration);

// Application handlers.
builder.Services.AddScoped<GetSystemStatusHandler>();

// UI services: interfaces declared in Hsm.Contracts, implemented by this
// host with in-process handler calls (client-isolation boundary, plan U8).
builder.Services.AddScoped<ISystemStatusUiService, SystemStatusUiService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposes the entry point to WebApplicationFactory-based tests.
public partial class Program { }
