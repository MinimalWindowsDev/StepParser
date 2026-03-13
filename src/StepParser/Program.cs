using StepParser.Cli;

namespace StepParser;

internal static class Program
{
    public static int Main(string[] args)
    {
        return StepParserCli.Run(args, Console.Out, Console.Error);
    }
}
