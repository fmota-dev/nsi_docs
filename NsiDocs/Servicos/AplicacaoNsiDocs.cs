using System.Text;
using AgentesFramework.Agentes;
using AgentesFramework.Configuracoes;
using AgentesFramework.Modelos;
using Microsoft.SemanticKernel;

namespace AgentesFramework.Servicos;

internal sealed class AplicacaoNsiDocs
{
    private readonly ConfiguracaoAplicacao _configuracao;
    private readonly CarregadorDocumentacao _carregadorDocumentacao;
    private readonly Kernel _kernel;
    private readonly SemaphoreSlim _semaforo = new(1, 1);
    private List<ProjetoDocumentacao> _projetos = [];
    private OrquestradorConsulta? _orquestradorConsulta;

    public AplicacaoNsiDocs(
        ConfiguracaoAplicacao configuracao,
        CarregadorDocumentacao carregadorDocumentacao,
        Kernel kernel)
    {
        _configuracao = configuracao;
        _carregadorDocumentacao = carregadorDocumentacao;
        _kernel = kernel;
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

    public async Task<IReadOnlyList<DocumentoDto>> ListarDocumentosAsync(CancellationToken cancellationToken = default)
    {
        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            return _projetos
                .OrderBy(projeto => projeto.Nome)
                .Select(projeto => new DocumentoDto(
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

    public async Task<RespostaChatDto> PerguntarAsync(string pergunta, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pergunta))
        {
            throw new ArgumentException("A pergunta nao pode ser vazia.", nameof(pergunta));
        }

        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            if (_orquestradorConsulta is null)
            {
                throw new InvalidOperationException("A aplicacao ainda nao foi inicializada.");
            }

            var resultado = await _orquestradorConsulta.ProcessarAsync(pergunta).WaitAsync(cancellationToken);

            return new RespostaChatDto(
                resultado.RespostaFinal,
                resultado.SecoesUtilizadas
                    .Select(secao => new SecaoUtilizadaDto(secao.Projeto, secao.Titulo))
                    .ToList());
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

            var recuperadorContexto = new RecuperadorContexto();
            var fabricaAgentes = new FabricaAgentes(_kernel, _projetos, _configuracao);

            _orquestradorConsulta = new OrquestradorConsulta(
                recuperadorContexto,
                fabricaAgentes,
                _configuracao);
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
}

internal sealed record StatusAplicacaoDto(string Modelo, int QuantidadeProjetos, int QuantidadeSecoes);

internal sealed record DocumentoDto(string Nome, string Arquivo, int QuantidadeSecoes);

internal sealed record DocumentoUploadResultadoDto(string Arquivo, string Mensagem);

internal sealed record SecaoUtilizadaDto(string Projeto, string Titulo);

internal sealed record RespostaChatDto(string Resposta, IReadOnlyList<SecaoUtilizadaDto> SecoesUtilizadas);
