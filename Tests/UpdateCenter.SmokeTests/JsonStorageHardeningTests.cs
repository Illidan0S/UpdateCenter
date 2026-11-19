using System.Text;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class JsonStorageHardeningTests
{
    internal static void Run()
    {
        var waited = TimeSpan.Zero;
        var attempts = 0;
        var status = JsonStorage.Read<UpdateRunStatus>("status.json", _ =>
        {
            if (waited < TimeSpan.FromMilliseconds(400))
                throw ++attempts % 2 == 0
                    ? new IOException("File is being replaced", unchecked((int)0x80070020))
                    : new FileNotFoundException();
            return Json("""{"CurrentIndex":42}""");
        }, delay => waited += delay);
        if (status?.CurrentIndex != 42)
            throw new InvalidOperationException("Read lost a status that became available after 400 ms.");

        waited = TimeSpan.Zero;
        var missing = JsonStorage.Read<UpdateRunStatus>("status.json",
            _ => throw new FileNotFoundException(), delay => waited += delay);
        if (missing is not null || waited.TotalMilliseconds is < 500 or > 1000)
            throw new InvalidOperationException("Missing status must expire within the bounded read window.");

        foreach (Func<string, Stream> open in new Func<string, Stream>[]
                 { _ => Json("{"), _ => throw new UnauthorizedAccessException() })
        {
            waited = TimeSpan.Zero;
            var invalid = JsonStorage.Read<UpdateRunStatus>("status.json", open, delay => waited += delay);
            if (invalid is not null || waited != TimeSpan.Zero)
                throw new InvalidOperationException("Malformed or inaccessible status must fail without a retry or cached success.");
        }
    }

    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));
}
