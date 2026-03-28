using Microsoft.SemanticKernel.Agents;
using System.Collections;
using System.Text;
using System.Reflection;

namespace NsiDocs.Servicos;

internal static class ExecutorAgente
{
    public static async Task<string> ObterRespostaAsync(
        ChatCompletionAgent agente,
        string mensagem,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

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

    public static async Task<string> ObterRespostaStreamingAsync(
        ChatCompletionAgent agente,
        string mensagem,
        TimeSpan timeout,
        Func<string, CancellationToken, Task> aoReceberChunk,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var buffer = new StringBuilder();

        await foreach (var resposta in agente.InvokeStreamingAsync(mensagem, cancellationToken: cts.Token))
        {
            var conteudo = ExtrairConteudoStreaming(resposta);
            if (conteudo.Length == 0)
            {
                continue;
            }

            buffer.Append(conteudo);
            await aoReceberChunk(conteudo, cts.Token);
        }

        var respostaCompleta = buffer.ToString();
        if (string.IsNullOrWhiteSpace(respostaCompleta))
        {
            return await ObterRespostaAsync(agente, mensagem, timeout, cancellationToken);
        }

        return NormalizarResposta(respostaCompleta);
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

    private static string ExtrairConteudoStreaming(object resposta)
    {
        var tipo = resposta.GetType();

        if (tipo.GetProperty("Message", BindingFlags.Instance | BindingFlags.Public)?.GetValue(resposta) is object mensagem &&
            ExtrairConteudoStreamingMensagem(mensagem) is string conteudoMensagem &&
            conteudoMensagem.Length > 0)
        {
            return conteudoMensagem;
        }

        if (ExtrairPrimeiraStringUtil(tipo, resposta) is string conteudo &&
            conteudo.Length > 0)
        {
            return conteudo;
        }

        if (tipo.GetProperty("Items", BindingFlags.Instance | BindingFlags.Public)?.GetValue(resposta) is IEnumerable itens)
        {
            var conteudoItens = string.Join(
                string.Empty,
                itens.Cast<object?>().Select(ExtrairTextoItemStreaming));

            if (conteudoItens.Length > 0)
            {
                return conteudoItens;
            }
        }

        return string.Empty;
    }

    private static string ExtrairConteudoStreamingMensagem(object mensagem)
    {
        var tipo = mensagem.GetType();

        if (ExtrairPrimeiraStringUtil(tipo, mensagem) is string conteudo &&
            conteudo.Length > 0)
        {
            return conteudo;
        }

        if (tipo.GetProperty("Items", BindingFlags.Instance | BindingFlags.Public)?.GetValue(mensagem) is IEnumerable itens)
        {
            var conteudoItens = string.Join(
                string.Empty,
                itens.Cast<object?>().Select(ExtrairTextoItemStreaming));
            if (conteudoItens.Length > 0)
            {
                return conteudoItens;
            }
        }

        return string.Empty;
    }

    private static string ExtrairTextoItemStreaming(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var tipo = item.GetType();
        return ExtrairPrimeiraStringUtil(tipo, item)
            ?? item.ToString()
            ?? string.Empty;
    }

    private static string? ExtrairPrimeiraStringUtil(Type tipo, object instancia)
    {
        var propriedades = tipo
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(propriedade => propriedade.PropertyType == typeof(string))
            .ToList();

        foreach (var nome in new[] { "Content", "Text", "TextContent", "Value", "InnerContent" })
        {
            var propriedade = propriedades.FirstOrDefault(item => item.Name.Equals(nome, StringComparison.OrdinalIgnoreCase));
            if (propriedade?.GetValue(instancia) is string valorPreferencial &&
                valorPreferencial.Length > 0)
            {
                return valorPreferencial;
            }
        }

        return null;
    }
}
