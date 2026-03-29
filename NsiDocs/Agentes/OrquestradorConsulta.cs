using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using Microsoft.SemanticKernel.Agents;
using NsiDocs.Configuracoes;
using NsiDocs.Modelos;
using NsiDocs.Servicos;

namespace NsiDocs.Agentes;

internal sealed class OrquestradorConsulta
{
    private readonly RecuperadorContexto _recuperadorContexto;
    private readonly FabricaAgentes _fabricaAgentes;
    private readonly ConfiguracaoAplicacao _configuracao;

    public OrquestradorConsulta(
        RecuperadorContexto recuperadorContexto,
        FabricaAgentes fabricaAgentes,
        ConfiguracaoAplicacao configuracao)
    {
        _recuperadorContexto = recuperadorContexto;
        _fabricaAgentes = fabricaAgentes;
        _configuracao = configuracao;
    }

    public Task<ResultadoConsulta> ProcessarAsync(
        string pergunta,
        ModoRespostaConsulta modoResposta = ModoRespostaConsulta.Normal,
        CancellationToken cancellationToken = default)
    {
        return ProcessarInternoAsync(pergunta, modoResposta, null, null, cancellationToken);
    }

    public Task<ResultadoConsulta> ProcessarStreamingAsync(
        string pergunta,
        ModoRespostaConsulta modoResposta,
        Func<string, CancellationToken, Task> publicarStatus,
        Func<string, CancellationToken, Task> publicarChunk,
        CancellationToken cancellationToken = default)
    {
        return ProcessarInternoAsync(pergunta, modoResposta, publicarStatus, publicarChunk, cancellationToken);
    }

