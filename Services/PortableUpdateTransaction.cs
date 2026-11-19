using System.Security.Cryptography;

namespace UpdateCenter.Services;

internal static class PortableUpdateTransaction
{
    internal static void Apply(string target, string staged, string backup, byte[] expectedOriginal,
        byte[] expectedCandidate, Action confirmStartup)
    {
        // Never discard a backup from an interrupted attempt.
        Move(target, backup, overwrite: false);
        var replaced = false;
        try
        {
            using (var original = File.OpenRead(backup))
                if (!expectedOriginal.AsSpan().SequenceEqual(SHA256.HashData(original)))
                    throw new IOException("L'eseguibile originale è cambiato dopo la verifica.");
            Move(staged, target, overwrite: false);
            replaced = true;
            confirmStartup();
        }
        catch (Exception startupError) when (startupError is not UpdateProcessStillRunningException)
        {
            try
            {
                if (replaced && File.Exists(target))
                {
                    using var current = File.OpenRead(target);
                    if (!expectedCandidate.AsSpan().SequenceEqual(SHA256.HashData(current)))
                        throw new IOException("La destinazione è cambiata. Il backup è stato conservato.");
                }
                Move(backup, target, overwrite: replaced);
            }
            catch (Exception restoreError)
            {
                throw new IOException("Impossibile ripristinare automaticamente il backup dell'aggiornamento.",
                    new AggregateException(startupError, restoreError));
            }
            throw;
        }

        try { File.Delete(backup); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogService.Write("Backup dell'aggiornamento conservato dopo il riavvio confermato.", ex);
        }
    }

    private static void Move(string source, string destination, bool overwrite)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Thread.Sleep(400);
            }
        }
    }
}

internal sealed class UpdateProcessStillRunningException(Exception inner)
    : IOException("La nuova istanza è ancora attiva. Il backup è stato conservato.", inner);
