using System.ComponentModel;
using System.Text;
using System.Text.Json;
using AgentesFramework.Modelos;
using AgentesFramework.Servicos;
using Microsoft.SemanticKernel;

namespace AgentesFramework.Plugins;

internal sealed class PluginDocumentacaoNsi
{
    private readonly List<ProjetoDocumentacao> _projetos;
    private readonly RecuperadorContexto _recuperadorContexto;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PluginDocumentacaoNsi(
        List<ProjetoDocumentacao> projetos,
        RecuperadorContexto recuperadorContexto)
    {
        _projetos = projetos;
        _recuperadorContexto = recuperadorContexto;
    }

    [KernelFunction("obter_indice_documentacao")]
    [Description("Retorna um indice resumido dos projetos e secoes de documentacao disponiveis.")]
    public string ObterIndiceDocumentacao()
    {
        var sb = new StringBuilder();
        foreach (var projeto in _projetos.OrderBy(item => item.Nome))
        {
            sb.AppendLine($"## Projeto: {projeto.Nome}");
            foreach (var secao in projeto.Secoes.Take(18))
            {
                sb.AppendLine($"- {secao.Titulo}");
            }

            if (projeto.Secoes.Count > 18)
            {
                sb.AppendLine("- ...");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    [KernelFunction("buscar_secoes_relevantes")]
    [Description("Busca secoes tecnicas relevantes para a pergunta e retorna JSON com projeto, titulo, arquivo e conteudo.")]
    public string BuscarSecoesRelevantes(
        [Description("Pergunta do usuario sobre a documentacao")] string pergunta,
        [Description("Projeto alvo ou todos")] string projetoAlvo = "todos",
        [Description("Temas separados por virgula")] string temasCsv = "",
        [Description("Quantidade maxima de secoes")] int quantidade = 8)
    {
        if (string.IsNullOrWhiteSpace(pergunta))
        {
            return "[]";
        }

        var plano = new PlanoConsulta
        {
            ProjetoAlvo = string.IsNullOrWhiteSpace(projetoAlvo) ? "todos" : projetoAlvo,
            Temas = temasCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            Objetivo = string.Empty
        };

        var secoes = _recuperadorContexto.RecuperarSecoes(
            _projetos,
            plano,
            pergunta,
            Math.Clamp(quantidade, 1, 20));

        var resultado = secoes.Select(secao => new SecaoPluginDto
        {
            Projeto = secao.Projeto,
            Titulo = secao.Titulo,
            Arquivo = secao.Arquivo,
            Conteudo = secao.Conteudo
        });

        return JsonSerializer.Serialize(resultado, JsonOptions);
    }

    private sealed class SecaoPluginDto
    {
        public string Projeto { get; init; } = string.Empty;
        public string Titulo { get; init; } = string.Empty;
        public string Arquivo { get; init; } = string.Empty;
        public string Conteudo { get; init; } = string.Empty;
    }
}
