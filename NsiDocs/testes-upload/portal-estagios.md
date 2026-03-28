# Portal de Estagios

## Visao Geral

O Portal de Estagios centraliza vagas, candidaturas e acompanhamento de status para alunos e empresas conveniadas.

## Stack e Tecnologias

### Back-end

- ASP.NET Core 8 Web API
- C#
- Entity Framework Core
- SQL Server 2019
- FluentValidation

### Front-end

- HTML
- CSS
- JavaScript
- Bootstrap 5

## Integracoes

- API interna de alunos para consultar matricula e status academico
- Servico de e-mail institucional para notificacoes de candidatura
- Active Directory apenas para autenticacao do time administrativo

## Seguranca

- Autenticacao via JWT para usuarios externos
- Perfis separados para aluno, empresa e administrador

## Observabilidade

- Logs estruturados com Serilog
- Dashboard simples no Grafana para erros de candidatura
