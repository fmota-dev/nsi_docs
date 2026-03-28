using System.Text;
using AgentesFramework.Configuracoes;
using AgentesFramework.Modelos;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace AgentesFramework.Agentes;

internal sealed class FabricaAgentes
{
    private readonly Kernel _kernel;
    private readonly List<ProjetoDocumentacao> _projetos;
    private readonly ConfiguracaoAplicacao _configuracao;

    public FabricaAgentes(Kernel kernel, List<ProjetoDocumentacao> projetos, ConfiguracaoAplicacao configuracao)
    {
        _kernel = kernel;
        _projetos = projetos;
        _configuracao = configuracao;
    }

    public ChatCompletionAgent CriarPlanejador()
    {
        return new ChatCompletionAgent
        {
            Name = "Planejador",
            Instructions = $"""
                            Voce analisa perguntas sobre a documentacao do NSI.
                            Sua tarefa e planejar a consulta com base apenas no indice dos projetos e secoes.

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
}
