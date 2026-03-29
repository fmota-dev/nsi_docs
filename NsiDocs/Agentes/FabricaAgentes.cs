using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.Ollama;
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

                            Regras para "Temas":
                            - Use de 3 a 7 termos concretos e buscaveis.
                            - Prefira substantivos tecnicos, nomes de componentes, tecnologias, cabecalhos, endpoints, entidades, modulos ou conceitos que provavelmente aparecam literalmente na documentacao.
                            - Evite termos vagos como "detalhes", "informacoes", "coisa", "geral".
                            - Se a pergunta estiver abstrata demais, traduza para conceitos tecnicos mais localizaveis na documentacao.

                            INDICE DISPONIVEL:
                            {GerarIndiceDocumentacao()}
                            """,
            Arguments = CriarArgumentosDeterministicos(),
            Kernel = _kernel
        };
    }

    public ChatCompletionAgent CriarAnalistaContexto()
    {
        return new ChatCompletionAgent
        {
            Name = "AnalistaContexto",
            Instructions = $"""
                            Voce recebe uma pergunta e trechos recuperados da documentacao.
                            Sua tarefa e condensar o contexto realmente util para o agente respondedor.

                            Regras:
                            - Use apenas fatos presentes nos trechos.
                            - Destaque projeto, secao e pontos tecnicos relevantes.
                            - Descarte repeticoes.
                            - Nao invente conexoes nem complete lacunas.
                            - Responda exatamente nesta estrutura:
                              Resposta principal: [conclusao central em uma frase]
                              Fatos confirmados:
                              - [fato confirmado]
                              - [fato confirmado]
                              Lacunas do contexto:
                              - [detalhe importante que nao apareceu]
                              Termos tecnicos centrais:
                              - [termo, tecnologia, endpoint, cabecalho, componente, claim ou regra]
                            - Se nao houver lacunas relevantes, escreva:
                              Lacunas do contexto:
                              - nenhuma lacuna relevante identificada nos trechos recuperados
                            - Gere no maximo {_configuracao.LimiteLinhasAnalista} linhas.
                            """,
            Arguments = CriarArgumentosDeterministicos(),
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
                           - Use a estrutura do contexto consolidado como fonte principal: resposta principal, fatos confirmados, lacunas do contexto e termos tecnicos centrais.
                           - Se o contexto contiver a resposta, responda diretamente na primeira linha.
                           - So use o fallback "Nao encontrei essa informacao na documentacao recuperada." quando realmente nao houver base suficiente no contexto.
                           - Nunca troque uma resposta valida por fallback apenas por causa do modo de resposta.
                           - Preserve a mesma base factual entre os modos curta, normal e detalhada.
                           - Nunca use linguagem especulativa ou alternativas nao sustentadas pelo contexto recuperado.
                           - Se houver qualquer duvida, prefira os trechos originais recuperados em vez de inferir algo novo.
                           - Cite explicitamente os termos tecnicos centrais apenas quando eles estiverem presentes no contexto consolidado ou nos trechos originais.
                           - Quando um detalhe importante nao estiver documentado, use a secao de lacunas do contexto para explicitar esse limite em vez de completar a resposta por inferencia.
                           - Quando apropriado, use listas com "-".
                           - Seja tecnico, objetivo e previsivel.
                           - Siga exatamente o contrato recebido no prompt para o perfil da pergunta e o modo de resposta.
                           """,
            Arguments = CriarArgumentosDeterministicos(),
            Kernel = _kernel
        };
    }

    public ChatCompletionAgent CriarFormatador()
    {
        return new ChatCompletionAgent
        {
            Name = "Formatador",
            Instructions = $"""
                            Voce formata a resposta final para console.
                            Regras:
                            - Use markdown simples.
                            - Preserve a primeira resposta factual recebida; ela nao pode sumir nem ser trocada por fragmentos.
                            - Prefira titulos curtos e listas para facilitar leitura.
                            - Se houver diagrama ASCII (com +, |, ->), envolva obrigatoriamente em bloco:
                              ```text
                              ...
                              ```
                            - Nunca escreva pseudo-tabela com pipes fora de bloco de codigo.
                            - Preserve o conteudo tecnico.
                            - Remova repeticoes.
                            - Nao adicione informacoes novas.
                            - Respeite o contrato recebido no campo "Modo de saida".
                            - Limite a resposta a no maximo {_configuracao.LimiteLinhasFormatador} linhas.
                            """,
            Arguments = CriarArgumentosDeterministicos(),
            Kernel = _kernel
        };
    }

    public ChatCompletionAgent CriarRespondedorFinal()
    {
        return new ChatCompletionAgent
        {
            Name = "RespondedorFinal",
            Instructions = """
                           Voce e o assistente de documentacao tecnica do NSI do Senac RN.
                           Sua tarefa e gerar a resposta final ja em markdown, sem depender de uma etapa posterior de formatacao.

                           Regras:
                           - Responda apenas com base no contexto recebido.
                           - Responda em portugues BR.
                           - Preserve a mesma base factual entre os modos curta, normal e detalhada.
                           - A primeira linha deve responder diretamente a pergunta quando o perfil for factual.
                           - Use titulos curtos e bullets simples apenas quando agregarem clareza.
                           - Nao use pseudo-tabelas com pipes fora de bloco de codigo.
                           - Se houver diagrama ASCII, envolva em ```text.
                           - Nao invente informacoes, exemplos, alternativas ou boas praticas nao sustentadas pelo contexto.
                           - Quando faltar um detalhe importante, explicite esse limite em vez de completar por inferencia.
                           - Preserve termos tecnicos centrais quando eles estiverem documentados.
                           - Nao devolva payload cru, JSON, listas quebradas nem fragmentos soltos.
                           - O resultado precisa sair pronto para exibicao final no chat.
                           """,
            Arguments = CriarArgumentosDeterministicos(),
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

    private static KernelArguments CriarArgumentosDeterministicos()
    {
        return new KernelArguments(new OllamaPromptExecutionSettings
        {
            Temperature = 0.1f,
            TopP = 0.2f,
            TopK = 20
        });
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
