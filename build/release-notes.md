Duas opções, com o .NET e o FFmpeg já dentro (não precisa instalar mais nada):

- **TudoMKV-…-setup.exe** — instala em `%LOCALAPPDATA%\Programs\TudoMKV`, cria o atalho no Menu Iniciar e a entrada de desinstalação em Aplicativos e recursos. Não pede administrador. O Windows pode avisar que o aplicativo não é reconhecido, porque ele ainda não tem assinatura digital: clique em "Mais informações" e "Executar assim mesmo".
- **TudoMKV-…-win-x64.zip** — versão portátil: extraia a pasta e abra `TudoMKV.exe`.

Como usar
---------

Funciona como o MakeMKV: em Arquivo > Abrir arquivo, escolha o `VIDEO_TS.IFO` (DVD), o `index.bdmv` ou o `MovieObject.bdmv` (Blu-ray) ou vídeos comuns. Marque títulos e faixas na árvore e clique em Make MKV.

O MKV sai marcado como gravado pelo TudoMKV, com a data, os nomes das faixas de áudio (Stereo, Surround 5.1…) e o ID de cada faixa no disco de origem, como o MediaInfo mostra nos arquivos do MakeMKV.

Novidades desta versão (desde a v0.4.9)
---------------------------------------

**Metadados pelo TMDb, com o OMDb de reserva**

- O **TMDb** passa a ser a fonte principal das fichas dos filmes: em uma única consulta ele traz a sinopse completa, o gênero, o país, o estúdio, a duração, o idioma e o pôster. A chave da API já vem preenchida, então a busca funciona sem configurar nada.
- A **referência digitada no campo Filme decide quem responde** pela consulta:
  - `tt0887736` ou o link do IMDb vão para o **OMDb**, como antes;
  - `223883`, `https://www.themoviedb.org/movie/223883` ou um link embrulhado em outro endereço (por exemplo `https://anon.to/?https://www.themoviedb.org/movie/91970`) vão para o **TMDb**.
- O TMDb é a fonte principal e o OMDb entra como **reserva**: quando o TMDb não trouxer título ou sinopse, a ficha é completada pelo OMDb, usando o código IMDb que o próprio TMDb devolve no campo `imdb_id`. Se a reserva falhar, a consulta continua válida com o que o TMDb já trouxe.
- A janela de informações do filme ganhou **pôster**, notas do IMDb e do TMDb e a lista completa dos campos. Ela agora é **redimensionável** e a sinopse tem área própria com rolagem: antes, a descrição chegava a ficar escondida quando a lista de campos crescia.
- As etiquetas gravadas no MKV passam a levar também os identificadores **`imdb`** e **`tmdb`**, somados a título, ano, diretor, gênero, país, estúdio e descrição.
- Nas Preferências (aba Integração) há dois campos de chave — TMDb e OMDb — e a opção de integração passou a se chamar **"TMDb, com o OMDb de reserva"**.

**Log mais informativo, com link para a pasta gravada**

- A primeira linha do log identifica o aplicativo e a plataforma: `TudoMKV v0.5.2 win(x64-release) Iniciado`.
- Na abertura, o TudoMKV consulta a última versão publicada no GitHub e, quando existe uma mais nova, avisa no log com o endereço de download. Sem internet, o aviso simplesmente não aparece e o aplicativo abre normalmente.
- No fim da gravação, a mensagem mostra a **pasta de destino entre aspas** e, logo abaixo do log, aparece um **link clicável** com o caminho. O clique abre a pasta exata onde os MKVs foram gravados.

O que muda no uso
-----------------

- O Explorer **não abre mais sozinho** ao terminar a gravação: a pasta só abre quando você clicar no link abaixo do log. E o link leva à pasta dos arquivos gerados, não à raiz do disco.
- Informar o código do IMDb continua funcionando exatamente como antes; o que mudou é que agora existe também o caminho pelo TMDb, com ficha mais completa.

Discos com proteção contra cópia (CSS, AACS) não são suportados. Veja o LEIA-ME.txt dentro do pacote.

Contribuidores
--------------

- Clediomar do Carmo Santos
- Trae (assistente de IA), no código, nos testes e no empacotamento

Versão anterior: [v0.4.9](https://github.com/Hayllander/TudoMKV/releases/tag/v0.4.9)
