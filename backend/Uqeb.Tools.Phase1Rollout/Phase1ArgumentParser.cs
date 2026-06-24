namespace Uqeb.Tools.Phase1Rollout;

internal static class Phase1ArgumentParser
{
    internal static Phase1Arguments Parse(string[] args)
    {
        var queue = new Queue<string>(args);
        var result = new Phase1Arguments();

        while (queue.TryDequeue(out var argument))
        {
            switch (argument)
            {
                case "--admin-username":
                    result.AdminUsername = ReadRequiredValue(queue, argument);
                    break;
                case "--settings-path":
                    result.SettingsPath = ReadRequiredValue(queue, argument);
                    break;
                case "--verbose":
                    result.Verbose = true;
                    break;
                case "--help":
                case "-h":
                    result.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (string.IsNullOrWhiteSpace(result.SettingsPath))
            result.ShowHelp = true;

        return result;
    }

    private static string ReadRequiredValue(Queue<string> queue, string argument)
    {
        if (!queue.TryDequeue(out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing value for {argument}.");

        return value;
    }
}
