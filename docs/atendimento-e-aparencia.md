# Atendimento e aparência

## Enviar e fechar

Na conversa do chamado, **Enviar** publica o comentário e mantém o status. **Enviar e fechar** publica o mesmo comentário e muda o chamado para **Fechado** em uma única operação. A ação serve tanto para respostas públicas quanto para notas internas da equipe.

- Técnicos e gestores podem fechar chamados não terminais dentro da sua visibilidade.
- O solicitante pode enviar uma confirmação e fechar seu próprio chamado quando ele estiver resolvido.
- Chamados fechados ou cancelados não aceitam novos comentários.
- A opção aparece conforme `allowedNextStatuses`, calculado pela API. A autorização também é verificada no servidor.
- Texto vazio ou acima de 10.000 caracteres é recusado. Durante o envio, os controles ficam desabilitados; se houver erro, o rascunho é preservado e uma mensagem aparece.
- Uma nota interna continua invisível ao solicitante e não conta como primeira resposta pública.

O contrato existente `POST /api/tickets/{id}/comments` recebe o campo opcional `closeTicket` (padrão `false`):

```json
{
  "content": "Acesso restabelecido. Atendimento concluído.",
  "isInternal": false,
  "closeTicket": true
}
```

A resposta de sucesso continua sendo `201` com o comentário. Permissão de fechamento inválida retorna `409`, assim como comentários em chamados terminais; recurso fora da visibilidade retorna `404`; nota interna de solicitante retorna `403`; texto inválido retorna `400`. Nenhum desses erros de validação grava o comentário ou fecha o chamado.

Comentário, status, marcos de SLA e histórico são persistidos pelo mesmo `SaveChangesAsync`, na transação do EF Core. Os efeitos de SLA são compartilhados com a mudança de status: a pausa é encerrada, `ClosedAt` é preenchido e `ResolvedAt` é preenchido apenas se ainda estiver vazio. O interceptor registra o comentário e o fechamento com a transição real, sem simular etapas intermediárias. Não há mudança de schema nem migration.

Depois do envio, conversa, detalhe, histórico, filas e dashboard são invalidados para recarregar os dados atuais. As transições também estão documentadas na seção 5.8 do README.

## Interface

A experiência aproxima o atendimento de uma central de suporte: navegação lateral no desktop, navegação compacta no celular, cabeçalho com acesso rápido a novo chamado e superfícies de cartões sobre fundo suave.

**Listagem.** Cada chamado é um cartão, e o cartão inteiro é o link. A prioridade aparece duas vezes por decisão: no rótulo escrito e numa faixa colorida à esquerda, em posição fixa, para que a fila possa ser varrida pela borda. Abaixo dos metadados ficam os dois prazos de SLA, coloridos por estado — cumprido, a vencer, vencido —, e chamados fechados ou cancelados não mostram prazo, porque saem dos indicadores. Os filtros de status são alternadores de múltipla escolha, com os atalhos **Sem responsável** e **Vencidos** para a equipe; os filtros ativos são listados com remoção individual e um **Limpar tudo**. Tudo continua na URL.

**Detalhe.** O cabeçalho reúne código, status, prioridade, título e a linha de quem abriu e quem atende. Conversa e histórico dividem o mesmo espaço em abas, em vez de empilhar cartões que empurram as ações para fora da tela. Gerenciamento, SLA e propriedades ficam ao lado em telas largas.

A descrição de abertura é a primeira mensagem da conversa, marcada como abertura, e não um cartão separado acima dela: quem atende lê o chamado em ordem, num lugar só. Cada mensagem traz avatar com as iniciais do autor, colorido conforme o perfil, para que se veja de relance se a última palavra foi da equipe ou do solicitante.

O editor oferece **Resposta pública** e **Nota interna**, com indicação explícita de quem poderá ler. A opção de nota interna só aparece para a equipe. Histórico recebe uma apresentação de linha do tempo, e textos longos da conversa e descrição quebram para caber na tela.

As transições viram botões com o nome da ação — **Enviar para triagem**, **Iniciar atendimento**, **Aguardar solicitante**, **Marcar como resolvido** —, e não um seletor com o nome do estado. A lista de botões continua vindo de `allowedNextStatuses`. Fechar e cancelar pedem um segundo clique de confirmação no próprio botão.

Trata-se de uma aproximação visual e do fluxo de atendimento, sem equivalência completa de funcionalidades ao Freshdesk. Respostas são comentários no portal; esta mudança não implementa envio de e-mail, anexos ou editor de texto rico.

## Temas

O seletor está no cabeçalho e nas telas de entrada e cadastro:

- **Claro**: mantém o tema claro.
- **Escuro**: mantém o tema escuro, incluindo formulários, tabelas e gráficos.
- **Sistema**: acompanha a preferência do dispositivo e suas alterações; é o padrão inicial.

A escolha fica em `localStorage`, na chave `opsdesk-theme`, e é sincronizada entre abas. O tema é aplicado antes da renderização inicial para evitar o clarão do tema oposto. Se o armazenamento estiver indisponível, o seletor continua funcionando enquanto a tela estiver montada, sem persistência após recarregar. Não é uma preferência da conta compartilhada entre dispositivos.

## Validação

Os testes de domínio cobrem o grafo e o fechamento direto restrito à equipe. Os testes de integração em `TicketWorkflowTests` cobrem envio e fechamento nos cinco status não terminais, notas internas, SLA pausado, preservação da data de resolução, histórico, confirmação pelo solicitante, texto vazio, estados terminais e recursos fora da visibilidade.

```bash
dotnet test backend/OpsDesk.slnx
cd frontend
npm ci
npm run build
npm run lint
```

Requer .NET 10, Node compatível com `frontend/package.json` e Docker para os testes de integração.

Roteiro de revisão manual:

1. Como técnico, enviar resposta comum e verificar que o status permanece.
2. Enviar e fechar um chamado em espera; conferir comentário, histórico, status fechado e fim da pausa de SLA.
3. Repetir com nota interna e verificar, como solicitante, que o texto não aparece.
4. Como solicitante, confirmar que a ação só aparece após resolução.
5. Simular erro da API e conferir a preservação do rascunho.
6. Alternar os três temas, recarregar e abrir outra aba. Em Sistema, alterar o tema do dispositivo.
7. Conferir lista, conversa, dashboard, entrada e cadastro em tela larga e celular, com navegação por teclado.
