using Microsoft.AspNetCore.Http.Features;
using NsiDocs;
using NsiDocs.Configuracoes;
using NsiDocs.Servicos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});

builder.Services.AddSingleton(_ => ConfiguracaoAplicacao.Carregar());
builder.Services.AddSingleton<ParserSecoesMarkdown>();
builder.Services.AddSingleton<CarregadorDocumentacao>();
builder.Services.AddSingleton<RecuperadorContexto>();
builder.Services.AddSingleton<AvaliadorCoberturaConsulta>();
builder.Services.AddSingleton<GeradorSugestoesPerguntas>();
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

app.MapGet("/api/ollama/configuracao", async (AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    var configuracao = await aplicacao.ObterConfiguracaoOllamaAsync(cancellationToken);
    return Results.Ok(configuracao);
});

app.MapPost("/api/ollama/testar-conexao", async (ConfiguracaoOllamaRequestDto request, AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    try
    {
        var resultado = await aplicacao.TestarConexaoOllamaAsync(
            (request.Endpoint ?? string.Empty).Trim(),
            (request.Modelo ?? string.Empty).Trim(),
            cancellationToken);

        return Results.Ok(resultado);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErroRespostaDto(ex.Message));
    }
});

app.MapPost("/api/ollama/conectar", async (ConfiguracaoOllamaRequestDto request, AplicacaoNsiDocs aplicacao, CancellationToken cancellationToken) =>
{
    try
    {
        var resultado = await aplicacao.SalvarConfiguracaoOllamaAsync(
            (request.Endpoint ?? string.Empty).Trim(),
            (request.Modelo ?? string.Empty).Trim(),
            cancellationToken);

        return Results.Ok(resultado);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErroRespostaDto(ex.Message));
    }
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
        var resposta = await aplicacao.PerguntarAsync(
            request.Pergunta.Trim(),
            request.DocumentosSelecionados,
            cancellationToken);
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

namespace NsiDocs
{
    internal sealed record PerguntaRequestDto(string Pergunta, IReadOnlyList<string>? DocumentosSelecionados);

    internal sealed record ConfiguracaoOllamaRequestDto(string Endpoint, string Modelo);

    internal sealed record ErroRespostaDto(string Mensagem);
}
