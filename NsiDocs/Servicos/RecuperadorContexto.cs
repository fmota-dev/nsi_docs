using System.Text;
using System.Text.RegularExpressions;
using NsiDocs.Modelos;

namespace NsiDocs.Servicos;

internal sealed class RecuperadorContexto
{
    public List<SecaoDocumento> RecuperarSecoes(
        List<ProjetoDocumentacao> projetos,
        PlanoConsulta planoConsulta,
        string pergunta,
        int quantidade)
    {
        var secoes = projetos.SelectMany(projeto => projeto.Secoes).ToList();
        var termos = ExtrairTermos($"{pergunta} {string.Join(' ', planoConsulta.Temas)}");
        var projetoAlvo = planoConsulta.ProjetoAlvo.Equals("todos", StringComparison.OrdinalIgnoreCase)
            ? null
            : planoConsulta.ProjetoAlvo;
        var projetoAlvoNormalizado = string.IsNullOrWhiteSpace(projetoAlvo)
            ? null
            : NormalizarTextoBusca(projetoAlvo);

        return secoes
            .Select(secao => new
            {
                Secao = secao,
                Score = CalcularScore(secao, termos, projetoAlvoNormalizado)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Secao.Projeto)
            .Take(quantidade)
            .Select(item => item.Secao)
            .ToList();
    }

    public string MontarContexto(List<SecaoDocumento> secoes)
    {
        var sb = new StringBuilder();
        foreach (var secao in secoes)
        {
            sb.AppendLine($"## PROJETO: {secao.Projeto}");
            sb.AppendLine($"### SECAO: {secao.Titulo}");
            sb.AppendLine($"(fonte: {secao.Arquivo})");
            sb.AppendLine(secao.Conteudo);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static int CalcularScore(SecaoDocumento secao, HashSet<string> termos, string? projetoAlvoNormalizado)
    {
        var score = 0;
        var projeto = ObterTextoNormalizado(secao.ProjetoNormalizado, secao.Projeto);
        var titulo = ObterTextoNormalizado(secao.TituloNormalizado, secao.Titulo);
        var conteudo = ObterTextoNormalizado(secao.ConteudoNormalizado, secao.Conteudo);

        if (!string.IsNullOrWhiteSpace(projetoAlvoNormalizado) &&
            projeto.Contains(projetoAlvoNormalizado, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        foreach (var termo in termos)
        {
            if (titulo.Contains(termo, StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
            }

            if (conteudo.Contains(termo, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }
        }

        if (titulo.Contains("stack") || titulo.Contains("tecnologias"))
        {
            score += AjustarPorTema(termos, ["stack", "tecnologia", "tecnologias", "framework", "frontend", "backend"], 18);
        }

        if (titulo.Contains("autenticacao") || titulo.Contains("seguranca") || titulo.Contains("permissoes") || titulo.Contains("acesso"))
        {
            score += AjustarPorTema(termos, ["login", "senha", "ad", "active", "directory", "autenticacao", "permissao"], 18);
        }

        if (titulo.Contains("banco") || titulo.Contains("dados"))
        {
            score += AjustarPorTema(termos, ["sql", "banco", "dados", "tabela", "database"], 18);
        }

        if (titulo.Contains("roteamento") || titulo.Contains("url") || titulo.Contains("views"))
        {
            score += AjustarPorTema(termos, ["rota", "rotas", "url", "endpoint", "endpoints", "views"], 18);
        }

        if (titulo.Contains("logs") || titulo.Contains("observabilidade"))
        {
            score += AjustarPorTema(termos, ["log", "logs", "erro", "observabilidade"], 18);
        }

        if (titulo.Contains("e-mails"))
        {
            score += AjustarPorTema(termos, ["email", "emails", "smtp"], 18);
        }

        if (conteudo.Length > 6500)
        {
            score -= 4;
        }

        return score;
    }

    private static int AjustarPorTema(HashSet<string> termos, string[] tema, int score)
    {
        return termos.Overlaps(tema) ? score : 0;
    }

    private static HashSet<string> ExtrairTermos(string texto)
    {
        var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "as", "o", "os", "de", "da", "do", "das", "dos", "e", "em", "no", "na", "nos", "nas",
            "para", "por", "com", "como", "qual", "quais", "que", "se", "uma", "um", "ou", "ao", "aos",
            "projeto", "interno", "nsi", "senac", "rn"
        };

        return Regex.Matches(NormalizarTextoBusca(texto), @"[a-z0-9\-_]{2,}")
            .Select(match => match.Value)
            .Where(termo => !stopwords.Contains(termo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static string NormalizarTextoBusca(string texto)
    {
        return texto
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

    private static string ObterTextoNormalizado(string textoNormalizado, string textoOriginal)
    {
        return string.IsNullOrWhiteSpace(textoNormalizado)
            ? NormalizarTextoBusca(textoOriginal)
            : textoNormalizado;
    }
}
