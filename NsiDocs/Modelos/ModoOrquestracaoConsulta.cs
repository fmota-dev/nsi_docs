namespace NsiDocs.Modelos;

internal enum ModoOrquestracaoConsulta
{
    QuatroAgentes,
    TresAgentes,
    DoisAgentes,
    UmAgente
}

internal static class ModoOrquestracaoConsultaHelper
{
    public static ModoOrquestracaoConsulta Normalizar(string? valor)
    {
        var modo = valor?.Trim().ToLowerInvariant();
        return modo switch
        {
            "tres_agentes" or "3" => ModoOrquestracaoConsulta.TresAgentes,
            "dois_agentes" or "2" => ModoOrquestracaoConsulta.DoisAgentes,
            "um_agente" or "1" => ModoOrquestracaoConsulta.UmAgente,
            _ => ModoOrquestracaoConsulta.TresAgentes
        };
    }

    public static string Descrever(this ModoOrquestracaoConsulta modo)
    {
        return modo switch
        {
            ModoOrquestracaoConsulta.TresAgentes => "3 agentes",
            ModoOrquestracaoConsulta.DoisAgentes => "2 agentes",
            ModoOrquestracaoConsulta.UmAgente => "1 agente",
            _ => "4 agentes"
        };
    }
}
