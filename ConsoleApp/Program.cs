using System.Text;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var historyFileName = "history.json";

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
#if DEBUG
    .AddUserSecrets<Program>()
#endif
    .Build();

# region Setup AppInsights
var connectionString = configuration.GetValue<string>("ApplicationInsights:ConnectionString");
var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(configuration.GetValue<string>("ServiceName")!);

// Enable model diagnostics with sensitive data.
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

using var traceProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource("Microsoft.SemanticKernel*")
    .AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString)
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter("Microsoft.SemanticKernel*", "System*")
    .AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString)
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    // Add OpenTelemetry as a logging provider
    builder.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resourceBuilder);
        options.AddAzureMonitorLogExporter(options => options.ConnectionString = connectionString);
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    });
    builder.SetMinimumLevel(LogLevel.Trace);
});
#endregion

var kernelBuilder = Kernel.CreateBuilder();
kernelBuilder.Services.AddSingleton(loggerFactory);
kernelBuilder.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
kernelBuilder.AddAzureOpenAIChatCompletion(
    configuration["AI:DelpoymentModel"]!,
    configuration["AI:Endpoint"]!,
    configuration["AI:ApiKey"]!);

var kernel = kernelBuilder.Build();
//var result = await kernel.InvokePromptAsync("What is semantic kernel?");
//Console.WriteLine(result);

var logger = kernel.GetRequiredService<ILoggerFactory>().CreateLogger<ILogger>();
var chat = kernel.GetRequiredService<IChatCompletionService>();

// read the history from a file or create new history
ChatHistory chatHistory;
var fileExists = File.Exists(historyFileName);
if (!fileExists)
{
    chatHistory = new ChatHistory();
    Console.WriteLine("AI: What is my role?");
    var aiRole = Console.ReadLine();
    chatHistory.AddSystemMessage(aiRole);
}
else
{
    var fileContent = File.ReadAllText(historyFileName);
    chatHistory = JsonSerializer.Deserialize<ChatHistory>(fileContent)!;
}

// talk to an assistant
while (true)
{
    Console.WriteLine();
    Console.WriteLine("Enter your prompt:");
    var prompt = Console.ReadLine();
    if (string.IsNullOrEmpty(prompt))
    {
        break;
    }

    chatHistory.AddUserMessage(prompt);

    var responseText = new StringBuilder();

    try
    {
        await foreach (var response in chat.GetStreamingChatMessageContentsAsync(chatHistory))
        {
            responseText.Append(response.Content);
            Console.Write(response.Content);
            //await Task.Delay(100);
        };

        logger.LogInformation($"AIResponse: {responseText.ToString()}.");
        chatHistory.AddAssistantMessage(responseText.ToString());
    }
    catch (Exception exception)
    {
        Console.WriteLine("Last prompt will not proceed due to exception.");
        logger.LogError("Last prompt will not proceed due to exception.");
        chatHistory.RemoveAt(chatHistory.Count - 1);
        Console.WriteLine($"Exception message: {exception.Message}");
    }
}

// store history to a file
var jsonHistory = JsonSerializer.Serialize(chatHistory, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(historyFileName, jsonHistory);
