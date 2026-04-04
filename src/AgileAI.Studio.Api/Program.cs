using AgileAI.Studio.Api.Contracts;
using AgileAI.Studio.Api.Data;
using AgileAI.Abstractions;
using AgileAI.DependencyInjection;
using AgileAI.Extensions.FileSystem;
using AgileAI.Extensions.FileSystem.DependencyInjection;
using AgileAI.Studio.Api.Infrastructure;
using AgileAI.Studio.Api.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

var connectionString = builder.Configuration.GetConnectionString("Studio")
    ?? "Data Source=studio.db";

builder.Services.AddDbContext<StudioDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddAgileAI();
builder.Services.AddScoped<ModelCatalogService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<ConversationService>();
builder.Services.AddScoped<AgentExecutionService>();
builder.Services.AddScoped<SkillService>();
builder.Services.AddScoped<ToolApprovalService>();
builder.Services.AddScoped<StudioToolExecutionGate>();
builder.Services.AddScoped<ProcessExecutionService>();
builder.Services.AddScoped<AgileAI.Studio.Api.Tools.RunLocalCommandTool>();
builder.Services.AddHttpClient<AgileAI.Studio.Api.Tools.WebFetchTool>();
builder.Services.AddScoped<StudioToolRegistryFactory>();
builder.Services.AddSingleton<ProviderClientFactory>();
builder.Services.AddFileSystemTools(new FileSystemToolOptions
{
    RootPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..")),
    MaxReadCharacters = 12000
});

var skillsRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "skills"));
if (Directory.Exists(skillsRoot))
{
    builder.Services.AddLocalSkills(options =>
    {
        options.RootDirectory = skillsRoot;
    });
}

builder.Services.RemoveAll<ISkillExecutor>();
builder.Services.AddSingleton<ISkillExecutor, StudioPromptSkillExecutor>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
    await StudioDbSeeder.SeedAsync(dbContext, CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapGet("/api/overview", async (ConversationService conversationService, CancellationToken cancellationToken) =>
{
    var overview = await conversationService.GetOverviewAsync(cancellationToken);
    return Results.Ok(overview);
});

app.MapGet("/api/provider-connections", async (ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    var items = await modelCatalogService.GetProviderConnectionsAsync(cancellationToken);
    return Results.Ok(items);
});

app.MapPost("/api/provider-connections", async (ProviderConnectionRequest request, ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    var item = await modelCatalogService.CreateProviderConnectionAsync(request, cancellationToken);
    return Results.Ok(item);
});

app.MapPut("/api/provider-connections/{id:guid}", async (Guid id, ProviderConnectionRequest request, ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    var item = await modelCatalogService.UpdateProviderConnectionAsync(id, request, cancellationToken);
    return Results.Ok(item);
});

app.MapDelete("/api/provider-connections/{id:guid}", async (Guid id, ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    await modelCatalogService.DeleteProviderConnectionAsync(id, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/models", async (ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    var items = await modelCatalogService.GetModelsAsync(cancellationToken);
    return Results.Ok(items);
});

app.MapPost("/api/models", async (ModelRequest request, ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    var item = await modelCatalogService.CreateModelAsync(request, cancellationToken);
    return Results.Ok(item);
});

app.MapPut("/api/models/{id:guid}", async (Guid id, ModelRequest request, ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    var item = await modelCatalogService.UpdateModelAsync(id, request, cancellationToken);
    return Results.Ok(item);
});

app.MapDelete("/api/models/{id:guid}", async (Guid id, ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    await modelCatalogService.DeleteModelAsync(id, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/api/models/{id:guid}/test", async (Guid id, ModelCatalogService modelCatalogService, CancellationToken cancellationToken) =>
{
    var result = await modelCatalogService.TestModelAsync(id, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/agents", async (AgentService agentService, CancellationToken cancellationToken) =>
{
    var items = await agentService.GetAgentsAsync(cancellationToken);
    return Results.Ok(items);
});

app.MapGet("/api/agent-tools", (AgentService agentService) =>
{
    return Results.Ok(agentService.GetAvailableTools());
});

app.MapGet("/api/skills", (SkillService skillService) =>
{
    return Results.Ok(skillService.GetLoadedSkills());
});

app.MapPost("/api/agents", async (AgentRequestDto request, AgentService agentService, CancellationToken cancellationToken) =>
{
    var item = await agentService.CreateAgentAsync(request, cancellationToken);
    return Results.Ok(item);
});

app.MapPut("/api/agents/{id:guid}", async (Guid id, AgentRequestDto request, AgentService agentService, CancellationToken cancellationToken) =>
{
    var item = await agentService.UpdateAgentAsync(id, request, cancellationToken);
    return Results.Ok(item);
});

app.MapDelete("/api/agents/{id:guid}", async (Guid id, AgentService agentService, CancellationToken cancellationToken) =>
{
    await agentService.DeleteAgentAsync(id, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/conversations", async (ConversationService conversationService, CancellationToken cancellationToken) =>
{
    var items = await conversationService.GetConversationsAsync(cancellationToken);
    return Results.Ok(items);
});

app.MapPost("/api/conversations", async (ConversationCreateRequest request, ConversationService conversationService, CancellationToken cancellationToken) =>
{
    var item = await conversationService.CreateConversationAsync(request, cancellationToken);
    return Results.Ok(item);
});

app.MapGet("/api/conversations/{id:guid}/messages", async (Guid id, ConversationService conversationService, CancellationToken cancellationToken) =>
{
    var items = await conversationService.GetMessagesAsync(id, cancellationToken);
    return Results.Ok(items);
});

app.MapPost("/api/conversations/{id:guid}/messages", async (Guid id, SendMessageRequest request, AgentExecutionService executionService, CancellationToken cancellationToken) =>
{
    var result = await executionService.SendMessageAsync(id, request.Content, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/conversations/{id:guid}/stream", async (Guid id, SendMessageRequest request, AgentExecutionService executionService, HttpContext httpContext, CancellationToken cancellationToken) =>
{
    await executionService.StreamMessageAsync(id, request.Content, httpContext.Response, cancellationToken);
    return Results.Empty;
});

app.MapGet("/api/conversations/{id:guid}/tool-approvals", async (Guid id, ToolApprovalService toolApprovalService, CancellationToken cancellationToken) =>
{
    var items = await toolApprovalService.GetToolApprovalsAsync(id, cancellationToken);
    return Results.Ok(items);
});

app.MapPost("/api/tool-approvals/{id:guid}/resolve", async (Guid id, ResolveToolApprovalRequest request, ToolApprovalService toolApprovalService, CancellationToken cancellationToken) =>
{
    var result = await toolApprovalService.ResolveApprovalAsync(id, request.Approved, request.Comment, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/tool-approvals/{id:guid}/resolve/stream", async (Guid id, ResolveToolApprovalRequest request, ToolApprovalService toolApprovalService, HttpContext httpContext, CancellationToken cancellationToken) =>
{
    await toolApprovalService.StreamApprovalResolutionAsync(id, request.Approved, request.Comment, httpContext.Response, cancellationToken);
    return Results.Empty;
});

app.Run();
