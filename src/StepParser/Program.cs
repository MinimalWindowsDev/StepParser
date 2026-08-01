using StepParser.Cli;
using StepParser.Logging;

namespace StepParser;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return StepParserCli.Run(args, Console.In, Console.Out, Console.Error);
        }
        finally
        {
            // Serilog's file sink batches writes; without this the last lines of a --log run can be
            // lost when the process exits.
            StepLogger.CloseAndFlush();
        }
    }
}
