using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.SemanticKernel;
using NsiDocs.Agentes;
using NsiDocs.Configuracoes;
using NsiDocs.Modelos;

namespace NsiDocs.Servicos;

internal sealed class AplicacaoNsiDocs
{
    private ConfiguracaoAplicacao _configuracao;
    private readonly CarregadorDocumentacao _carregadorDocumentacao;
    private readonly RecuperadorContexto _recuperadorContexto;
    private readonly AvaliadorCoberturaConsulta _avaliadorCoberturaConsulta;
    private readonly GeradorSugestoesPerguntas _geradorSugestoesPerguntas;
    private Kernel _kernel;
    private readonly SemaphoreSlim _semaforo = new(1, 1);
    private List<ProjetoDocumentacao> _projetos = [];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AplicacaoNsiDocs(
        ConfiguracaoAplicacao configuracao,
        CarregadorDocumentacao carregadorDocumentacao,
        RecuperadorContexto recuperadorContexto,
        AvaliadorCoberturaConsulta avaliadorCoberturaConsulta,
        GeradorSugestoesPerguntas geradorSugestoesPerguntas)
    {
        _configuracao = configuracao;
        _carregadorDocumentacao = carregadorDocumentacao;
        _recuperadorContexto = recuperadorContexto;
        _avaliadorCoberturaConsulta = avaliadorCoberturaConsulta;
        _geradorSugestoesPerguntas = geradorSugestoesPerguntas;
        _kernel = CriarKernel(configuracao);
    }

    public async Task InicializarAsync(CancellationToken cancellationToken = default)
    {
        await RecarregarDocumentacoesAsync(cancellationToken);
    }

    public async Task RecarregarIndiceAsync(CancellationToken cancellationToken = default)
    {
        await RecarregarDocumentacoesAsync(cancellationToken);
    }

