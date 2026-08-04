namespace UpdateCenter.Core;

public static class UserMessageFormatter
{
    public static string FromException(Exception exception)
    {
        var message = exception.GetBaseException().Message?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(message)) return "errore non specificato";

        if (message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("operation was cancelled", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("task was canceled", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("task was cancelled", StringComparison.OrdinalIgnoreCase))
            return "tempo di attesa scaduto";
        if (message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return "il componente di rete non risponde";
        if (message.Contains("network path was not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("host is unreachable", StringComparison.OrdinalIgnoreCase))
            return "dispositivo non raggiungibile sulla rete locale";

        var separator = message.IndexOf(": ", StringComparison.Ordinal);
        if (separator is > 0 and < 48 && message[..separator].All(character =>
                char.IsLetterOrDigit(character) || character is '_' or '-'))
            return message[(separator + 2)..];
        return message;
    }
}
