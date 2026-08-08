using System.Text.RegularExpressions;

namespace BioGuard.Api.Services;

public static class InputSanitizer
{
    // Remueve etiquetas HTML (incluidas las malformadas y de autocierre), decodifica
    // entidades residuales y colapsa el espacio en blanco sobrante.
    public static string StripHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;
        var withoutTags = Regex.Replace(input, "<[^>]*>", string.Empty);
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"[ \t]{2,}", " ").Trim();
    }
}
