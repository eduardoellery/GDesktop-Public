# Prova técnica — Windows OCR

Experimento separado da versão 1.0 do GDesktop. Não altera o aplicativo instalado, a fila, o índice ou os PDFs originais. Não há integração automática de OCR no produto nesta etapa.

## Resultado neste computador

- Windows build 26200, OCR instalado em `pt-BR` e `fr-FR`.
- Motor `Windows.Media.Ocr.OcrEngine`, idioma selecionado `pt-BR`.
- Funcionou tanto no host .NET quanto no executável autocontido, sem identidade de pacote/MSIX nesta máquina.
- Renderização de PDF por `Windows.Data.Pdf`, seguida de decodificação BGRA8 e reconhecimento. Não precisou de PDFium nem Tesseract.
- Exemplo gerado em português: texto, acentos, moeda, data e códigos de controle reconhecidos corretamente na imagem e nas duas páginas rasterizadas. Confirmado com PdfPig que o PDF de exemplo não possui camada textual.
- Executável publicado: imagem em aproximadamente 61 ms e duas páginas em 352 ms de processamento, com pico aproximado de 172 MB no processo. Medidas ilustrativas deste exemplo limpo e desta máquina.
- PDF técnico fornecido no projeto: quatro páginas renderizadas e reconhecidas em aproximadamente 848 ms de processamento (1,1 s incluindo inicialização). Os termos de controle SQLite, FileSystemWatcher e OCR foram encontrados; o hash do PDF original permaneceu igual. Esse PDF contém texto digital e serve como teste de renderização/layout, não como validação de um scanner real.
- Arquivo de imagem temporário com atributo Offline foi recusado antes da leitura do conteúdo; isso testa a guarda, sem reproduzir toda a sincronização de um provedor de nuvem.
- Teste posterior com PDF digitalizado real: cinco páginas sem camada textual, em aproximadamente 2,34 s a 200 DPI e 4,05 s a 300 DPI. Boa leitura do texto corrido por amostragem, com erros relevantes nas tabelas pequenas. O documento em inglês foi processado com o modelo pt-BR disponível. Detalhes em `REAL-PDF-TEST.md`.

Resultados gerados em `tmp/windows-ocr-published/result.json`, exemplo de PDF em `tmp/windows-ocr-published/digitalizado.pdf` e medição do documento técnico em `tmp/windows-ocr-published/reference-pdf-result.json`.

## Executar

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.cli"
$env:NUGET_PACKAGES = "$PWD/.nuget"
./.dotnet/dotnet.exe build experiments/WindowsOcr/WindowsOcr.csproj -c Release
./.dotnet/dotnet.exe experiments/WindowsOcr/bin/Release/net10.0-windows10.0.19041.0/WindowsOcr.dll --self-test tmp/windows-ocr
```

Executável autocontido de teste: `artifacts/experiments/WindowsOcr/WindowsOcr.exe`.

Comandos: `--probe`, `--self-test pasta`, `--pdf arquivo.pdf` e `--image arquivo.png`. Os dois últimos imprimem o texto reconhecido; o PDF original permanece intacto. O experimento limita arquivos a 32 MB, PDFs às primeiras cinco páginas e renderização a aproximadamente 200 DPI/2800 pixels no lado maior. Reconhecimento tem espera máxima de 30 segundos por página. Os limites de trabalho/memória do processo isolado da aplicação ainda precisam ser integrados.

Diagnóstico adicional: `--inspect-pdf arquivo.pdf` conta páginas/caracteres digitais; `--pdf arquivo.pdf pasta-de-imagens 300` salva as páginas renderizadas e compara OCR a 300 DPI (limite de 4000 pixels). O terceiro argumento opcional é a pasta de imagens e o quarto é a resolução entre 100 e 400 DPI. As imagens/textos derivados podem conter informação pessoal e permanecem em disco local; não os inclua em uma distribuição do projeto.

## Antes de integrar

1. Validar com PDF realmente digitalizado: resolução, inclinação, ruído, tabelas, comprovantes e texto pequeno. Os exemplos sintéticos não representam toda essa variedade.
2. Decidir por página quando usar OCR em PDFs mistos, preservando o texto digital existente.
3. Integrar fila de baixa prioridade, pausa/retomada, cache e referência à origem do texto reconhecido.
4. Verificar indisponibilidade do idioma e compatibilidade de distribuição em outras versões do Windows. A execução sem identidade de pacote foi observada nesta máquina e não é garantia universal.
5. Repetir a validação de arquivos somente online no fluxo integrado. O experimento apenas recusa atributos Offline/Recall; não oferece garantia atômica contra mudança de disponibilidade durante a abertura.

O inglês não está instalado como idioma de OCR nesta máquina. Nenhum pacote de idioma, configuração de Windows ou MSIX foi instalado para o teste.

Referências oficiais: [OcrEngine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine), [WinRT em aplicativos .NET desktop](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps), [RenderToStreamAsync](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync).
