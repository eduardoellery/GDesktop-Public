# Histórico de versões

## Ajustes posteriores à 1.0.0

- Instalador Windows x64 por usuário, autocontido, com assistente em português, atalhos, desinstalação e preservação do índice pessoal. Geração reproduzível por `Criar-Instalador.ps1` usando Inno Setup.
- Ícone próprio derivado do favicon, em sete resoluções, incorporado ao executável e ao instalador. Atalhos da área de trabalho/menu Iniciar referenciam explicitamente o ícone instalado.

- Reforço da colagem no campo de busca, botão Colar, substituição da seleção e normalização de quebras de linha em espaços. Alternativa de Ctrl+V usa acesso à área de transferência apenas se já permitido pelo navegador.
- Prova isolada de Windows OCR validada com exemplos gerados e PDF digitalizado real de cinco páginas. Windows OCR agora integrado ao índice: imagens e PDFs por página, fila persistente, retomada, prioridade ao texto digital e verificação de disponibilidade OneDrive antes da leitura.

## 1.0.0 — 31/10/2026

Primeira versão funcional local para Windows x64.

- Busca por nomes e conteúdo de textos, PDFs com texto e Office moderno: DOCX, XLSX e PPTX.
- Word: corpo, tabelas, cabeçalhos, rodapés, notas e comentários; texto dividido em formatação preservado.
- Excel: nomes de planilhas, inclusive ocultas, células de texto/números/booleanos, datas comuns, fórmulas e resultados salvos. Não recalcula fórmulas.
- PowerPoint: texto dos slides, tabelas com texto e notas do apresentador, em ordem de apresentação.
- Atualização automática dos documentos Office anteriormente catalogados apenas pelo nome.
- Busca com AND, OR, NOT, grupos e frases entre aspas; filtros e trechos destacados.
- Gerenciamento de pastas, remoção e consolidação de pastas superiores sem perder resultados já indexados.
- Indexação persistente com pausa/retomada, monitoramento de alterações e extratores isolados.
- Proteção para arquivos OneDrive somente online, validada em pasta pequena com transições reais.
- Interface no navegador, favicon próprio e bandeja com indicadores de estado.

Limites conhecidos: OCR e conteúdo de DOC/XLS/PPT antigos ainda pendentes; arquivos protegidos por senha não são extraídos. Imagens/objetos incorporados, gráficos/SmartArt e formatação visual de Office não têm cobertura completa. Datas Excel usam formatos comuns; não há reprodução integral da exibição de células. Permanecem os limites de tamanho/extração e as restrições de subpastas com reparse points descritos no README. A numeração 1.0.0 identifica a entrega funcional; não implica validação exaustiva com grandes acervos ou todas as variantes Office/OneDrive.
