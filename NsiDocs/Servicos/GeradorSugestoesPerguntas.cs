using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NsiDocs.Modelos;

namespace NsiDocs.Servicos;

internal sealed class GeradorSugestoesPerguntas
{
    public IReadOnlyList<string> Gerar(string perguntaOriginal, IReadOnlyList<SecaoDocumento> secoesUtilizadas, int quantidade = 4)
    {
        if (string.IsNullOrWhiteSpace(perguntaOriginal) || secoesUtilizadas.Count == 0 || quantidade <= 0)
        {
            return [];
        }

        var perguntaNormalizada = Normalizar(perguntaOriginal);
        var sugestoes = new List<string>();

        foreach (var secao in secoesUtilizadas)
        {
            foreach (var sugestao in GerarSugestoesDaSecao(secao))
            {
                var sugestaoNormalizada = Normalizar(sugestao);
                if (sugestaoNormalizada == perguntaNormalizada ||
                    sugestoes.Any(item => Normalizar(item) == sugestaoNormalizada))
                {
                    continue;
                }

                sugestoes.Add(sugestao);
                if (sugestoes.Count >= quantidade)
                {
                    return sugestoes;
                }
            }
        }

        return sugestoes;
    }

    private static IEnumerable<string> GerarSugestoesDaSecao(SecaoDocumento secao)
    {
        var titulo = secao.Titulo.Trim();
        var projeto = secao.Projeto.Trim();
        var partes = titulo.Split(" > ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var topicoFinal = partes.LastOrDefault() ?? titulo;
        var topicoRaiz = partes.FirstOrDefault() ?? titulo;
        var tituloNormalizado = Normalizar(titulo);

        yield return $"No projeto {projeto}, detalhe melhor a seção \"{titulo}\".";

        if (tituloNormalizado.Contains("stack") || tituloNormalizado.Contains("tecnolog"))
        {
            yield return $"No projeto {projeto}, quais tecnologias aparecem em \"{titulo}\"?";
            yield return $"No projeto {projeto}, explique os pontos mais importantes de \"{topicoFinal}\" na stack atual.";
            yield break;
        }

        if (tituloNormalizado.Contains("integrac") || tituloNormalizado.Contains("api") || tituloNormalizado.Contains("servico"))
        {
            yield return $"No projeto {projeto}, quais integrações aparecem em \"{titulo}\"?";
            yield return $"No projeto {projeto}, descreva o fluxo principal relacionado a \"{topicoFinal}\".";
            yield break;
        }

        if (tituloNormalizado.Contains("segur") || tituloNormalizado.Contains("autentic") || tituloNormalizado.Contains("permiss") || tituloNormalizado.Contains("acesso"))
        {
            yield return $"No projeto {projeto}, como funciona \"{titulo}\"?";
            yield return $"No projeto {projeto}, quais regras, perfis ou controles aparecem em \"{topicoFinal}\"?";
            yield break;
        }

        if (tituloNormalizado.Contains("banco") || tituloNormalizado.Contains("dados") || tituloNormalizado.Contains("tabela"))
        {
            yield return $"No projeto {projeto}, quais estruturas de dados, tabelas ou entidades aparecem em \"{titulo}\"?";
            yield return $"No projeto {projeto}, explique os pontos técnicos de banco relacionados a \"{topicoFinal}\".";
            yield break;
        }

        if (tituloNormalizado.Contains("configur") || tituloNormalizado.Contains("ambiente") || tituloNormalizado.Contains("deploy"))
        {
            yield return $"No projeto {projeto}, quais configurações importantes aparecem em \"{titulo}\"?";
            yield return $"No projeto {projeto}, quais cuidados de ambiente ou implantação aparecem em \"{topicoFinal}\"?";
            yield break;
        }

        yield return $"No projeto {projeto}, quais são os pontos principais de \"{titulo}\"?";

        if (!string.Equals(topicoFinal, topicoRaiz, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"No projeto {projeto}, explique com foco técnico o tópico \"{topicoFinal}\".";
        }
    }

    private static string Normalizar(string texto)
    {
        var semAcento = texto
            .Normalize(NormalizationForm.FormD)
            .Where(caractere => CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return Regex.Replace(new string(semAcento).ToLowerInvariant(), @"\s+", " ").Trim();
    }
}
