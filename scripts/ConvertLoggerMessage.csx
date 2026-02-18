// C# script to convert _logger.Log* calls to LoggerMessage source generator pattern
// Run with: dotnet script ConvertLoggerMessage.csx

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

var sourceDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "src", "FluxIndex.Core", "Application", "Services"));
if (args.Length > 0) sourceDir = args[0];

Console.WriteLine($"Processing directory: {sourceDir}");

var files = Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories);
int totalConverted = 0;

foreach (var file in files)
{
    var content = File.ReadAllText(file);
    if (!content.Contains("_logger.Log"))
        continue;

    var result = ConvertFile(content, Path.GetFileNameWithoutExtension(file));
    if (result.Changed)
    {
        File.WriteAllText(file, result.Content);
        Console.WriteLine($"Converted {Path.GetFileName(file)}: {result.MethodCount} LoggerMessage methods added");
        totalConverted += result.MethodCount;
    }
}

Console.WriteLine($"\nTotal: {totalConverted} LoggerMessage methods created");

static (string Content, bool Changed, int MethodCount) ConvertFile(string content, string className)
{
    // Make class partial if needed
    var classRegex = new Regex(@"public\s+class\s+(" + Regex.Escape(className) + @")\s");
    if (classRegex.IsMatch(content) && !content.Contains($"public partial class {className}"))
    {
        content = classRegex.Replace(content, $"public partial class {className} ", 1);
    }

    var methods = new List<string>();
    int methodIndex = 0;
    bool changed = false;

    // Pattern to match _logger.LogXxx(... ) calls, handling multiline and nested parens
    // We'll use a more manual approach to handle nested parentheses
    var logCallStarts = new Regex(@"_logger\.(Log(?:Information|Debug|Warning|Error|Trace|Critical))\s*\(");

    var offset = 0;
    var sb = new StringBuilder();
    var match = logCallStarts.Match(content);

    while (match.Success)
    {
        var logMethod = match.Groups[1].Value;
        var startIndex = match.Index;
        var argsStart = match.Index + match.Length;

        // Find matching closing paren
        int parenDepth = 1;
        int i = argsStart;
        while (i < content.Length && parenDepth > 0)
        {
            if (content[i] == '(') parenDepth++;
            else if (content[i] == ')') parenDepth--;
            i++;
        }

        if (parenDepth != 0)
        {
            match = logCallStarts.Match(content, argsStart);
            continue;
        }

        var argsEnd = i - 1; // points to closing paren
        var argsStr = content.Substring(argsStart, argsEnd - argsStart).Trim();

        // Parse arguments
        var parsed = ParseLogArgs(logMethod, argsStr);
        if (parsed == null)
        {
            match = logCallStarts.Match(content, argsStart);
            continue;
        }

        methodIndex++;
        var methodName = $"Log{className.Replace("Service", "").Replace("Manager", "")}{methodIndex:D3}";

        // Build the replacement call
        var level = logMethod switch
        {
            "LogInformation" => "LogLevel.Information",
            "LogDebug" => "LogLevel.Debug",
            "LogWarning" => "LogLevel.Warning",
            "LogError" => "LogLevel.Error",
            "LogTrace" => "LogLevel.Trace",
            "LogCritical" => "LogLevel.Critical",
            _ => "LogLevel.Information"
        };

        // Build LoggerMessage method
        var paramList = new List<string> { "ILogger logger" };
        if (parsed.HasException) paramList.Add("Exception ex");
        paramList.AddRange(parsed.Parameters.Select(p => $"{p.Type} {p.Name}"));

        var methodDecl = $"    [LoggerMessage(Level = {level}, Message = {EscapeString(parsed.MessageTemplate)})]\n" +
                         $"    private static partial void {methodName}({string.Join(", ", paramList)});";
        methods.Add(methodDecl);

        // Build replacement call
        var callArgs = new List<string> { "_logger" };
        if (parsed.HasException) callArgs.Add(parsed.ExceptionArg);
        callArgs.AddRange(parsed.Parameters.Select(p => p.Expression));

        var replacement = $"{methodName}({string.Join(", ", callArgs)})";

        // Append content before this match, then the replacement
        sb.Append(content, offset, startIndex - offset);
        sb.Append(replacement);
        offset = argsEnd + 1; // after closing paren

        changed = true;
        match = logCallStarts.Match(content, argsEnd + 1);
    }

    if (!changed)
        return (content, false, 0);

    // Append remaining content
    sb.Append(content, offset, content.Length - offset);

    // Find the last closing brace of the class to insert LoggerMessage methods
    var result = sb.ToString();

    // Insert LoggerMessage methods before the last closing brace of the class
    // Find the class end - look for the last } that's at the class level
    var lastBrace = FindClassClosingBrace(result);
    if (lastBrace >= 0)
    {
        var methodsBlock = "\n    #region LoggerMessage Definitions\n\n" +
                          string.Join("\n\n", methods) +
                          "\n\n    #endregion\n";

        result = result.Insert(lastBrace, methodsBlock);
    }

    return (result, true, methods.Count);
}

