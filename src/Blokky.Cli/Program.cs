using Blokky.Cli;

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
using var stderr = Console.OpenStandardError();
return CliApp.Run(args, stdin, stdout, stderr);
