using System.CommandLine;
using FluxIndex.CLI.Commands;

var rootCommand = new RootCommand("FluxIndex - RAG MCP service for intelligent document memorization and search");

// Add subcommands
rootCommand.AddCommand(InitCommand.Create());
rootCommand.AddCommand(MemorizeCommand.Create());
rootCommand.AddCommand(UnmemorizeCommand.Create());
rootCommand.AddCommand(StatusCommand.Create());
rootCommand.AddCommand(ServeCommand.Create());

return await rootCommand.InvokeAsync(args);
