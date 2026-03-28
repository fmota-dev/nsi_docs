using System.Text.RegularExpressions;
using NsiDocs.Configuracoes;
using NsiDocs.Modelos;
using NsiDocs.Servicos;

namespace NsiDocs.Agentes;

internal sealed class OrquestradorConsulta
{
    private readonly RecuperadorContexto _recuperadorContexto;
    private readonly FabricaAgentes _fabricaAgentes;
    private readonly ConfiguracaoAplicacao _configuracao;

    public OrquestradorConsulta(
        RecuperadorContexto recuperadorContexto,
        FabricaAgentes fabricaAgentes,
        ConfiguracaoAplicacao configuracao)
    {
        _recuperadorContexto = recuperadorContexto;
        _fabricaAgentes = fabricaAgentes;
        _configuracao = configuracao;
    }

    public async Task<ResultadoConsulta> ProcessarAsync(string pergunta)
    {
        var planejador = _fabricaAgentes.CriarPlanejador();
        var analista = _fabricaAgentes.CriarAnalistaContexto();
        var respondedor = _fabricaAgentes.CriarRespondedor();
        var formatador = _fabricaAgentes.CriarFormatador();

        var respostaPlanejador = await ExecutorAgente.ObterRespostaAsync(
            planejador,
            pergunta,
            _configuracao.TimeoutPlanejador);

        var planoConsulta = InterpretarPlanoConsulta(respostaPlanejador);

        List<SecaoDocumento> secoesRecuperadas;
        try
        {
            secoesRecuperadas = await _fabricaAgentes.BuscarSecoesRelevantesComPluginAsync(
                pergunta,
                planoConsulta,
                _configuracao.QuantidadeSecoesRecuperadas);
        }
        catch
        {
            secoesRecuperadas = _recuperadorContexto.RecuperarSecoes(
                _fabricaAgentes.ObterProjetos(),
                planoConsulta,
                pergunta,
                _configuracao.QuantidadeSecoesRecuperadas);
        }

        var secoesUtilizadas = secoesRecuperadas
            .Take(_configuracao.QuantidadeSecoesUtilizadas)
            .ToList();

        if (secoesUtilizadas.Count == 0)
        {
            return new ResultadoConsulta
            {
                RespostaFinal = "Nao encontrei trechos relevantes na documentacao.",
                SecoesUtilizadas = []
            };
        }

        var contextoRecuperado = _recuperadorContexto.MontarContexto(secoesUtilizadas);

        var contextoConsolidado = await ExecutorAgente.ObterRespostaAsync(
            analista,
            $"""
             Pergunta:
             {pergunta}

             Plano de consulta:
             Projeto: {planoConsulta.ProjetoAlvo}
             Temas: {string.Join(", ", planoConsulta.Temas)}
             Objetivo: {planoConsulta.Objetivo}

             Trechos recuperados:
             {contextoRecuperado}
             """,
            _configuracao.TimeoutAnalista);

        var respostaTecnica = await ExecutorAgente.ObterRespostaAsync(
            respondedor,
            $"""
             Pergunta do usuario:
             {pergunta}

             Contexto consolidado:
             {contextoConsolidado}
             """,
            _configuracao.TimeoutRespondedor);

        var respostaFinal = await ExecutorAgente.ObterRespostaAsync(
            formatador,
            respostaTecnica,
            _configuracao.TimeoutFormatador);

        return new ResultadoConsulta
        {
            RespostaFinal = respostaFinal,
            SecoesUtilizadas = secoesUtilizadas
        };
    }

    private static PlanoConsulta InterpretarPlanoConsulta(string respostaPlanejador)
    {
        var projeto = ExtrairValor(respostaPlanejador, "Projeto") ?? "todos";
        var temas = (ExtrairValor(respostaPlanejador, "Temas") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var objetivo = ExtrairValor(respostaPlanejador, "Objetivo") ?? string.Empty;

        return new PlanoConsulta
        {
            ProjetoAlvo = string.IsNullOrWhiteSpace(projeto) ? "todos" : projeto,
            Temas = temas,
            Objetivo = objetivo
        };
    }

    private static string? ExtrairValor(string texto, string campo)
    {
        var match = Regex.Match(
            texto,
            $@"^{campo}\s*:\s*(.+)$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
