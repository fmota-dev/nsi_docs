using System.Text.RegularExpressions;

namespace NsiDocs.Servicos;

internal static class AnaliseTextoBusca
{
    private static readonly HashSet<string> StopwordsBase = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "as", "o", "os", "de", "da", "do", "das", "dos", "e", "em", "no", "na", "nos", "nas",
        "para", "por", "com", "como", "qual", "quais", "que", "se", "uma", "um", "ou", "ao", "aos",
        "projeto", "interno", "nsi", "senac", "rn"
    };

    public static HashSet<string> ExtrairTermos(string texto, IEnumerable<string>? termosIgnorados = null)
    {
        var ignorados = new HashSet<string>(StopwordsBase, StringComparer.OrdinalIgnoreCase);

        if (termosIgnorados is not null)
        {
            foreach (var termo in termosIgnorados.SelectMany(ExtrairTokens))
            {
                ignorados.Add(termo);
            }
        }

        return ExtrairTokens(texto)
            .Where(termo => !ignorados.Contains(termo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizarTexto(string texto)
    {
        return (texto ?? string.Empty)
            .ToLowerInvariant()
            .Replace("á", "a")
            .Replace("à", "a")
            .Replace("ã", "a")
            .Replace("â", "a")
            .Replace("é", "e")
            .Replace("ê", "e")
            .Replace("í", "i")
            .Replace("ó", "o")
            .Replace("ô", "o")
            .Replace("õ", "o")
            .Replace("ú", "u")
            .Replace("ç", "c");
    }

    private static IEnumerable<string> ExtrairTokens(string texto)
    {
        return Regex.Matches(NormalizarTexto(texto), @"[a-z0-9\-_]{2,}")
            .Select(match => match.Value);
    }
}