    public async Task<StatusAplicacaoDto> ObterStatusAsync(CancellationToken cancellationToken = default)
    {
        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            return new StatusAplicacaoDto(
                _configuracao.ModeloOllama,
                _projetos.Count,
                _projetos.Sum(projeto => projeto.Secoes.Count));
        }
        finally
        {
            _semaforo.Release();
        }
    }

    public async Task<ConfiguracaoOllamaDto> ObterConfiguracaoOllamaAsync(CancellationToken cancellationToken = default)
    {
        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            return new ConfiguracaoOllamaDto(_configuracao.EndpointOllama, _configuracao.ModeloOllama);
        }
        finally
        {
            _semaforo.Release();
        }
    }

    public async Task<TesteConexaoOllamaDto> TestarConexaoOllamaAsync(
        string endpointOllama,
        string modeloOllama,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ValidarEndpointOllama(endpointOllama);
        var modelo = ValidarModeloOllama(modeloOllama);
        return await TestarConexaoOllamaInternaAsync(endpoint, modelo, _configuracao.TimeoutHttp, cancellationToken);
    }

    public async Task<ConfiguracaoOllamaResultadoDto> SalvarConfiguracaoOllamaAsync(
        string endpointOllama,
        string modeloOllama,
        CancellationToken cancellationToken = default)
    {
        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            var endpoint = ValidarEndpointOllama(endpointOllama);
            var modelo = ValidarModeloOllama(modeloOllama);
            var teste = await TestarConexaoOllamaInternaAsync(endpoint, modelo, _configuracao.TimeoutHttp, cancellationToken);

            _configuracao = _configuracao.ComOllama(endpoint, modelo);
            await _configuracao.SalvarAsync(cancellationToken);
            _kernel = CriarKernel(_configuracao);

            return new ConfiguracaoOllamaResultadoDto(
                _configuracao.EndpointOllama,
                _configuracao.ModeloOllama,
                "Configuracao salva e conexao aplicada.",
                teste.ModelosDisponiveis);
        }
        finally
        {
            _semaforo.Release();
        }
    }

    public async Task<IReadOnlyList<DocumentoDto>> ListarDocumentosAsync(CancellationToken cancellationToken = default)
    {
        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            return _projetos
                .OrderBy(projeto => projeto.Nome)
                .ThenBy(projeto => projeto.Identificador)
                .Select(projeto => new DocumentoDto(
                    projeto.Identificador,
                    projeto.Nome,
                    projeto.Arquivo,
                    projeto.Secoes.Count))
                .ToList();
        }
        finally
        {
            _semaforo.Release();
        }
    }

    public async Task<RespostaChatDto> PerguntarAsync(
        string pergunta,
        IReadOnlyList<string>? documentosSelecionados = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pergunta))
        {
            throw new ArgumentException("A pergunta nao pode ser vazia.", nameof(pergunta));
        }

        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            var projetosConsulta = FiltrarProjetosSelecionados(documentosSelecionados);
            if (projetosConsulta.Count == 0)
            {
                throw new InvalidOperationException("Nenhum documento selecionado e valido para a consulta.");
            }

            var orquestradorConsulta = CriarOrquestradorConsulta(projetosConsulta);

            var resultado = await orquestradorConsulta.ProcessarAsync(pergunta).WaitAsync(cancellationToken);
            var perguntasSugeridas = _geradorSugestoesPerguntas.Gerar(pergunta, resultado.SecoesUtilizadas);
            var cobertura = _avaliadorCoberturaConsulta.Avaliar(
                pergunta,
                resultado.RespostaFinal,
                resultado.SecoesUtilizadas);

            return new RespostaChatDto(
                resultado.RespostaFinal,
                resultado.SecoesUtilizadas
                    .Select(secao => new SecaoUtilizadaDto(secao.Projeto, secao.Titulo))
                    .ToList(),
                perguntasSugeridas,
                cobertura);
        }
        finally
        {
            _semaforo.Release();
        }
    }

    public async Task<DocumentoUploadResultadoDto> SalvarDocumentoAsync(
        Stream conteudo,
        string nomeArquivoOriginal,
        CancellationToken cancellationToken = default)
    {
        var nomeTratado = TratarNomeArquivo(nomeArquivoOriginal);
        if (!nomeTratado.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Somente arquivos .md sao aceitos.");
        }

        var caminhoFinal = ObterCaminhoDisponivel(nomeTratado);

        await using (var arquivo = File.Create(caminhoFinal))
        {
            await conteudo.CopyToAsync(arquivo, cancellationToken);
        }

        await RecarregarDocumentacoesAsync(cancellationToken);

        return new DocumentoUploadResultadoDto(
            Path.GetFileName(caminhoFinal),
            "Documentacao enviada com sucesso e indice recarregado.");
    }

    private async Task RecarregarDocumentacoesAsync(CancellationToken cancellationToken)
    {
        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_configuracao.PastaDocumentacoes);

            _projetos = await _carregadorDocumentacao
                .CarregarProjetosAsync(_configuracao.PastaDocumentacoes)
                .WaitAsync(cancellationToken);
        }
        finally
        {
            _semaforo.Release();
        }
    }

    private string ObterCaminhoDisponivel(string nomeArquivo)
    {
        var caminhoBase = Path.Combine(_configuracao.PastaDocumentacoes, nomeArquivo);
        if (!File.Exists(caminhoBase))
        {
            return caminhoBase;
        }

        var nomeSemExtensao = Path.GetFileNameWithoutExtension(nomeArquivo);
        var extensao = Path.GetExtension(nomeArquivo);
        var contador = 2;

        while (true)
        {
            var candidato = Path.Combine(
                _configuracao.PastaDocumentacoes,
                $"{nomeSemExtensao}-{contador}{extensao}");

            if (!File.Exists(candidato))
            {
                return candidato;
            }

            contador++;
        }
    }

    private static string TratarNomeArquivo(string nomeArquivo)
    {
        var nomeSeguro = Path.GetFileName(nomeArquivo);
        var sb = new StringBuilder(nomeSeguro.Length);

        foreach (var caractere in nomeSeguro)
        {
            sb.Append(char.IsLetterOrDigit(caractere) || caractere is '.' or '-' or '_' ? caractere : '-');
        }

        return sb.ToString();
    }

    private OrquestradorConsulta CriarOrquestradorConsulta(List<ProjetoDocumentacao> projetos)
    {
        var fabricaAgentes = new FabricaAgentes(
            _kernel,
            projetos,
            _recuperadorContexto,
            _configuracao);

        return new OrquestradorConsulta(
            _recuperadorContexto,
            fabricaAgentes,
            _configuracao);
    }

    private List<ProjetoDocumentacao> FiltrarProjetosSelecionados(IReadOnlyList<string>? documentosSelecionados)
    {
        if (documentosSelecionados is null || documentosSelecionados.Count == 0)
        {
            return _projetos;
        }

        var identificadores = documentosSelecionados
            .Select(NormalizarIdentificadorDocumento)
            .Where(identificador => !string.IsNullOrWhiteSpace(identificador))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (identificadores.Count == 0)
        {
            return _projetos;
        }

        return _projetos
            .Where(projeto => identificadores.Contains(projeto.Identificador))
            .ToList();
    }

    private static string NormalizarIdentificadorDocumento(string identificador)
    {
        return identificador.Trim().Replace('\\', '/');
    }

    private static Kernel CriarKernel(ConfiguracaoAplicacao configuracao)
    {
        var httpClient = CriarHttpClient(configuracao.EndpointOllama, configuracao.TimeoutHttp);
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddOllamaChatCompletion(
            modelId: configuracao.ModeloOllama,
            httpClient: httpClient);

        return kernelBuilder.Build();
    }

    private static HttpClient CriarHttpClient(string endpointOllama, TimeSpan timeout)
    {
        return new HttpClient
        {
            BaseAddress = new Uri(endpointOllama),
            Timeout = timeout
        };
    }

    private static string ValidarEndpointOllama(string endpointOllama)
    {
        var endpoint = endpointOllama.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Informe o endpoint do Ollama.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Informe um endpoint valido, por exemplo http://192.168.0.3:11434.");
        }

        return uri.ToString().TrimEnd('/');
    }

    private static string ValidarModeloOllama(string modeloOllama)
    {
        var modelo = modeloOllama.Trim();
        if (string.IsNullOrWhiteSpace(modelo))
        {
            throw new InvalidOperationException("Informe o modelo do Ollama.");
        }

        return modelo;
    }

    private static async Task<TesteConexaoOllamaDto> TestarConexaoOllamaInternaAsync(
        string endpointOllama,
        string modeloOllama,
        TimeSpan timeoutHttp,
        CancellationToken cancellationToken)
    {
        using var httpClient = CriarHttpClient(endpointOllama, timeoutHttp);

        HttpResponseMessage respostaTags;
        try
        {
            respostaTags = await httpClient.GetAsync("api/tags", cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Nao foi possivel acessar o endpoint informado: {ex.Message}");
        }

        var conteudoTags = await respostaTags.Content.ReadAsStringAsync(cancellationToken);
        if (!respostaTags.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtrairMensagemErroOllama(
                conteudoTags,
                $"O endpoint respondeu com erro ao listar modelos ({(int)respostaTags.StatusCode})."));
        }

        var tags = JsonSerializer.Deserialize<RespostaTagsOllamaDto>(conteudoTags, JsonOptions);
        var modelosDisponiveis = tags?.Models?
            .Select(modelo => modelo.Name ?? modelo.Model ?? string.Empty)
            .Where(modelo => !string.IsNullOrWhiteSpace(modelo))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(modelo => modelo)
            .ToList() ?? [];

        using var requisicaoShow = new HttpRequestMessage(HttpMethod.Post, "api/show")
        {
            Content = JsonContent.Create(new { model = modeloOllama })
        };

        HttpResponseMessage respostaShow;
        try
        {
            respostaShow = await httpClient.SendAsync(requisicaoShow, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"O endpoint respondeu, mas falhou ao validar o modelo: {ex.Message}");
        }

        var conteudoShow = await respostaShow.Content.ReadAsStringAsync(cancellationToken);
        if (!respostaShow.IsSuccessStatusCode)
        {
            var mensagem = ExtrairMensagemErroOllama(
                conteudoShow,
                $"O modelo '{modeloOllama}' nao foi validado no endpoint informado.");

            if (modelosDisponiveis.Count > 0)
            {
                mensagem = $"{mensagem} Modelos visiveis: {string.Join(", ", modelosDisponiveis.Take(8))}";
            }

            throw new InvalidOperationException(mensagem);
        }

        return new TesteConexaoOllamaDto(
            endpointOllama,
            modeloOllama,
            "Conexao validada com sucesso.",
            modelosDisponiveis);
    }

    private static string ExtrairMensagemErroOllama(string conteudo, string fallback)
    {
        if (string.IsNullOrWhiteSpace(conteudo))
        {
            return fallback;
        }

        try
        {
            var erro = JsonSerializer.Deserialize<ErroOllamaDto>(conteudo, JsonOptions);
            if (!string.IsNullOrWhiteSpace(erro?.Error))
            {
                return erro.Error.ReplaceLineEndings(" ").Trim();
            }
        }
        catch
        {
            // ignora e usa texto bruto
        }

        return conteudo.ReplaceLineEndings(" ").Trim();
    }

    private sealed class RespostaTagsOllamaDto
    {
        public List<ItemModeloOllamaDto>? Models { get; init; }
    }

    private sealed class ItemModeloOllamaDto
    {
        public string? Name { get; init; }
        public string? Model { get; init; }
    }

    private sealed class ErroOllamaDto
    {
        public string? Error { get; init; }
    }
}

internal sealed record StatusAplicacaoDto(string Modelo, int QuantidadeProjetos, int QuantidadeSecoes);

internal sealed record DocumentoDto(string Identificador, string Nome, string Arquivo, int QuantidadeSecoes);

internal sealed record DocumentoUploadResultadoDto(string Arquivo, string Mensagem);

internal sealed record SecaoUtilizadaDto(string Projeto, string Titulo);

internal sealed record RespostaChatDto(
    string Resposta,
    IReadOnlyList<SecaoUtilizadaDto> SecoesUtilizadas,
    IReadOnlyList<string> PerguntasSugeridas,
    CoberturaDocumentalDto Cobertura);

internal sealed record ConfiguracaoOllamaDto(string Endpoint, string Modelo);

internal sealed record TesteConexaoOllamaDto(
    string Endpoint,
    string Modelo,
    string Mensagem,
    IReadOnlyList<string> ModelosDisponiveis);

internal sealed record ConfiguracaoOllamaResultadoDto(
    string Endpoint,
    string Modelo,
    string Mensagem,
    IReadOnlyList<string> ModelosDisponiveis);
