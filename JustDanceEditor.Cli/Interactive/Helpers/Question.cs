namespace JustDanceEditor.Cli.Interactive.Helpers;

internal class Question
{
    // Version that takes in a list of options and an optional start index
    public static int Ask(ICollection<string> options, int startIndex = 0, string? question = null)
    {
        // If the list is empty, return -1
        if (options.Count == 0)
            return -1;

        // Print the question
        if (question != null)
            Console.WriteLine(question);

        // Print the options with "i) " before each option, i starting at startIndex
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"{i + startIndex})  {options.ElementAt(i)}");

        // Ask the user for an option
        return AskNumber("Please select an option", startIndex, options.Count - 1 + startIndex);
    }

    public static string AskFolder(string question, bool mustExist = false)
    {
        string requirement = mustExist ? "(This folder must already exist)" : "(This folder will be created if it doesn't exist)";
        Console.WriteLine($"{question} {requirement}");
        Console.WriteLine("You can also drag and drop the folder onto the console window and press Enter.");

        string? filepath = null;

        while (filepath == null)
        {
            Console.Write("Folder path: ");
            filepath = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(filepath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("The path cannot be empty. Please try again.");
                Console.ResetColor();
                filepath = null;
                continue;
            }

            // If the path starts with or ends with a quote, remove it
            if (filepath.StartsWith('"') && filepath.EndsWith('"'))
                filepath = filepath[1..^1];

            if (mustExist && !Directory.Exists(filepath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("The specified folder does not exist. Please check the path and try again.");
                Console.ResetColor();
                filepath = null;
                continue;
            }
        }

        return filepath;
    }

    public static string AskFolderOrIpk(string question)
    {
        Console.WriteLine($"{question} (This folder or .ipk file must already exist)");
        Console.WriteLine("You can also drag and drop the folder or IPK onto the console window and press Enter.");

        string? filepath = null;

        while (filepath == null)
        {
            Console.Write("Folder or IPK path: ");
            filepath = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(filepath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("The path cannot be empty. Please try again.");
                Console.ResetColor();
                filepath = null;
                continue;
            }

            if (filepath.StartsWith('"') && filepath.EndsWith('"'))
                filepath = filepath[1..^1];

            if (Directory.Exists(filepath))
                return filepath;

            if (string.Equals(Path.GetExtension(filepath), ".ipk", StringComparison.OrdinalIgnoreCase) && File.Exists(filepath))
                return filepath;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Please specify an existing folder or an existing .ipk file.");
            Console.ResetColor();
            filepath = null;
        }

        return filepath ?? throw new InvalidOperationException("No valid folder or IPK path was provided.");
    }

    public static string AskFile(string question, bool mustExist = false)
    {
        string requirement = mustExist ? "(This file must already exist)" : "(This file will be created if it doesn't exist)";
        Console.WriteLine($"{question} {requirement}");
        Console.WriteLine("You can also drag and drop the file onto the console window and press Enter.");

        string? filepath = null;

        while (filepath == null)
        {
            Console.Write("File path: ");
            filepath = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(filepath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("The path cannot be empty. Please try again.");
                Console.ResetColor();
                filepath = null;
                continue;
            }

            // If the path starts with or ends with a quote, remove it
            if (filepath.StartsWith('"') && filepath.EndsWith('"'))
                filepath = filepath[1..^1];

            if (mustExist && !File.Exists(filepath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("The specified file does not exist. Please check the path and try again.");
                Console.ResetColor();
                filepath = null;
                continue;
            }
        }

        return filepath;
    }

    public static int AskNumber(string question, int min = int.MinValue, int max = int.MaxValue)
    {
        Console.Write($"{question} ");
        if (min != int.MinValue && max != int.MaxValue)
        {
            Console.Write($"(between {min} and {max}): ");
        }
        else if (min != int.MinValue)
        {
            Console.Write($"(minimum {min}): ");
        }
        else if (max != int.MaxValue)
        {
            Console.Write($"(maximum {max}): ");
        }
        else
        {
            Console.Write(": ");
        }

        int? value = null;

        while (value == null)
        {
            string? numberStr = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(numberStr))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Input cannot be empty. Please enter a number.");
                Console.ResetColor();
                Console.Write("Enter number: ");
                continue;
            }

            if (!int.TryParse(numberStr, out int readNumber))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid input. Please enter a valid integer.");
                Console.ResetColor();
                Console.Write("Enter number: ");
                continue;
            }

            if (readNumber < min || readNumber > max)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"The number must be between {min} and {max}. Please try again.");
                Console.ResetColor();
                Console.Write("Enter number: ");
                continue;
            }

            value = readNumber;
        }

        return value.Value;
    }

    public static string AskForUrl(string assetName, bool canBeEmpty = false)
    {
        string canBeEmptyText = canBeEmpty ? "(Leave empty to skip)" : "";
        Console.Write($"Please enter the URL for {assetName}{canBeEmptyText}: ");
        string? url = Console.ReadLine();

        while (!string.IsNullOrEmpty(url) && !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid URL format. Please enter a valid URL (e.g., http://example.com/asset).");
            Console.ResetColor();
            Console.Write($"URL for {assetName}{canBeEmptyText}: ");
            url = Console.ReadLine();
        }

        return string.IsNullOrEmpty(url) ? "" : url;
    }

    public static bool AskYesNo(string question)
    {
        Console.Write($"{question} (y/n): ");
        string? answer = Console.ReadLine()?.Trim().ToLower();

        while (answer is not ("y" or "n" or "yes" or "no"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid input. Please answer with 'y' (yes) or 'n' (no).");
            Console.ResetColor();
            Console.Write($"{question} (y/n): ");
            answer = Console.ReadLine()?.Trim().ToLower();
        }

        return answer is "y" or "yes";
    }
}