using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NsiDocs.Configuracoes;
using NsiDocs.Modelos;
using NsiDocs.Plugins;
using NsiDocs.Servicos;

namespace NsiDocs.Agentes;

internal sealed class FabricaAgentes
{
    private const string NomePluginDocumentacao = "DocumentacaoPlugin";

    private readonly Kernel _kernel;
    private readonly List<ProjetoDocumentacao> _projetos;
    private readonly RecuperadorContexto _recuperadorContexto;
    private readonly ConfiguracaoAplicacao _configuracao;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FabricaAgentes(
        Kernel kernel,
        List<ProjetoDocumentacao> projetos,
        RecuperadorContexto recuperadorContexto,
        ConfiguracaoAplicacao configuracao)
    {
        _kernel = kernel;
        _projetos = projetos;
        _recuperadorContexto = recuperadorContexto;
        _configuracao = configuracao;

        RegistrarPluginDocumentacao();
    }

    public ChatCompletionAgent CriarPlanejador()
    {
        return new ChatCompletionAgent
        {
            Name = "Planejador",
            Instructions = $"""
                            Voce analisa perguntas sobre a documentacao do NSI.
                            Sua tarefa e planejar a consulta com base apenas no indice dos projetos e secoes.

                            Plugin disponivel:
                            - {NomePluginDocumentacao}.obter_indice_documentacao
                            - {NomePluginDocumentacao}.buscar_secoes_relevantes

                            Responda exatamente neste formato:
                            Projeto: [nome do projeto ou "todos"]
                            Temas: [lista curta separada por virgula]
                            Objetivo: [o que a resposta precisa entregar]

                            INDICE DISPONIVEL:
                            {GerarIndiceDocumentacao()}
                            """,
            Kernel = _kernel
        };
    }

    public ChatCompletionAgent CriarAnalistaContexto()
    {
        return new ChatCompletionAgent
        {
            Name = "AnalistaContexto",
            Instructions = """
                           Voce recebe uma pergunta e trechos recuperados da documentacao.
                           Sua tarefa e condensar o contexto realmente util para o agente respondedor.

                           Regras:
                           - Use apenas fatos presentes nos trechos.
                           - Destaque projeto, secao e pontos tecnicos relevantes.
                           - Descarte repeticoes.
                           - Gere no maximo 15 linhas.
                           """,
            Kernel = _kernel
        };
    }

    public ChatCompletionAgent CriarRespondedor()
    {
        return new ChatCompletionAgent
        {
            Name = "Respondedor",
            Instructions = """
                           Voce e o assistente de documentacao tecnica do NSI do Senac RN.
                           Responda apenas com base no contexto consolidado.

                           Regras:
                           - Responda em portugues BR.
                           - Nao invente nada.
                           - Se a informacao nao estiver clara no contexto, diga:
                             "Nao encontrei essa informacao na documentacao recuperada."
                           - Quando apropriado, use listas com "-".
                           - Seja tecnico e objetivo.
                           """,
            Kernel = _kernel
        };
    }

    public ChatCompletionAgent CriarFormatador()
    {
        return new ChatCompletionAgent
        {
            Name = "Formatador",
            Instructions = """
                           Voce formata a resposta final para console.
                           Regras:
                           - Use markdown simples.
                           - Prefira titulos curtos e listas para facilitar leitura.
                           - Se houver diagrama ASCII (com +, |, ->), envolva obrigatoriamente em bloco:
                             ```text
                             ...
                             ```
                           - Nunca escreva pseudo-tabela com pipes fora de bloco de codigo.
                           - Preserve o conteudo tecnico.
                           - Remova repeticoes.
                           - Nao adicione informacoes novas.
                           - Limite a resposta a no maximo 25 linhas.
                           """,
            Kernel = _kernel
        };
    }

    public List<ProjetoDocumentacao> ObterProjetos()
    {
        return _projetos;
    }

    public async Task<List<SecaoDocumento>> BuscarSecoesRelevantesComPluginAsync(
        string pergunta,
        PlanoConsulta planoConsulta,
        int quantidade,
        CancellationToken cancellationToken = default)
    {
        var argumentos = new KernelArguments
        {
            ["pergunta"] = pergunta,
            ["projetoAlvo"] = planoConsulta.ProjetoAlvo,
            ["temasCsv"] = string.Join(", ", planoConsulta.Temas),
            ["quantidade"] = quantidade
        };

        var resultado = await _kernel.InvokeAsync(
            NomePluginDocumentacao,
            "buscar_secoes_relevantes",
            argumentos,
            cancellationToken);

        var json = resultado.GetValue<string>() ?? "[]";
        var secoes = JsonSerializer.Deserialize<List<SecaoPluginResultado>>(json, JsonOptions) ?? [];

        return secoes
            .Where(secao =>
                !string.IsNullOrWhiteSpace(secao.Projeto) &&
                !string.IsNullOrWhiteSpace(secao.Titulo) &&
                !string.IsNullOrWhiteSpace(secao.Conteudo))
            .Select(secao => new SecaoDocumento
            {
                Projeto = secao.Projeto,
                Titulo = secao.Titulo,
                Arquivo = secao.Arquivo,
                Conteudo = secao.Conteudo
            })
            .ToList();
    }

    private void RegistrarPluginDocumentacao()
    {
        if (_kernel.Plugins.TryGetPlugin(NomePluginDocumentacao, out var pluginExistente))
        {
            _kernel.Plugins.Remove(pluginExistente);
        }

        var plugin = KernelPluginFactory.CreateFromObject(
            new PluginDocumentacaoNsi(_projetos, _recuperadorContexto),
            NomePluginDocumentacao);

        _kernel.Plugins.Add(plugin);
    }

    private string GerarIndiceDocumentacao()
    {
        var sb = new StringBuilder();
        foreach (var projeto in _projetos)
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

    private sealed class SecaoPluginResultado
    {
        public string Projeto { get; init; } = string.Empty;
        public string Titulo { get; init; } = string.Empty;
        public string Arquivo { get; init; } = string.Empty;
        public string Conteudo { get; init; } = string.Empty;
    }
}
