using Amazon.BedrockRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;
using TemporalioSamples.Bedrock.SignalsAndQueries;

// Create a client to localhost on default namespace
var client = await TemporalClient.ConnectAsync(new("localhost:7233")
{
    LoggerFactory = LoggerFactory.Create(builder =>
        builder.
            AddSimpleConsole(options => options.TimestampFormat = "[HH:mm:ss] ").
            SetMinimumLevel(LogLevel.Information)),
});

async Task RunWorkerAsync()
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.
        SetMinimumLevel(LogLevel.Information).
        AddSimpleConsole(options => options.SingleLine = true);

    builder.Services.AddSingleton<IAmazonBedrockRuntime>(_ => new AmazonBedrockRuntimeClient());
    builder.Services.AddSingleton<IBedrockActivities, BedrockActivities>();

    builder.Services.
        AddHostedTemporalWorker(clientTargetHost: "localhost:7233", clientNamespace: "default", taskQueue: "bedrock-task-queue").
        AddSingletonActivities<IBedrockActivities>().
        AddWorkflow<BedrockWorkflow>();

    var app = builder.Build();
    await app.RunAsync();
}

async Task SendMessageAsync()
{
    var prompt = args.ElementAtOrDefault(1);
    if (prompt is null)
    {
        Console.WriteLine("Usage: dotnet run send-message '<prompt>'");
        Console.WriteLine("Example: dotnet run send-message 'What animals are marsupials?'");
        return;
    }

    var workflowId = "bedrock-workflow-with-signals";
    var inactivityTimeoutMinutes = 1;

    // Sends a signal to the workflow (and starts it if needed)
    var workflowOptions = new WorkflowOptions(workflowId, "bedrock-task-queue");
    workflowOptions.SignalWithStart((BedrockWorkflow workflow) => workflow.UserPromptAsync(new(prompt)));
    await client.StartWorkflowAsync((BedrockWorkflow workflow) => workflow.RunAsync(new(inactivityTimeoutMinutes)), workflowOptions);
}

async Task GetHistoryAsync()
{
    var workflowId = "bedrock-workflow-with-signals";
    var handle = client.GetWorkflowHandle<BedrockWorkflow>(workflowId);

    // Queries the workflow for the conversation history
    var history = await handle.QueryAsync(workflow => workflow.ConversationHistory);

    Console.WriteLine("Conversation History:");
    foreach (var entry in history)
    {
        Console.WriteLine($"{entry.Speaker}: {entry.Message}");
    }

    // Queries the workflow for the conversation summary
    var summary = await handle.QueryAsync(workflow => workflow.ConversationSummary);
    if (summary is not null)
    {
        Console.WriteLine("Conversation Summary:");
        Console.WriteLine(summary);
    }
}

switch (args.ElementAtOrDefault(0))
{
    case "worker":
        await RunWorkerAsync();
        break;
    case "send-message":
        await SendMessageAsync();
        break;
    case "get-history":
        await GetHistoryAsync();
        break;
    default:
        throw new ArgumentException("Must pass 'worker' or 'send-message' as the single argument");
}