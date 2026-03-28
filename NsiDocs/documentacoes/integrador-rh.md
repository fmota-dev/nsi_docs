# Integrador RH

## Objetivo

O Integrador RH sincroniza dados de colaboradores entre sistemas internos e fornecedores externos.

## Stack e Tecnologias

### Processamento

- Worker Service em .NET 8
- C#
- Dapper
- SQL Server

### Mensageria e Integrações

- RabbitMQ para filas de sincronizacao
- API REST do fornecedor de folha
- API interna de pessoas

## Fluxo Operacional

1. Ler eventos pendentes no banco.
2. Montar payload padronizado.
3. Publicar mensagem na fila.
4. Persistir retorno e auditoria.

## Monitoramento

- Health checks
- Logs estruturados
- Alertas por e-mail para falhas consecutivas