static int FindClassClosingBrace(string content)
{
    // Find the position to insert - before the class closing brace
    // Look for the pattern: any content followed by } at class level
    // We need to track brace depth from the class declaration

    // Find "public partial class" or "public class"
    var classMatch = Regex.Match(content, @"public\s+(?:partial\s+)?class\s+\w+[^{]*\{");
    if (!classMatch.Success) return -1;

    int braceDepth = 1;
    int pos = classMatch.Index + classMatch.Length;

    while (pos < content.Length && braceDepth > 0)
    {
        if (content[pos] == '{') braceDepth++;
        else if (content[pos] == '}')
        {
            braceDepth--;
            if (braceDepth == 0)
                return pos;
        }
        pos++;
    }

    return -1;
}

class ParsedLogCall
{
    public string MessageTemplate { get; set; }
    public bool HasException { get; set; }
    public string ExceptionArg { get; set; }
    public List<(string Name, string Type, string Expression)> Parameters { get; set; } = new();
}

static ParsedLogCall ParseLogArgs(string logMethod, string argsStr)
{
    // Parse: (ex, "message {Param}", param) or ("message {Param}", param)
    var result = new ParsedLogCall();

    // Split args respecting string literals and nested expressions
    var args = SplitArgs(argsStr);
    if (args.Count == 0) return null;

    int currentArg = 0;

    // Check if first arg is an exception (for LogError/LogWarning with exception)
    if ((logMethod == "LogError" || logMethod == "LogWarning" || logMethod == "LogCritical") && args.Count >= 2)
    {
        var firstArg = args[0].Trim();
        // If first arg is not a string literal, it might be an exception
        if (!firstArg.StartsWith("\"") && !firstArg.StartsWith("$\"") && !firstArg.StartsWith("@\""))
        {
            // Check if second arg is a string literal (the message)
            var secondArg = args[1].Trim();
            if (secondArg.StartsWith("\"") || secondArg.StartsWith("$\"") || secondArg.StartsWith("@\""))
            {
                result.HasException = true;
                result.ExceptionArg = firstArg;
                currentArg = 1;
            }
        }
    }

    // Next arg should be the message template
    if (currentArg >= args.Count) return null;

    var messageArg = args[currentArg].Trim();
    currentArg++;

    // Extract message template
    string messageTemplate;
    if (messageArg.StartsWith("$\"") || messageArg.StartsWith("$@\""))
    {
        // Interpolated string - convert to template
        messageTemplate = ConvertInterpolatedToTemplate(messageArg);
    }
    else if (messageArg.StartsWith("\""))
    {
        messageTemplate = messageArg;
    }
    else if (messageArg.StartsWith("@\""))
    {
        messageTemplate = messageArg;
    }
    else
    {
        // Not a string literal - could be a concatenation or variable
        return null;
    }

    result.MessageTemplate = messageTemplate;

    // Extract template placeholders
    var placeholders = Regex.Matches(messageTemplate, @"\{(\w+)(?::[^}]*)?\}");
    var placeholderNames = placeholders.Cast<Match>().Select(m => m.Groups[1].Value).ToList();

    // Match remaining args to placeholders
    for (int i = 0; i < placeholderNames.Count && currentArg < args.Count; i++, currentArg++)
    {
        var paramName = ToCamelCase(placeholderNames[i]);
        var expression = args[currentArg].Trim();
        var type = InferType(expression);

        result.Parameters.Add((paramName, type, expression));
    }

    return result;
}

