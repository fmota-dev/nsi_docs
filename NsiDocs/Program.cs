using AgentesFramework.Configuracoes;
using AgentesFramework.Servicos;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});

builder.Services.AddSingleton(_ => ConfiguracaoAplicacao.Carregar());
builder.Services.AddSingleton<ParserSecoesMarkdown>();
builder.Services.AddSingleton<CarregadorDocumentacao>();
builder.Services.AddSingleton<RecuperadorContexto>();
builder.Services.AddSingleton(sp =>
{
    var configuracao = sp.GetRequiredService<ConfiguracaoAplicacao>();
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(configuracao.EndpointOllama),
        Timeout = configuracao.TimeoutHttp
    };

    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddOllamaChatCompletion(
        modelId: configuracao.ModeloOllama,
        httpClient: httpClient);

    return kernelBuilder.Build();
});
builder.Services.AddSingleton<AplicacaoNsiDocs>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = contexto =>
    {
        var nomeArquivo = Path.GetFileName(contexto.File.Name);
        if (!nomeArquivo.Equals("service-worker.js", StringComparison.OrdinalIgnoreCase) &&
            !nomeArquivo.Equals("manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        contexto.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        contexto.Context.Response.Headers.Pragma = "no-cache";
        contexto.Context.Response.Headers.Expires = "0";
    }
});

app.MapGet("/api/status", async (AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    var status = await aplicacao.ObterStatusAsync(cancellationToken);
    return Results.Ok(status);
});

app.MapGet("/api/documentos", async (AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    var documentos = await aplicacao.ListarDocumentosAsync(cancellationToken);
    return Results.Ok(documentos);
});

app.MapPost("/api/documentos/recarregar", async (AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    await aplicacao.RecarregarIndiceAsync(cancellationToken);
    var status = await aplicacao.ObterStatusAsync(cancellationToken);
    return Results.Ok(status);
});

app.MapPost("/api/documentos/upload", async (HttpRequest request, AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new ErroRespostaDto("Envie o arquivo usando multipart/form-data."));
    }

    var formulario = await request.ReadFormAsync(cancellationToken);
    var arquivo = formulario.Files["arquivo"];

    if (arquivo is null || arquivo.Length == 0)
    {
        return Results.BadRequest(new ErroRespostaDto("Nenhum arquivo .md foi enviado."));
    }

    if (!arquivo.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ErroRespostaDto("Somente arquivos .md sao aceitos."));
    }

    await using var stream = arquivo.OpenReadStream();
    var resultado = await aplicacao.SalvarDocumentoAsync(stream, arquivo.FileName, cancellationToken);

    return Results.Ok(resultado);
});

app.MapPost("/api/chat/perguntar", async (PerguntaRequestDto request, AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Pergunta))
    {
        return Results.BadRequest(new ErroRespostaDto("Informe uma pergunta para consultar a documentacao."));
    }

    try
    {
        var resposta = await aplicacao.PerguntarAsync(request.Pergunta.Trim(), cancellationToken);
        return Results.Ok(resposta);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErroRespostaDto(ex.Message));
    }
});

var aplicacaoNsiDocs = app.Services.GetRequiredService<AplicacaoNsiDocs>();
await aplicacaoNsiDocs.InicializarAsync();

app.Run();

internal sealed record PerguntaRequestDto(string Pergunta);

internal sealed record ErroRespostaDto(string Mensagem);


