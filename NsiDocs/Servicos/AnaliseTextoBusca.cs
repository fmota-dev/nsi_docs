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
            .SelectMany(GerarVariantesTermo)
            .Where(termo => termo.Length >= 2 && !ignorados.Contains(termo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ExtrairFrasesSignificativas(
        string texto,
        int tamanhoMinimo = 2,
        int tamanhoMaximo = 3,
        IEnumerable<string>? termosIgnorados = null)
    {
        var ignorados = new HashSet<string>(StopwordsBase, StringComparer.OrdinalIgnoreCase);

        if (termosIgnorados is not null)
        {
            foreach (var termo in termosIgnorados.SelectMany(ExtrairTokens))
            {
                ignorados.Add(termo);
            }
        }

        var tokens = ExtrairTokens(texto)
            .Where(token => !ignorados.Contains(token))
            .ToList();

        var frases = new List<string>();
        for (var tamanho = tamanhoMinimo; tamanho <= tamanhoMaximo; tamanho++)
        {
            for (var indice = 0; indice <= tokens.Count - tamanho; indice++)
            {
                var frase = string.Join(' ', tokens.Skip(indice).Take(tamanho));
                if (!string.IsNullOrWhiteSpace(frase))
                {
                    frases.Add(frase);
                }
            }
        }

        return frases
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static IEnumerable<string> GerarVariantesTermo(string termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
        {
            yield break;
        }

        yield return termo;

        foreach (var fragmento in termo.Split(['-', '_', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!fragmento.Equals(termo, StringComparison.OrdinalIgnoreCase))
            {
                yield return fragmento;
            }
        }

        if (termo.Length >= 5 && termo.EndsWith("coes", StringComparison.OrdinalIgnoreCase))
        {
            yield return termo[..^4] + "cao";
        }

        if (termo.Length >= 5 && termo.EndsWith("oes", StringComparison.OrdinalIgnoreCase))
        {
            yield return termo[..^3] + "ao";
        }

        if (termo.Length >= 5 && termo.EndsWith("aes", StringComparison.OrdinalIgnoreCase))
        {
            yield return termo[..^3] + "ao";
        }

        if (termo.Length >= 4 && termo.EndsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            yield return termo[..^2];
        }

        if (termo.Length >= 4 && termo.EndsWith('s'))
        {
            yield return termo[..^1];
        }
    }
}