static string ConvertInterpolatedToTemplate(string interpolated)
{
    // Convert $"text {expr}" to "text {ParamName}"
    // This is a simplified conversion
    var cleaned = interpolated;
    if (cleaned.StartsWith("$@\"") || cleaned.StartsWith("@$\""))
        cleaned = cleaned.Substring(3);
    else if (cleaned.StartsWith("$\""))
        cleaned = cleaned.Substring(2);

    // Remove closing quote
    if (cleaned.EndsWith("\""))
        cleaned = cleaned.Substring(0, cleaned.Length - 1);

    // Replace {expressions} with {ParamN}
    int paramNum = 0;
    cleaned = Regex.Replace(cleaned, @"\{([^}]+)\}", m =>
    {
        paramNum++;
        var expr = m.Groups[1].Value;
        // Try to extract a meaningful name
        var name = Regex.Replace(expr, @"[^a-zA-Z0-9]", "");
        if (string.IsNullOrEmpty(name)) name = $"Param{paramNum}";
        return $"{{{name}}}";
    });

    return $"\"{cleaned}\"";
}

static List<string> SplitArgs(string argsStr)
{
    var args = new List<string>();
    int depth = 0;
    bool inString = false;
    bool inVerbatim = false;
    bool inInterpolated = false;
    var current = new StringBuilder();

    for (int i = 0; i < argsStr.Length; i++)
    {
        char c = argsStr[i];

        if (inString)
        {
            current.Append(c);
            if (c == '\\' && !inVerbatim && i + 1 < argsStr.Length)
            {
                current.Append(argsStr[++i]);
                continue;
            }
            if (c == '"')
            {
                if (inVerbatim && i + 1 < argsStr.Length && argsStr[i + 1] == '"')
                {
                    current.Append(argsStr[++i]);
                    continue;
                }
                inString = false;
                inVerbatim = false;
            }
            continue;
        }

        if (c == '"')
        {
            inString = true;
            if (i > 0 && argsStr[i - 1] == '@') inVerbatim = true;
            if (i > 0 && argsStr[i - 1] == '$') inInterpolated = true;
            current.Append(c);
            continue;
        }

        if (c == '(' || c == '[' || c == '{') depth++;
        else if (c == ')' || c == ']' || c == '}') depth--;

        if (c == ',' && depth == 0)
        {
            args.Add(current.ToString());
            current.Clear();
            continue;
        }

        current.Append(c);
    }

    if (current.Length > 0)
        args.Add(current.ToString());

    return args;
}

static string InferType(string expression)
{
    expression = expression.Trim();

    // Common patterns
    if (expression.EndsWith(".Count") || expression.EndsWith(".Length") || expression.Contains("Count()"))
        return "int";
    if (expression.EndsWith(".ElapsedMilliseconds"))
        return "long";
    if (expression.Contains("TimeSpan") || expression.EndsWith(".Elapsed"))
        return "TimeSpan";
    if (expression.StartsWith("\"") || expression.Contains(".ToString()"))
        return "string";
    if (expression.Contains(":F") || expression.Contains(".Score") || expression.Contains("Weight"))
        return "double";
    if (Regex.IsMatch(expression, @"^\d+$"))
        return "int";
    if (Regex.IsMatch(expression, @"^\d+\.\d+f?$"))
        return "float";

    // Default to object for unknown types
    return "object";
}

static string ToCamelCase(string name)
{
    if (string.IsNullOrEmpty(name)) return name;
    return char.ToLower(name[0]) + name.Substring(1);
}

static string EscapeString(string s)
{
    // Already quoted
    if (s.StartsWith("\"")) return s;
    return $"\"{s}\"";
}
