using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace NsiDocs.Configuracoes;

internal sealed class ConfiguracaoAplicacao
{
    public string ModeloOllama { get; init; } = "gpt-oss:120b-cloud";
    public string EndpointOllama { get; init; } = "http://127.0.0.1:11434";
    public string PastaDocumentacoes { get; init; } = string.Empty;
    public string CaminhoArquivoConfiguracao { get; init; } = string.Empty;
    public TimeSpan TimeoutHttp { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan TimeoutPlanejador { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan TimeoutAnalista { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan TimeoutRespondedor { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan TimeoutFormatador { get; init; } = TimeSpan.FromSeconds(45);
    public int QuantidadeSecoesRecuperadas { get; init; } = 12;
    public int QuantidadeSecoesUtilizadas { get; init; } = 6;
    public int LimiteLinhasAnalista { get; init; } = 25;
    public int LimiteLinhasFormatador { get; init; } = 40;

    public static ConfiguracaoAplicacao Carregar()
    {
        var pastaBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var caminhoArquivoConfiguracao = Path.Combine(pastaBase, "configuracao.local.json");
        var configuracaoPersistida = CarregarConfiguracaoPersistida(caminhoArquivoConfiguracao);

        return new ConfiguracaoAplicacao
        {
            ModeloOllama = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
                ?? configuracaoPersistida?.ModeloOllama
                ?? "gpt-oss:120b-cloud",
            EndpointOllama = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")
                ?? configuracaoPersistida?.EndpointOllama
                ?? ObterEndpointOllamaPadrao(),
            PastaDocumentacoes = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "documentacoes")),
            CaminhoArquivoConfiguracao = caminhoArquivoConfiguracao,
            TimeoutHttp = LerSegundosAmbiente("NSIDOCS_TIMEOUT_HTTP_SEGUNDOS", TimeSpan.FromMinutes(5)),
            TimeoutPlanejador = LerSegundosAmbiente("NSIDOCS_TIMEOUT_PLANEJADOR_SEGUNDOS", TimeSpan.FromSeconds(60)),
            TimeoutAnalista = LerSegundosAmbiente("NSIDOCS_TIMEOUT_ANALISTA_SEGUNDOS", TimeSpan.FromSeconds(60)),
            TimeoutRespondedor = LerSegundosAmbiente("NSIDOCS_TIMEOUT_RESPONDEDOR_SEGUNDOS", TimeSpan.FromSeconds(90)),
            TimeoutFormatador = LerSegundosAmbiente("NSIDOCS_TIMEOUT_FORMATADOR_SEGUNDOS", TimeSpan.FromSeconds(45)),
            QuantidadeSecoesRecuperadas = LerInteiroAmbiente("NSIDOCS_SECOES_RECUPERADAS", 12),
            QuantidadeSecoesUtilizadas = LerInteiroAmbiente("NSIDOCS_SECOES_UTILIZADAS", 6),
            LimiteLinhasAnalista = LerInteiroAmbiente("NSIDOCS_LIMITE_LINHAS_ANALISTA", 25),
            LimiteLinhasFormatador = LerInteiroAmbiente("NSIDOCS_LIMITE_LINHAS_FORMATADOR", 40)
        };
    }

    public ConfiguracaoAplicacao ComOllama(string endpointOllama, string modeloOllama)
    {
        return new ConfiguracaoAplicacao
        {
            ModeloOllama = modeloOllama,
            EndpointOllama = endpointOllama,
            PastaDocumentacoes = PastaDocumentacoes,
            CaminhoArquivoConfiguracao = CaminhoArquivoConfiguracao,
            TimeoutHttp = TimeoutHttp,
            TimeoutPlanejador = TimeoutPlanejador,
            TimeoutAnalista = TimeoutAnalista,
            TimeoutRespondedor = TimeoutRespondedor,
            TimeoutFormatador = TimeoutFormatador,
            QuantidadeSecoesRecuperadas = QuantidadeSecoesRecuperadas,
            QuantidadeSecoesUtilizadas = QuantidadeSecoesUtilizadas,
            LimiteLinhasAnalista = LimiteLinhasAnalista,
            LimiteLinhasFormatador = LimiteLinhasFormatador
        };
    }

    public async Task SalvarAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(CaminhoArquivoConfiguracao))
        {
            throw new InvalidOperationException("Caminho do arquivo de configuracao nao foi definido.");
        }

        var pasta = Path.GetDirectoryName(CaminhoArquivoConfiguracao);
        if (!string.IsNullOrWhiteSpace(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        var conteudo = JsonSerializer.Serialize(
            new ConfiguracaoPersistidaDto
            {
                ModeloOllama = ModeloOllama,
                EndpointOllama = EndpointOllama
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        await File.WriteAllTextAsync(CaminhoArquivoConfiguracao, conteudo, cancellationToken);
    }

    private static string ObterEndpointOllamaPadrao()
    {
        var ipv4 = NetworkInterface.GetAllNetworkInterfaces()
            .Where(rede =>
                rede.OperationalStatus == OperationalStatus.Up &&
                rede.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(rede => rede.GetIPProperties().UnicastAddresses)
            .Select(endereco => endereco.Address)
            .FirstOrDefault(endereco =>
                endereco.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(endereco));

        return ipv4 is null
            ? "http://127.0.0.1:11434"
            : $"http://{ipv4}:11434";
    }

    private static ConfiguracaoPersistidaDto? CarregarConfiguracaoPersistida(string caminhoArquivoConfiguracao)
    {
        if (!File.Exists(caminhoArquivoConfiguracao))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(caminhoArquivoConfiguracao);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ConfiguracaoPersistidaDto>(json);
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan LerSegundosAmbiente(string nomeVariavel, TimeSpan valorPadrao)
    {
        var valorTexto = Environment.GetEnvironmentVariable(nomeVariavel);
        if (!int.TryParse(valorTexto, out var segundos) || segundos <= 0)
        {
            return valorPadrao;
        }

        return TimeSpan.FromSeconds(segundos);
    }

    private static int LerInteiroAmbiente(string nomeVariavel, int valorPadrao)
    {
        var valorTexto = Environment.GetEnvironmentVariable(nomeVariavel);
        if (!int.TryParse(valorTexto, out var valor) || valor <= 0)
        {
            return valorPadrao;
        }

        return valor;
    }

    private sealed class ConfiguracaoPersistidaDto
    {
        public string? ModeloOllama { get; init; }
        public string? EndpointOllama { get; init; }
    }
}
