using System.CommandLine;
using FluxIndex.CLI.Commands;

var rootCommand = new RootCommand("FluxIndex - RAG MCP service for intelligent document memorization and search");

// Add subcommands
rootCommand.Add(InitCommand.Create());
rootCommand.Add(ConfigCommand.Create());
rootCommand.Add(MemorizeCommand.Create());
rootCommand.Add(UnmemorizeCommand.Create());
rootCommand.Add(StatusCommand.Create());
rootCommand.Add(ServeCommand.Create());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