    private async Task<ResultadoConsulta> ProcessarInternoAsync(
        string pergunta,
        ModoRespostaConsulta modoResposta,
        Func<string, CancellationToken, Task>? publicarStatus,
        Func<string, CancellationToken, Task>? publicarChunk,
        CancellationToken cancellationToken)
    {
        var perfilPergunta = PerfilPerguntaConsultaHelper.Classificar(pergunta);
        var modoOrquestracao = _configuracao.ModoOrquestracao;
        var planejador = modoOrquestracao is ModoOrquestracaoConsulta.UmAgente
            ? null
            : _fabricaAgentes.CriarPlanejador();
        var analista = modoOrquestracao is ModoOrquestracaoConsulta.QuatroAgentes or ModoOrquestracaoConsulta.TresAgentes
            ? _fabricaAgentes.CriarAnalistaContexto()
            : null;
        var respondedor = modoOrquestracao is ModoOrquestracaoConsulta.QuatroAgentes
            ? _fabricaAgentes.CriarRespondedor()
            : null;
        var respondedorFinal = modoOrquestracao is ModoOrquestracaoConsulta.QuatroAgentes
            ? null
            : _fabricaAgentes.CriarRespondedorFinal();
        var formatador = modoOrquestracao is ModoOrquestracaoConsulta.QuatroAgentes
            ? _fabricaAgentes.CriarFormatador()
            : null;

        var planoConsulta = await ObterPlanoConsultaAsync(
            pergunta,
            modoOrquestracao,
            planejador,
            publicarStatus,
            cancellationToken);

        List<SecaoDocumento> secoesRecuperadas;
        await PublicarStatusAsync(publicarStatus, "buscando trechos relevantes", cancellationToken);
        try
        {
            secoesRecuperadas = await _fabricaAgentes.BuscarSecoesRelevantesComPluginAsync(
                pergunta,
                planoConsulta,
                _configuracao.QuantidadeSecoesRecuperadas,
                cancellationToken);
        }
        catch
        {
            secoesRecuperadas = _recuperadorContexto.RecuperarSecoes(
                _fabricaAgentes.ObterProjetos(),
                planoConsulta,
                pergunta,
                _configuracao.QuantidadeSecoesRecuperadas);
        }

        var secoesUtilizadas = secoesRecuperadas
            .Take(_configuracao.QuantidadeSecoesUtilizadas)
            .ToList();

        if (secoesUtilizadas.Count == 0)
        {
            await PublicarStatusAsync(publicarStatus, "nenhum trecho relevante encontrado", cancellationToken);
            return new ResultadoConsulta
            {
                RespostaFinal = "Nao encontrei trechos relevantes na documentacao.",
                SecoesUtilizadas = []
            };
        }

        var contextoRecuperado = _recuperadorContexto.MontarContexto(secoesUtilizadas);

        string respostaTecnica;
        string respostaFinal;
        var aplicarFiltroProfundo = modoOrquestracao is ModoOrquestracaoConsulta.QuatroAgentes;

        switch (modoOrquestracao)
        {
            case ModoOrquestracaoConsulta.QuatroAgentes:
            {
                await PublicarStatusAsync(publicarStatus, "consolidando contexto", cancellationToken);
                var contextoConsolidado = await ExecutorAgente.ObterRespostaAsync(
                    analista!,
                    $"""
                     Pergunta:
                     {pergunta}

                     Perfil da pergunta:
                     {perfilPergunta}

                     Plano de consulta:
                     {FormatarPlanoConsulta(planoConsulta)}

                     Trechos recuperados:
                     {contextoRecuperado}
                     """,
                    _configuracao.TimeoutAnalista,
                    cancellationToken);

                await PublicarStatusAsync(publicarStatus, "redigindo resposta tecnica", cancellationToken);
                var contratoModoRespondedor = perfilPergunta is PerfilPerguntaConsulta.Factual
                    ? modoResposta.DescreverContratoResposta(perfilPergunta)
                    : """
                      Nesta etapa, ignore diferencas entre curta, normal e detalhada.
                      Gere uma base tecnica completa, estavel e bem ancorada no contexto.
                      O detalhamento final sera ajustado apenas na etapa de formatacao.
                      """;
                var moldeRespondedor = perfilPergunta is PerfilPerguntaConsulta.Factual
                    ? perfilPergunta.DescreverMoldeResposta(modoResposta)
                    : perfilPergunta.DescreverMoldeResposta(ModoRespostaConsulta.Normal);

                respostaTecnica = await ExecutorAgente.ObterRespostaAsync(
                    respondedor!,
                    $"""
                     Perfil da pergunta:
                     {perfilPergunta}

                     Contrato do perfil:
                     {perfilPergunta.DescreverContratoResposta()}

                     Contrato do modo:
                     {contratoModoRespondedor}

                     Molde de saida esperado:
                     {moldeRespondedor}

                     Pergunta do usuario:
                     {pergunta}

                     Contexto consolidado:
                     {contextoConsolidado}

                     Trechos originais recuperados:
                     {contextoRecuperado}
                     """,
                    _configuracao.TimeoutRespondedor,
                    cancellationToken);

                await PublicarStatusAsync(publicarStatus, "formatando resposta final", cancellationToken);
                respostaFinal = publicarChunk is null
                    ? await ExecutorAgente.ObterRespostaAsync(
                        formatador!,
                        $"""
                         Perfil da pergunta:
                         {perfilPergunta}

                         Modo de saida:
                         {modoResposta.DescreverContratoFormatacao(perfilPergunta, _configuracao.LimiteLinhasFormatador)}

                         Resposta tecnica:
                         {respostaTecnica}
                         """,
                        _configuracao.TimeoutFormatador,
                        cancellationToken)
                    : await ExecutorAgente.ObterRespostaStreamingAsync(
                        formatador!,
                        $"""
                         Perfil da pergunta:
                         {perfilPergunta}

                         Modo de saida:
                         {modoResposta.DescreverContratoFormatacao(perfilPergunta, _configuracao.LimiteLinhasFormatador)}

                         Resposta tecnica:
                         {respostaTecnica}
                         """,
                        _configuracao.TimeoutFormatador,
                        publicarChunk,
                        cancellationToken);
                break;
            }

            case ModoOrquestracaoConsulta.TresAgentes:
            {
                await PublicarStatusAsync(publicarStatus, "consolidando contexto", cancellationToken);
                var contextoConsolidado = await ExecutorAgente.ObterRespostaAsync(
                    analista!,
                    $"""
                     Pergunta:
                     {pergunta}

                     Perfil da pergunta:
                     {perfilPergunta}

                     Plano de consulta:
                     {FormatarPlanoConsulta(planoConsulta)}

                     Trechos recuperados:
                     {contextoRecuperado}
                     """,
                    _configuracao.TimeoutAnalista,
                    cancellationToken);

                await PublicarStatusAsync(publicarStatus, "redigindo resposta final", cancellationToken);
                respostaFinal = await ObterRespostaFinalDiretaAsync(
                    respondedorFinal!,
                    perfilPergunta,
                    modoResposta,
                    pergunta,
                    planoConsulta,
                    contextoRecuperado,
                    contextoConsolidado,
                    publicarChunk,
                    cancellationToken);
                respostaTecnica = respostaFinal;
                break;
            }

            case ModoOrquestracaoConsulta.DoisAgentes:
            case ModoOrquestracaoConsulta.UmAgente:
            {
                await PublicarStatusAsync(publicarStatus, "redigindo resposta final", cancellationToken);
                respostaFinal = await ObterRespostaFinalDiretaAsync(
                    respondedorFinal!,
                    perfilPergunta,
                    modoResposta,
                    pergunta,
                    planoConsulta,
                    contextoRecuperado,
                    null,
                    publicarChunk,
                    cancellationToken);
                respostaTecnica = respostaFinal;
                break;
            }

            default:
                throw new InvalidOperationException("Modo de orquestracao nao suportado.");
        }

        respostaFinal = AjustarRespostaFinal(
            perfilPergunta,
            modoResposta,
            respostaTecnica,
            respostaFinal,
            contextoRecuperado,
            aplicarFiltroProfundo);

        return new ResultadoConsulta
        {
            RespostaFinal = respostaFinal,
            SecoesUtilizadas = secoesUtilizadas
        };
    }

