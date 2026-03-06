using System;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlannerAgent.Models;
using RepairPlannerAgent.Services;

// avoid ambiguous identifier: alias the agent class
using PlannerAgent = RepairPlannerAgent.RepairPlannerAgent;

// load environment variables from .env (optional)
// this uses the DotNetEnv package, but you can also `export $(cat ../.env | xargs)`
DotNetEnv.Env.Load("/workspaces/agentic-factory-hack/.env");

// build a minimal service collection to wire dependencies
var services = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

// read environment variables required by the sample
var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? throw new InvalidOperationException("COSMOS_ENDPOINT is required");
var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY") ?? throw new InvalidOperationException("COSMOS_KEY is required");
var cosmosDatabase = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? throw new InvalidOperationException("COSMOS_DATABASE_NAME is required");

services.AddSingleton(_ => new CosmosClient(cosmosEndpoint, cosmosKey));
services.AddSingleton<CosmosDbService>(sp =>
{
    var client = sp.GetRequiredService<CosmosClient>();
    var logger = sp.GetRequiredService<ILogger<CosmosDbService>>();
    return new CosmosDbService(client, cosmosDatabase, logger);
});

services.AddSingleton<IFaultMappingService, FaultMappingService>();

// Azure AI project client
var aiEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is required");
var modelDeployment = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME") ?? throw new InvalidOperationException("MODEL_DEPLOYMENT_NAME is required");

services.AddSingleton(_ => new AIProjectClient(new Uri(aiEndpoint), new DefaultAzureCredential()));
services.AddSingleton<PlannerAgent>(sp =>
{
    var projectClient = sp.GetRequiredService<AIProjectClient>();
    var cosmosDb = sp.GetRequiredService<CosmosDbService>();
    var faultMapping = sp.GetRequiredService<IFaultMappingService>();
    var logger = sp.GetRequiredService<ILogger<PlannerAgent>>();
    return new PlannerAgent(projectClient, cosmosDb, faultMapping, modelDeployment, logger);
});

var provider = services.BuildServiceProvider();
var repairAgent = provider.GetRequiredService<PlannerAgent>();

// demonstration of workflow
Console.WriteLine("Ensuring prompt agent version is registered...");
await repairAgent.EnsureAgentVersionAsync();

var sampleFault = new DiagnosedFault
{
    MachineId = "MACH-001",
    FaultType = "curing_temperature_excessive",
    Description = "Curing press ran at too high a temperature."
};

Console.WriteLine("Planning work order for sample fault...");
var wo = await repairAgent.PlanAndCreateWorkOrderAsync(sampleFault);

Console.WriteLine("Work order created:");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(wo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

