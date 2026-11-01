# Teste com PDF digitalizado real

Executado localmente no arquivo fornecido pelo usuário, sem envio para serviços externos ou alteração do original. O arquivo estava disponível localmente no OneDrive, com aproximadamente 5,7 MiB, cinco páginas e nenhuma camada textual nas cinco páginas.

## Medições

| Configuração | Tempo total aproximado | Caracteres por página |
|---|---|---|
| 200 DPI, BGRA8, OCR pt-BR | 2,34 s, incluindo inicialização e imagens de diagnóstico | 1508 / 1027 / 1608 / 1435 / 90 |
| 300 DPI, BGRA8, OCR pt-BR | 4,05 s, incluindo inicialização e imagens de diagnóstico | 1510 / 1026 / 1607 / 1950 / 90 |

Tempo não é benchmark de produção: depende do computador e inclui operações auxiliares. Quantidade de caracteres não mede precisão.

## Avaliação

- O texto corrido e os títulos apresentaram boa leitura na comparação visual por amostragem.
- A fotografia tem inclinação, sombra/coloração do papel e tabelas com letras pequenas. Nas tabelas houve erros de letras, números e ordem das células. A maior resolução capturou mais conteúdo, mas continuou imprecisa.
- O documento está em inglês e foi reconhecido com o motor pt-BR, pois os únicos idiomas OCR instalados eram português do Brasil e francês. Não foi instalado idioma adicional. Uma avaliação com o modelo inglês ainda é necessária antes de concluir a melhor configuração para esse tipo de documento.
- O hash SHA-256 do original permaneceu igual após o primeiro processamento; nenhum PDF com camada OCR foi gravado sobre o arquivo original.
- O experimento mantém texto e imagens de diagnóstico apenas nas pastas temporárias locais `tmp/windows-ocr-sample` e `tmp/windows-ocr-sample-300`. Não foram adicionados ao índice do produto.

Conclusão: o Windows OCR é promissor para encontrar palavras no texto corrido deste caso, mas o reconhecimento não deve ser tratado como transcrição fiel das tabelas ou valores. Ainda é uma prova separada, não uma integração de OCR à versão 1.0.
