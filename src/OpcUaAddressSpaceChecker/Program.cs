using System.CommandLine;
using OpcUaAddressSpaceChecker.Commands;

var command = new CheckCommand();
return await command.Parse(args).InvokeAsync();

