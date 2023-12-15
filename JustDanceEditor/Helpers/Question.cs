﻿namespace JustDanceEditor.Helpers;
internal class Question
{
    // Version that takes in a list of options and an optional start index
    public static int Ask(List<string> options, int startIndex = 0, string? question = null)
    {
        // If the list is empty, return -1
        if (options.Count == 0)
            return -1;

        // Print the question
        if (question != null)
            Console.WriteLine(question);

        // Print the options with "i) " before each option, i starting at startIndex
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"{i + startIndex})  {options[i]}");

        // Ask the user for an option
        Console.Write("Enter the number of the option you want to use: ");
        string? option = Console.ReadLine();

        // If the option is not a number, ask again
        if (!uint.TryParse(option, out uint value))
        {
            Console.WriteLine("The option is not a number.");
            return Ask(options, startIndex, question);
        }

        // If the option is not in the valid range, ask again
        if (value < startIndex || value > options.Count + startIndex - 1)
        {
            Console.WriteLine("The option is not valid.");
            return Ask(options, startIndex, question);
        }

        return (int)value;
    }

    public static string AskFolder(string question, bool mustExist = false)
    {
        string? filepath = null;

        while (filepath == null)
        {
            Console.Write(question);
            filepath = Console.ReadLine()!;

            // Trim the filepath
            filepath = filepath.Trim();

            if (string.IsNullOrWhiteSpace(filepath))
            {
                Console.WriteLine("The path is empty.");
                filepath = null;
                continue;
            }

            // If the path starts with or ends with a quote, remove it
            if (filepath.StartsWith('"') && filepath.EndsWith('"'))
                filepath = filepath[1..^1];
            
            if (mustExist && !Directory.Exists(filepath))
            {
                Console.WriteLine("The path does not exist.");
                filepath = null;
                continue;
            }
        }

        return filepath;
    }

    public static string AskFile(string question, bool mustExist = false)
    {
        string? filepath = null;

        while (filepath == null)
        {
            Console.Write(question);
            filepath = Console.ReadLine()!;

            // Trim the filepath
            filepath = filepath.Trim();

            if (string.IsNullOrWhiteSpace(filepath))
            {
                Console.WriteLine("The path is empty.");
                filepath = null;
                continue;
            }

            // If the path starts with or ends with a quote, remove it
            if (filepath.StartsWith('"') && filepath.EndsWith('"'))
                filepath = filepath[1..^1];
            
            if (mustExist && !File.Exists(filepath))
            {
                Console.WriteLine("The path does not exist.");
                filepath = null;
                continue;
            }
        }

        return filepath;
    }
}
