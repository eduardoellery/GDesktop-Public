# Comparação local: Windows OCR × Tesseract

Teste isolado do GDesktop, sem alteração no aplicativo instalado, no índice, nos PDFs ou nas imagens originais. Nenhum documento foi enviado para serviços externos. Os dados de idioma foram baixados para `tools/tesseract-data`, sem instalação global do Tesseract ou de idiomas adicionais no Windows.

## Ambiente e método

- Windows OCR: modelo instalado `pt-BR`. Não havia modelo inglês disponível; o francês também estava instalado, mas não foi utilizado.
- Tesseract: wrapper NuGet `TesseractOCR 5.5.2`; a biblioteca nativa reportou **5.5.0** pela API em tempo de execução. Idiomas carregados Português/English foram verificados pela API.
- Modelos oficiais `tessdata_fast` e `tessdata_best`, com `por` e `eng`, modo LSTM e segmentação automática padrão. Tesseract executado com `OMP_THREAD_LIMIT=1`.
- Mesmas imagens PNG, sem tratamento adicional: uma imagem de controle em português, cinco páginas do PDF real a 200 DPI e as mesmas cinco a 300 DPI. Hashes das imagens permaneceram iguais.
- Um motor é criado por configuração e reutilizado nas páginas. Os tempos incluem carregar/decodificar imagem e reconhecer; **não incluem renderizar o PDF nem inicializar o motor**. Medições de uma passagem, indicativas desta máquina, sem isolamento de carga do computador ou análise estatística.
- Os modelos oficiais e seus hashes estão registrados em `tmp/ocr-compare/environment.json`, junto da versão nativa real. O prefixo global TESSDATA_PREFIX não estava definido.

## Exemplo em português com texto conhecido

Comparação do texto inteiro com referência conhecida. Taxa de erro de caracteres normaliza caixa e espaços; taxa de erro de palavras compara palavras/números por distância de edição. O exemplo é limpo e curto, sem tabela.

| Configuração | Tempo | Erros de caracteres | Erros de palavras |
|---|---:|---:|---:|
| Windows pt-BR | 95 ms | 0% | 0% |
| Tesseract fast/por | 128 ms | 0% | 0% |
| Tesseract best/por | 231 ms | 0% | 0% |

Isso não demonstra precisão perfeita em outros documentos.

## PDF real digitalizado em inglês: cinco páginas

Para amostrar texto corrido, foram transcritos manualmente dois trechos de 53 palavras, nas páginas 1 e 4. Windows acertou as 106 palavras; cada configuração Tesseract teve um erro na amostra de 106 palavras. Não é medição de precisão do documento completo.

Para observar as tabelas pequenas, foram conferidos visualmente **dez valores monetários da página 4**. A coluna abaixo conta a presença literal de cada valor em algum ponto da página, ignorando o sinal de moeda e vírgulas de milhar. Não verifica associação com linhas/colunas nem equivale a precisão geral de uma tabela. A referência e o recorte visual permanecem apenas em `tmp/ocr-compare`.

| Configuração | Tempo a 200 DPI | Valores encontrados / 10 | Tempo a 300 DPI | Valores encontrados / 10 |
|---|---:|---:|---:|---:|
| Windows pt-BR | 0,64 s | 4 | 1,18 s | 5 |
| Tesseract fast/por | 3,72 s | 4 | 6,54 s | 4 |
| Tesseract best/por | 7,81 s | 4 | 11,62 s | 5 |
| Tesseract fast/eng | 3,75 s | 5 | 6,47 s | 9 |
| Tesseract best/eng | 7,64 s | 4 | 10,62 s | 8 |

O modelo denominado best não superou fast nessa amostra de valores. O Tesseract emitiu avisos de segmentação na página 2 e de detecção de diacríticos na página 4; as páginas foram processadas e produziram texto. Não foram usados os níveis de confiança do motor como medida de precisão.

## Conclusão prática e limites

- Windows foi mais rápido e teve boa leitura do texto corrido. Na comparação de modelos em português, Tesseract não demonstrou uma vantagem clara neste material.
- Tesseract fast em inglês, a 300 DPI, encontrou mais dos pequenos valores conferidos. É uma configuração promissora para documentos difíceis em inglês, com custo de processamento maior.
- **Idioma é um fator de confusão:** comparar Windows pt-BR com Tesseract eng não permite atribuir toda a diferença ao motor. Ainda é necessário repetir com Windows OCR em inglês para uma decisão definitiva sobre esse documento.
- É apenas um PDF real e um exemplo sintético. Inclinação, qualidade da imagem, tabelas, idioma e segmentação podem mudar o resultado. Nenhuma configuração deve ser tratada como transcrição fiel de tabelas financeiras sem conferência.
- Direção provisória para o aplicativo: Windows OCR como opção rápida e Tesseract como possível alternativa de qualidade/idioma. A escolha definitiva requer mais amostras e o teste do modelo inglês do Windows.

**Nenhum OCR automático foi ativado no produto por este teste.**

## Reproduzir

O experimento utiliza os PNGs e resultados da prova anterior em `tmp/windows-ocr-published`, `tmp/windows-ocr-sample` e `tmp/windows-ocr-sample-300`.

Baixe `eng.traineddata` e `por.traineddata` dos repositórios oficiais abaixo para `tools/tesseract-data/fast` e `tools/tesseract-data/best`. Os dados são ignorados pelo Git.

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.cli"
$env:NUGET_PACKAGES = "$PWD/.nuget"
$env:OMP_THREAD_LIMIT = '1'
./.dotnet/dotnet.exe build experiments/OcrCompare/OcrCompare.csproj -c Release
./.dotnet/dotnet.exe experiments/OcrCompare/bin/Release/net10.0-windows10.0.19041.0/OcrCompare.dll
./.dotnet/dotnet.exe experiments/OcrCompare/bin/Release/net10.0-windows10.0.19041.0/OcrCompare.dll --probe
node experiments/OcrCompare/analyze.mjs
```

A análise precisa da referência manual em `tmp/ocr-compare/reference.json`, que contém trechos do documento pessoal e não faz parte dos arquivos distribuídos. Resultados brutos e métricas ficam em `tmp/ocr-compare/results.json` e `metrics.json`. Não distribua essas pastas temporárias junto do aplicativo.

Fontes técnicas: [modelos oficiais e diferenças](https://tesseract-ocr.github.io/tessdoc/Data-Files.html), [tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast), [tessdata_best](https://github.com/tesseract-ocr/tessdata_best), [wrapper .NET](https://github.com/Sicos1977/TesseractOCR), [Windows OcrEngine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine).
