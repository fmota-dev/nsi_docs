using Microsoft.SemanticKernel.Agents;

namespace NsiDocs.Servicos;

internal static class ExecutorAgente
{
    public static async Task<string> ObterRespostaAsync(
        ChatCompletionAgent agente,
        string mensagem,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        await foreach (var resposta in agente.InvokeAsync(mensagem, cancellationToken: cts.Token))
        {
            var conteudo = resposta.Message.Content;
            if (!string.IsNullOrWhiteSpace(conteudo))
            {
                return NormalizarResposta(conteudo);
            }

            var conteudoItens = string.Join(
                string.Empty,
                resposta.Message.Items.Select(item => item.ToString()));

            if (!string.IsNullOrWhiteSpace(conteudoItens))
            {
                return NormalizarResposta(conteudoItens);
            }
        }

        return "(sem resposta do agente)";
    }

    private static string NormalizarResposta(string texto)
    {
        var resposta = texto.Trim();
        var inicioCerca = resposta.IndexOf("```", StringComparison.Ordinal);
        if (inicioCerca < 0)
        {
            return resposta;
        }

        var inicioConteudo = resposta.IndexOf('\n', inicioCerca);
        if (inicioConteudo < 0)
        {
            return resposta;
        }

        var fimCerca = resposta.IndexOf("```", inicioConteudo + 1, StringComparison.Ordinal);
        if (fimCerca < 0)
        {
            return resposta;
        }

        return resposta[(inicioConteudo + 1)..fimCerca].Trim();
    }
}