    private static Task PublicarStatusAsync(
        Func<string, CancellationToken, Task>? publicarStatus,
        string mensagem,
        CancellationToken cancellationToken)
    {
        return publicarStatus is null
            ? Task.CompletedTask
            : publicarStatus(mensagem, cancellationToken);
    }

    private async Task<PlanoConsulta> ObterPlanoConsultaAsync(
        string pergunta,
        ModoOrquestracaoConsulta modoOrquestracao,
        ChatCompletionAgent? planejador,
        Func<string, CancellationToken, Task>? publicarStatus,
        CancellationToken cancellationToken)
    {
        if (modoOrquestracao is ModoOrquestracaoConsulta.UmAgente)
        {
            await PublicarStatusAsync(publicarStatus, "planejando consulta localmente", cancellationToken);
            return CriarPlanoConsultaLocal(pergunta);
        }

        await PublicarStatusAsync(publicarStatus, "planejando consulta", cancellationToken);
        var respostaPlanejador = await ExecutorAgente.ObterRespostaAsync(
            planejador!,
            pergunta,
            _configuracao.TimeoutPlanejador,
            cancellationToken);

        return InterpretarPlanoConsulta(respostaPlanejador);
    }

    private async Task<string> ObterRespostaFinalDiretaAsync(
        ChatCompletionAgent respondedorFinal,
        PerfilPerguntaConsulta perfilPergunta,
        ModoRespostaConsulta modoResposta,
        string pergunta,
        PlanoConsulta planoConsulta,
        string contextoRecuperado,
        string? contextoConsolidado,
        Func<string, CancellationToken, Task>? publicarChunk,
        CancellationToken cancellationToken)
    {
        var prompt = $"""
                      Perfil da pergunta:
                      {perfilPergunta}

                      Contrato do perfil:
                      {perfilPergunta.DescreverContratoResposta()}

                      Contrato do modo:
                      {modoResposta.DescreverContratoResposta(perfilPergunta)}

                      Molde de saida esperado:
                      {perfilPergunta.DescreverMoldeResposta(modoResposta)}

                      Contrato de formatacao final:
                      {modoResposta.DescreverContratoFormatacao(perfilPergunta, _configuracao.LimiteLinhasFormatador)}

                      Pergunta do usuario:
                      {pergunta}

                      Plano de consulta:
                      {FormatarPlanoConsulta(planoConsulta)}

                      """;

        if (!string.IsNullOrWhiteSpace(contextoConsolidado))
        {
            prompt += $"""
                       
                       Contexto consolidado:
                       {contextoConsolidado}
                       """;
        }

        prompt += $"""

                    Trechos originais recuperados:
                    {contextoRecuperado}
                    """;

        return publicarChunk is null
            ? await ExecutorAgente.ObterRespostaAsync(
                respondedorFinal,
                prompt,
                _configuracao.TimeoutRespondedor,
                cancellationToken)
            : await ExecutorAgente.ObterRespostaStreamingAsync(
                respondedorFinal,
                prompt,
                _configuracao.TimeoutRespondedor,
                publicarChunk,
                cancellationToken);
    }

