Duas opções, com o .NET e o FFmpeg já dentro (não precisa instalar mais nada):

- **TudoMKV-…-setup.exe** — instala em `%LOCALAPPDATA%\Programs\TudoMKV`, cria o atalho no Menu Iniciar e a entrada de desinstalação em Aplicativos e recursos. Não pede administrador. O Windows pode avisar que o aplicativo não é reconhecido, porque ele ainda não tem assinatura digital: clique em "Mais informações" e "Executar assim mesmo".
- **TudoMKV-…-win-x64.zip** — versão portátil: extraia a pasta e abra `TudoMKV.exe`.

Como usar
---------

Funciona como o MakeMKV: em Arquivo > Abrir arquivo, escolha o `VIDEO_TS.IFO` (DVD), o `index.bdmv` ou o `MovieObject.bdmv` (Blu-ray) ou vídeos comuns. Marque títulos e faixas na árvore e clique em Make MKV.

O MKV sai marcado como gravado pelo TudoMKV, com a data, os nomes das faixas de áudio (Stereo, Surround 5.1…) e o ID de cada faixa no disco de origem, como o MediaInfo mostra nos arquivos do MakeMKV.

Novidades desta versão (desde a v0.4.1)
---------------------------------------

- **DVD danificado**: quando o demuxer de DVD do FFmpeg estraga o vídeo, o título passa a ser montado pelos VOBs físicos do disco (vídeo e áudio), ficando as legendas e os capítulos com o IFO. Era o caso do *Dog and Wolf*, que saía com quadros corrompidos.
- **Duração e capítulos do DVD**: passam a seguir o tempo do IFO com o fator 1,001 do NTSC, batendo com o MakeMKV (antes o erro chegava a 4 segundos no fim do título).
- **VLC**: o MKV de vídeo MPEG-2 recebe a marca de aplicativo de muxagem usada pelo MakeMKV, que evita a imagem esverdeada depois de avançar com a decodificação por hardware ligada. O arquivo continua identificado como TudoMKV.
- **Abertura de disco**: o seletor prioriza o `VIDEO_TS.IFO` (DVD) e o `index.bdmv`/`MovieObject.bdmv` (Blu-ray), como o MakeMKV.
- **Preferências**: aba Integration, para informar a chave da OMDb usada em Verificar.
- **Verificar**: além de preencher as tags do MKV, abre um pop-up com os dados do título, a nota, o elenco e o poster.
- **Conforto**: o log pode ser selecionado e copiado, e a pasta de saída abre no Explorer quando a gravação termina.

Discos com proteção contra cópia (CSS, AACS) não são suportados. Veja o LEIA-ME.txt dentro do pacote.

Contribuidores
--------------

- Clediomar do Carmo Santos
- Trae (assistente de IA), no código, nos testes e no empacotamento

Versão anterior: [v0.4.1](https://github.com/Hayllander/TudoMKV/releases/tag/v0.4.1)
