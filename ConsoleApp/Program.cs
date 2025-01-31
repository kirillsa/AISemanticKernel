using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

var historyFileName = "history.json";

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var kernelBuilder = Kernel.CreateBuilder();
kernelBuilder.AddAzureOpenAIChatCompletion(
    configuration["AI:DelpoymentModel"]!,
    configuration["AI:Endpoint"]!,
    configuration["AI:ApiKey"]!);

var kernel = kernelBuilder.Build();

//var result = await kernel.InvokePromptAsync("What is semantic kernel?");
//Console.WriteLine(result);

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
    Console.WriteLine("How can I help you?");
    var prompt = Console.ReadLine();
    if (string.IsNullOrEmpty(prompt))
    {
        break;
    }

    chatHistory.AddUserMessage(prompt);

    var responseText = new StringBuilder();
    await foreach (var response in chat.GetStreamingChatMessageContentsAsync(chatHistory))
    {
        responseText.Append(response.Content);
        Console.Write(response.Content);
        //await Task.Delay(100);
    };

    chatHistory.AddAssistantMessage(responseText.ToString());
}

// store history to a file
var jsonHistory = JsonSerializer.Serialize(chatHistory, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(historyFileName, jsonHistory);