    private PlanoConsulta CriarPlanoConsultaLocal(string pergunta)
    {
        var perguntaNormalizada = NormalizarParaComparacao(pergunta);
        var projetoAlvo = _fabricaAgentes
            .ObterProjetos()
            .FirstOrDefault(projeto => ProjetoFoiMencionado(perguntaNormalizada, projeto));

        return new PlanoConsulta
        {
            ProjetoAlvo = projetoAlvo?.Nome ?? "todos",
            Temas = ExtrairTemasLocais(pergunta, projetoAlvo).ToList(),
            Objetivo = pergunta.Trim()
        };
    }

    private static bool ProjetoFoiMencionado(string perguntaNormalizada, ProjetoDocumentacao projeto)
    {
        return new[]
            {
                projeto.Nome,
                projeto.Identificador,
                Path.GetFileNameWithoutExtension(projeto.Arquivo)
            }
            .Where(valor => !string.IsNullOrWhiteSpace(valor))
            .Select(NormalizarParaComparacao)
            .Any(candidato =>
                !string.IsNullOrWhiteSpace(candidato) &&
                (perguntaNormalizada.Contains(candidato, StringComparison.Ordinal)
                 || perguntaNormalizada.Contains(candidato.Replace("-", " "), StringComparison.Ordinal)));
    }

