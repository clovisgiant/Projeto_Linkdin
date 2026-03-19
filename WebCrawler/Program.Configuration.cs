using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

partial class Program
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("WEBCRAWLER_DB_CONNECTION")
        ?? throw new InvalidOperationException("Defina a variável de ambiente WEBCRAWLER_DB_CONNECTION.");

    private static string GetRequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Defina a variável de ambiente {name}.");
        }

        return value;
    }

    private static int GetOptionalIntEnv(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static bool GetOptionalBoolEnv(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetOptionalStringEnv(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim();
    }

    private static List<string> GetOptionalCsvEnvList(string name, params string[] defaultValues)
    {
        var value = Environment.GetEnvironmentVariable(name);
        IEnumerable<string> source = defaultValues;

        if (!string.IsNullOrWhiteSpace(value))
        {
            source = value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        var parsed = source
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parsed.Count == 0)
        {
            parsed.Add("PHP");
        }

        return parsed;
    }

    private static TimeSpan? GetOptionalTimeOfDayEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (int.TryParse(trimmed, out var hour) && hour >= 0 && hour <= 23)
        {
            return TimeSpan.FromHours(hour);
        }

        if (TimeSpan.TryParse(trimmed, out var parsed) && parsed >= TimeSpan.Zero)
        {
            return new TimeSpan(parsed.Hours + (parsed.Days * 24), parsed.Minutes, parsed.Seconds);
        }

        return null;
    }

    private static void LoadEnvFileIfExists()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "WebCrawler", ".env")
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var lines = File.ReadAllLines(path);
                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var line = raw.Trim();
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    var idx = line.IndexOf('=');
                    if (idx <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, idx).Trim();
                    var value = line.Substring(idx + 1).Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, value);
                    }
                }

                Console.WriteLine($"Arquivo .env carregado: {Path.GetFullPath(path)}");
                return;
            }
            catch
            {
                // Ignora erro de leitura e tenta próximo caminho.
            }
        }
    }
}
