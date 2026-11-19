namespace UpdateCenter.Services;
internal static class ProviderReleaseNotes
{
    internal static ReleaseNotesInfo Read(object value, string description)
    {
        dynamic update = value;
        string text = "", url = "";
        try { text = Convert.ToString(update.ReleaseNotes) ?? ""; } catch (Exception) { }
        object? links = null, articles = null;
        try
        {
            links = update.MoreInfoUrls;
            dynamic list = links;
            for (var i = 0; i < Math.Min((int)list.Count, 16); i++)
            {
                var link = ReleaseNotesService.SafeLink(Convert.ToString(list.Item(i)));
                if (link is not null) { url = link.AbsoluteUri; break; }
            }
        }
        catch (Exception) { }
        finally { Release(links); }
        if (url.Length == 0)
        {
            try
            {
                articles = update.KBArticleIDs;
                dynamic list = articles;
                if ((int)list.Count > 0)
                {
                    string number = Convert.ToString(list.Item(0)) ?? "";
                    if (System.Text.RegularExpressions.Regex.IsMatch(number, @"^[0-9]{4,10}$")) url = "https://support.microsoft.com/help/" + number;
                }
            }
            catch (Exception) { }
            finally { Release(articles); }
        }
        return new(text.Length > 65536 ? text[..65536] : text, ReleaseNotesService.SafeLink(url)?.AbsoluteUri ?? "", description);
    }
    private static void Release(object? value)
    {
        try { if (value is not null && System.Runtime.InteropServices.Marshal.IsComObject(value)) System.Runtime.InteropServices.Marshal.ReleaseComObject(value); }
        catch (Exception) { /* Optional information must never change scan completeness. */ }
    }

}