    private static IEnumerable<string> ExtrairTemasLocais(string pergunta, ProjetoDocumentacao? projetoAlvo)
    {
        var stopwords = new HashSet<string>(StringComparer.Ordinal)
        {
            "qual", "quais", "como", "para", "com", "sem", "sobre", "detalhe", "explique",
            "lista", "liste", "projeto", "modo", "usada", "usado", "funciona", "hub", "de",
            "do", "da", "dos", "das", "uma", "umas", "uns", "que", "esta", "esse"
        };

        var nomesProjeto = new HashSet<string>(StringComparer.Ordinal);
        if (projetoAlvo is not null)
        {
            foreach (var valor in new[] { projetoAlvo.Nome, projetoAlvo.Identificador, Path.GetFileNameWithoutExtension(projetoAlvo.Arquivo) })
            {
                if (string.IsNullOrWhiteSpace(valor))
                {
                    continue;
                }

                nomesProjeto.Add(NormalizarParaComparacao(valor));
            }
        }

        return Regex.Matches(NormalizarParaComparacao(pergunta), "[a-z0-9_./-]{4,}")
            .Select(match => match.Value)
            .Where(token => !stopwords.Contains(token) && !nomesProjeto.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .Take(6);
    }

    private static string FormatarPlanoConsulta(PlanoConsulta planoConsulta)
    {
        return $"""
                Projeto: {planoConsulta.ProjetoAlvo}
                Temas: {string.Join(", ", planoConsulta.Temas)}
                Objetivo: {planoConsulta.Objetivo}
                """;
    }

    private static PlanoConsulta InterpretarPlanoConsulta(string respostaPlanejador)
    {
        var projeto = ExtrairValor(respostaPlanejador, "Projeto") ?? "todos";
        var temas = (ExtrairValor(respostaPlanejador, "Temas") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var objetivo = ExtrairValor(respostaPlanejador, "Objetivo") ?? string.Empty;

        return new PlanoConsulta
        {
            ProjetoAlvo = string.IsNullOrWhiteSpace(projeto) ? "todos" : projeto,
            Temas = temas,
            Objetivo = objetivo
        };
    }

    private static string? ExtrairValor(string texto, string campo)
    {
        var match = Regex.Match(
            texto,
            $@"^{campo}\s*:\s*(.+)$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string AjustarRespostaFinal(
        PerfilPerguntaConsulta perfilPergunta,
        ModoRespostaConsulta modoResposta,
        string respostaTecnica,
        string respostaFinal,
        string contextoRecuperado,
        bool aplicarFiltroProfundo)
    {
        if (perfilPergunta is not PerfilPerguntaConsulta.Factual)
        {
            if ((perfilPergunta is PerfilPerguntaConsulta.Explicativa or PerfilPerguntaConsulta.Comparativa)
                && (ParecePayloadCru(respostaFinal) || RespostaSemEstrutura(respostaFinal)))
            {
                return LimparMarkdownIncompleto(respostaTecnica.Trim());
            }

            return LimparMarkdownIncompleto(respostaFinal.Trim());
        }

        if (string.IsNullOrWhiteSpace(respostaFinal))
        {
            return LimparMarkdownIncompleto(respostaTecnica.Trim());
        }

        var linhas = respostaFinal
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var precisaEstrutura = modoResposta is not ModoRespostaConsulta.Curta;
        var temEstrutura = respostaFinal.Contains("- ", StringComparison.Ordinal)
            || respostaFinal.Contains("##", StringComparison.Ordinal)
            || respostaFinal.Contains("###", StringComparison.Ordinal);
        var perdeuRespostaDireta = respostaTecnica.Contains("Resposta direta", StringComparison.OrdinalIgnoreCase)
            && !respostaFinal.Contains("Resposta direta", StringComparison.OrdinalIgnoreCase);
        var muitoCurta = respostaFinal.Length < (precisaEstrutura ? 80 : 40)
            || linhas.Length < (precisaEstrutura ? 3 : 2);

        if ((precisaEstrutura && (!temEstrutura || muitoCurta)) || (perdeuRespostaDireta && muitoCurta))
        {
            return LimparMarkdownIncompleto(respostaTecnica.Trim());
        }

        var respostaLimpa = LimparMarkdownIncompleto(respostaFinal.Trim());
        if (!aplicarFiltroProfundo)
        {
            return respostaLimpa;
        }

        var respostaFiltrada = LimparMarkdownIncompleto(FiltrarLinhasNaoAncoradas(respostaLimpa, contextoRecuperado));
        return FiltroFoiAgressivoDemais(respostaLimpa, respostaFiltrada)
            ? respostaLimpa
            : respostaFiltrada;
    }

    private static string FiltrarLinhasNaoAncoradas(string respostaFinal, string contextoRecuperado)
    {
        var tokensContexto = ExtrairTokensNormalizados(contextoRecuperado);
        if (tokensContexto.Count == 0)
        {
            return respostaFinal;
        }

        var linhas = respostaFinal.Split('\n');
        var linhasMantidas = new List<string>(linhas.Length);
        var primeiraLinhaMantida = false;

        foreach (var linhaBruta in linhas)
        {
            var linha = linhaBruta.TrimEnd('\r');
            var linhaSemEspacos = linha.Trim();

            if (!primeiraLinhaMantida && !string.IsNullOrWhiteSpace(linhaSemEspacos))
            {
                linhasMantidas.Add(linha);
                primeiraLinhaMantida = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(linhaSemEspacos))
            {
                linhasMantidas.Add(linha);
                continue;
            }

            if (linhaSemEspacos.StartsWith('#') || linhaSemEspacos.StartsWith("**") || linhaSemEspacos.StartsWith("```"))
            {
                linhasMantidas.Add(linha);
                continue;
            }

            if (linhaSemEspacos.StartsWith("-") || linhaSemEspacos.StartsWith("*"))
            {
                var sobreposicao = ExtrairTokensNormalizados(linhaSemEspacos)
                    .Count(token => tokensContexto.Contains(token));

                if (sobreposicao >= 3)
                {
                    linhasMantidas.Add(linha);
                }

                continue;
            }

            linhasMantidas.Add(linha);
        }

        var resultado = string.Join('\n', linhasMantidas).Trim();
        return string.IsNullOrWhiteSpace(resultado) ? respostaFinal : resultado;
    }

    private static bool FiltroFoiAgressivoDemais(string respostaOriginal, string respostaFiltrada)
    {
        if (string.IsNullOrWhiteSpace(respostaOriginal) || string.IsNullOrWhiteSpace(respostaFiltrada))
        {
            return false;
        }

        if (respostaFiltrada.Length >= respostaOriginal.Length)
        {
            return false;
        }

        var linhasOriginais = ContarLinhasRelevantes(respostaOriginal);
        var linhasFiltradas = ContarLinhasRelevantes(respostaFiltrada);
        var proporcaoCaracteres = (double)respostaFiltrada.Length / respostaOriginal.Length;

        return (linhasOriginais >= 6 && linhasFiltradas <= linhasOriginais - 3 && proporcaoCaracteres < 0.75)
            || (linhasOriginais >= 4 && linhasFiltradas <= linhasOriginais / 2 && proporcaoCaracteres < 0.7);
    }

    private static bool ParecePayloadCru(string respostaFinal)
    {
        var texto = respostaFinal.TrimStart();
        return texto.StartsWith('{') || texto.StartsWith('[');
    }

    private static bool RespostaSemEstrutura(string respostaFinal)
    {
        var linhas = respostaFinal
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var temEstrutura = respostaFinal.Contains("- ", StringComparison.Ordinal)
            || respostaFinal.Contains("##", StringComparison.Ordinal)
            || respostaFinal.Contains("###", StringComparison.Ordinal)
            || respostaFinal.Contains("**", StringComparison.Ordinal);

        return linhas.Length < 3 || !temEstrutura;
    }

    private static int ContarLinhasRelevantes(string markdown)
    {
        return markdown
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(linha =>
            {
                var texto = linha.Trim();
                return !string.IsNullOrWhiteSpace(texto)
                    && !EhPlaceholderVazio(texto);
            });
    }

    private static string LimparMarkdownIncompleto(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        var linhasOriginais = markdown.Split('\n');
        var linhasLimpas = new List<string>(linhasOriginais.Length);

        for (var indice = 0; indice < linhasOriginais.Length; indice++)
        {
            var linha = linhasOriginais[indice].TrimEnd('\r');
            var linhaSemEspacos = linha.Trim();

            if (EhTituloOuRotulo(linhaSemEspacos) && !PossuiConteudoApos(linhasOriginais, indice + 1))
            {
                continue;
            }

            if (EhPlaceholderVazio(linhaSemEspacos))
            {
                continue;
            }

            linhasLimpas.Add(linha);
        }

        while (linhasLimpas.Count > 0 && string.IsNullOrWhiteSpace(linhasLimpas[^1]))
        {
            linhasLimpas.RemoveAt(linhasLimpas.Count - 1);
        }

        return string.Join('\n', linhasLimpas).Trim();
    }

    private static bool PossuiConteudoApos(string[] linhas, int indiceInicial)
    {
        for (var indice = indiceInicial; indice < linhas.Length; indice++)
        {
            var linha = linhas[indice].Trim();
            if (string.IsNullOrWhiteSpace(linha))
            {
                continue;
            }

            if (EhTituloOuRotulo(linha) || EhPlaceholderVazio(linha))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool EhTituloOuRotulo(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha))
        {
            return false;
        }

        return linha.StartsWith('#')
            || Regex.IsMatch(linha, @"^(detalhes|observacoes|observações|como funciona|fluxo|pontos tecnicos|pontos técnicos|resumo)\s*:?\s*$", RegexOptions.IgnoreCase);
    }

    private static bool EhPlaceholderVazio(string linha)
    {
        return Regex.IsMatch(linha, @"^(observacoes|observações)\s*:\s*-\s*$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(linha, @"^-\s*$", RegexOptions.IgnoreCase);
    }

    private static HashSet<string> ExtrairTokensNormalizados(string texto)
    {
        var semAcentos = RemoverAcentos(texto).ToLowerInvariant();
        var correspondencias = Regex.Matches(semAcentos, "[a-z0-9_./-]{4,}");

        return correspondencias
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string NormalizarParaComparacao(string texto)
    {
        return RemoverAcentos(texto).ToLowerInvariant();
    }

    private static string RemoverAcentos(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(texto.Length);

        foreach (var caractere in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(caractere);
            }
        }

        return sb.ToString();
    }
}
