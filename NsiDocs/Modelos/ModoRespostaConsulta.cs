namespace NsiDocs.Modelos;

internal enum ModoRespostaConsulta
{
    Curta,
    Normal,
    Detalhada
}

internal static class ModoRespostaConsultaHelper
{
    public static ModoRespostaConsulta Normalizar(string? valor)
    {
        var modo = valor?.Trim().ToLowerInvariant();
        return modo switch
        {
            "curta" => ModoRespostaConsulta.Curta,
            "detalhada" => ModoRespostaConsulta.Detalhada,
            _ => ModoRespostaConsulta.Normal
        };
    }

    public static string ParaApi(this ModoRespostaConsulta modo)
    {
        return modo switch
        {
            ModoRespostaConsulta.Curta => "curta",
            ModoRespostaConsulta.Detalhada => "detalhada",
            _ => "normal"
        };
    }

    public static string DescreverContratoResposta(this ModoRespostaConsulta modo, PerfilPerguntaConsulta perfil)
    {
        return (perfil, modo) switch
        {
            (PerfilPerguntaConsulta.Factual, ModoRespostaConsulta.Curta) =>
                """
                Modo curto.
                - Use a mesma resposta direta dos outros modos.
                - Depois dela, use no maximo 2 bullets curtos.
                - Priorize identificacao direta, tecnologia, metodo ou conclusao principal.
                """,
            (PerfilPerguntaConsulta.Factual, ModoRespostaConsulta.Detalhada) =>
                """
                Modo detalhado.
                - Use a mesma resposta direta dos outros modos.
                - Depois dela, detalhe em 4 a 6 bullets curtos.
                - Acrescente contexto util, como fluxo, endpoint, header, claims, stack ou observacoes tecnicas, se estiverem no contexto.
                """,
            (PerfilPerguntaConsulta.Factual, _) =>
                """
                Modo normal.
                - Use a mesma resposta direta dos outros modos.
                - Depois dela, use de 3 a 4 bullets objetivos.
                - Cubra apenas os grupos essenciais sustentados pelo contexto.
                """,
            (_, ModoRespostaConsulta.Curta) =>
                "Resposta curta. Entregue so o essencial, com foco em leitura rapida e no maximo um bloco curto de bullets.",
            (_, ModoRespostaConsulta.Detalhada) =>
                "Resposta detalhada. Cubra os grupos tecnicos principais com mais contexto e organizacao, sem perder objetividade.",
            _ =>
                "Resposta normal. Equilibre concisao e detalhamento, priorizando clareza e boa estrutura."
        };
    }

    public static string DescreverContratoFormatacao(this ModoRespostaConsulta modo, PerfilPerguntaConsulta perfil, int limiteLinhasPadrao)
    {
        return (perfil, modo) switch
        {
            (PerfilPerguntaConsulta.Factual, ModoRespostaConsulta.Curta) =>
                """
                Preserve a resposta direta na primeira linha.
                Formate em markdown compacto, com no maximo 12 linhas.
                Se usar bullets, limite a 2 itens curtos apos a resposta direta.
                """,
            (PerfilPerguntaConsulta.Factual, ModoRespostaConsulta.Detalhada) =>
                $"""
                Preserve a resposta direta na primeira linha.
                Formate em markdown organizado, mantendo a mesma base factual dos outros modos.
                Pode expandir com bullets e subtitulos curtos, aproveitando ate {limiteLinhasPadrao} linhas.
                """,
            (PerfilPerguntaConsulta.Factual, _) =>
                """
                Preserve a resposta direta na primeira linha.
                Formate em markdown claro, normalmente entre 10 e 18 linhas.
                Depois da resposta direta, organize os fatos essenciais em bullets curtos.
                """,
            (_, ModoRespostaConsulta.Curta) =>
                "Formate de modo compacto, com no maximo 12 linhas e listas curtas quando fizer sentido.",
            (_, ModoRespostaConsulta.Detalhada) =>
                $"Formate de modo mais completo, aproveitando ate {limiteLinhasPadrao} linhas quando houver conteudo suficiente.",
            _ =>
                "Formate em modo equilibrado, normalmente entre 18 e 28 linhas quando o conteudo justificar."
        };
    }
}
