# Validação

## Aplicativo e instalador

- Busca local por nomes e conteúdo, operadores booleanos, frases, acentos, filtros e destaques verificados por testes de integração.
- Autenticação local, código temporário, Host/Origin e proteção de mutações verificados.
- Atualização, renomeação, exclusão, pausa/reinício e consolidação de pastas com preservação de IDs e conteúdo verificados.
- DOCX, XLSX e PPTX testados com amostras sintéticas, incluindo tabelas, notas, texto formatado, datas e fórmulas salvas. Arquivos inválidos, antigos e protegidos são tratados sem bloquear a fila.
- Windows OCR testado com PNG e PDF rasterizado sintéticos. Busca entre páginas, preservação dos originais e retomada do índice verificadas.
- Mudanças apenas nos atributos de disponibilidade foram testadas, com retorno à indexação quando o arquivo fica local. Testes reais adicionais com uma pasta pequena do OneDrive confirmaram as transições; não são uma garantia para todos os provedores ou situações de concorrência.
- Interface testada no Microsoft Edge, incluindo colagem, popup do README, pausa/retomada e layout desktop/mobile.
- Instalador testado em pasta isolada: arquivos, registro, atalhos, reinstalação, desinstalação e preservação do índice pessoal.
- Ícone próprio verificado no executável e no atalho real da área de trabalho.
- Instalação em outro Windows limpo confirmada com sucesso pelo responsável pelo projeto.

## Preparação da distribuição pública

- Histórico novo e independente, com endereço noreply nos metadados do commit inicial.
- Nenhum PDF pessoal, banco de indexação ou resultado de OCR real foi incluído no Git.
- As suítes usam PDF e arquivos Office sintéticos; não dependem de documentos externos ao repositório.
- Suítes gerais, de Office, interface e OCR passaram novamente na cópia pública.
- Licença MIT para o código original e avisos de terceiros incluídos na publicação autocontida.
- Instalador público recompilado a partir desta cópia, com links públicos no README. Símbolos de depuração, bancos e chaves são excluídos do pacote.
- Instalação e desinstalação do pacote público verificadas em pasta isolada; README público, MIT, avisos de terceiros e ícone confirmados na pasta instalada. Nenhum PDF, banco, símbolo de depuração ou chave foi encontrado no pacote instalado.

## Limites

Os testes não constituem auditoria completa de segurança nem benchmark de grandes acervos. O instalador ainda não possui assinatura digital própria. O OCR depende de um idioma instalado no Windows e pode errar letras, números e estrutura de tabelas. Consulte README.md para limites de tamanho, formatos, arquivos somente online e subpastas com reparse points.
