using System.Text.RegularExpressions;
using NsiDocs.Modelos;

namespace NsiDocs.Servicos;

internal sealed class AvaliadorCoberturaConsulta
{
    private static readonly string[] VerbosConsulta =
    [
        "explique", "explicar", "detalhe", "detalhada", "detalhado", "detalhar",
        "liste", "listar", "mostre", "mostrar", "compare", "comparar",
        "descreva", "descrever", "fale", "falar", "funciona", "funcionar",
        "organizada", "organizado", "usada", "usado", "aparece", "aparecem",
        "pontos", "detalhadamente", "forma", "pergunte", "sobre"
    ];

    private static readonly string[] MarcadoresFallback =
    [
        "nao encontrei essa informacao",
        "nao encontrei trechos relevantes",
        "nao foi localizada a informacao solicitada",
        "info nao encontrada",
        "resultado da busca",
        "verifique se a busca cobriu",
        "tente termos de pesquisa diferentes",
        "solicite ao autor",
        "consulte os indices",
        "nao encontrou essa informacao"
    ];

    public CoberturaDocumentalDto Avaliar(
        string pergunta,
        string respostaFinal,
        IReadOnlyList<SecaoDocumento> secoesUtilizadas)
    {
        if (secoesUtilizadas.Count == 0)
        {
            return new CoberturaDocumentalDto(
                "baixa",
                "Nao encontramos trechos relevantes para sustentar a resposta.",
                ["Nenhuma secao da documentacao foi usada na resposta."],
                [
                    "Use termos mais literais da documentacao, como nomes de secoes ou funcionalidades.",
                    "Se souber o projeto certo, marque apenas esse documento antes de perguntar."
                ]);
        }

        var termosProjeto = secoesUtilizadas
            .SelectMany(secao => AnaliseTextoBusca.ExtrairTermos(secao.Projeto))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var termosPergunta = AnaliseTextoBusca.ExtrairTermos(
            pergunta,
            termosProjeto.Concat(VerbosConsulta));

        var textoSecoesNormalizado = AnaliseTextoBusca.NormalizarTexto(
            string.Join(' ', secoesUtilizadas.Select(secao => $"{secao.Titulo} {secao.Conteudo}")));

        var respostaNormalizada = AnaliseTextoBusca.NormalizarTexto(respostaFinal);
        var termosCobertos = termosPergunta
            .Where(termo => textoSecoesNormalizado.Contains(termo, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var proporcaoCoberta = termosPergunta.Count == 0
            ? 1
            : (double)termosCobertos.Count / termosPergunta.Count;

        var respostaGenerica = MarcadoresFallback.Any(marcador =>
            respostaNormalizada.Contains(marcador, StringComparison.OrdinalIgnoreCase));

        var nivel = ClassificarNivel(secoesUtilizadas.Count, proporcaoCoberta, respostaGenerica, termosPergunta.Count);
        var detalhes = MontarDetalhes(
            nivel,
            secoesUtilizadas,
            termosPergunta,
            termosCobertos,
            respostaGenerica);
        var sugestoes = MontarSugestoes(nivel, secoesUtilizadas, termosPergunta, termosCobertos);

        return new CoberturaDocumentalDto(
            nivel,
            ObterMensagemNivel(nivel),
            detalhes,
            sugestoes);
    }

    private static string ClassificarNivel(
        int quantidadeSecoes,
        double proporcaoCoberta,
        bool respostaGenerica,
        int quantidadeTermosPergunta)
    {
        if (quantidadeSecoes == 0)
        {
            return "baixa";
        }

        if (!respostaGenerica && quantidadeSecoes >= 4 && proporcaoCoberta >= 0.66)
        {
            return "alta";
        }

        if (respostaGenerica)
        {
            return proporcaoCoberta >= 0.34 && quantidadeSecoes >= 3
                ? "media"
                : "baixa";
        }

        if (proporcaoCoberta >= 0.34 && quantidadeSecoes >= 2)
        {
            return "media";
        }

        if (quantidadeTermosPergunta == 0 && quantidadeSecoes >= 3)
        {
            return "media";
        }

        return "baixa";
    }

    private static IReadOnlyList<string> MontarDetalhes(
        string nivel,
        IReadOnlyList<SecaoDocumento> secoesUtilizadas,
        HashSet<string> termosPergunta,
        HashSet<string> termosCobertos,
        bool respostaGenerica)
    {
        var detalhes = new List<string>();

        detalhes.Add($"A resposta usou {secoesUtilizadas.Count} seção(ões) da documentação.");

        if (respostaGenerica)
        {
            detalhes.Add("A resposta final caiu em um formato mais genérico do que o esperado.");
        }

        if (termosPergunta.Count > 0)
        {
            var termosNaoCobertos = termosPergunta
                .Except(termosCobertos, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (termosNaoCobertos.Count > 0)
            {
                detalhes.Add($"Faltou aderência para termos da pergunta como: {string.Join(", ", termosNaoCobertos)}.");
            }
        }

        if (nivel != "alta")
        {
            var titulosCurtos = ExtrairTitulosCurtos(secoesUtilizadas, 3);
            if (titulosCurtos.Count > 0)
            {
                detalhes.Add($"Os trechos recuperados falaram mais de: {string.Join(", ", titulosCurtos)}.");
            }
        }

        return detalhes
            .Where(detalhe => !string.IsNullOrWhiteSpace(detalhe))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private static IReadOnlyList<string> MontarSugestoes(
        string nivel,
        IReadOnlyList<SecaoDocumento> secoesUtilizadas,
        HashSet<string> termosPergunta,
        HashSet<string> termosCobertos)
    {
        if (nivel == "alta")
        {
            return [];
        }

        var sugestoes = new List<string>
        {
            "Se puder, use termos mais literais da documentação ou nomes de seções."
        };

        var titulosCurtos = ExtrairTitulosCurtos(secoesUtilizadas, 3);
        if (titulosCurtos.Count > 0)
        {
            sugestoes.Add($"Neste contexto, tente perguntar por: {string.Join(", ", titulosCurtos)}.");
        }

        var termosNaoCobertos = termosPergunta
            .Except(termosCobertos, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        if (termosNaoCobertos.Count > 0)
        {
            sugestoes.Add($"Reformule citando diretamente o que procura, por exemplo: {string.Join(", ", termosNaoCobertos)}.");
        }

        sugestoes.Add("Se souber o documento certo, marque apenas ele antes de perguntar.");

        return sugestoes
            .Where(sugestao => !string.IsNullOrWhiteSpace(sugestao))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static string ObterMensagemNivel(string nivel)
    {
        return nivel switch
        {
            "alta" => "Encontramos trechos bem alinhados com a pergunta.",
            "media" => "Encontramos parte do contexto, mas a cobertura ficou parcial.",
            _ => "A documentação recuperada não cobriu bem o que você perguntou."
        };
    }

    private static List<string> ExtrairTitulosCurtos(IReadOnlyList<SecaoDocumento> secoesUtilizadas, int quantidade)
    {
        return secoesUtilizadas
            .Select(secao => secao.Titulo.Split(" > ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? secao.Titulo)
            .Select(LimparTitulo)
            .Where(titulo => !string.IsNullOrWhiteSpace(titulo))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(quantidade)
            .ToList();
    }

    private static string LimparTitulo(string titulo)
    {
        var texto = Regex.Replace(titulo, @"[`*_#>\[\]\(\)]", string.Empty);
        return Regex.Replace(texto, @"\s+", " ").Trim();
    }
}

internal sealed record CoberturaDocumentalDto(
    string Nivel,
    string Mensagem,
    IReadOnlyList<string> Detalhes,
    IReadOnlyList<string> Sugestoes);
