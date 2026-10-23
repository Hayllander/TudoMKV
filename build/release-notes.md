Duas opções, com o .NET e o FFmpeg já dentro (não precisa instalar mais nada):

- **TudoMKV-…-setup.exe** — instala em `%LOCALAPPDATA%\Programs\TudoMKV`, cria o atalho no Menu Iniciar e a entrada de desinstalação em Aplicativos e recursos. Não pede administrador. O Windows pode avisar que o aplicativo não é reconhecido, porque ele ainda não tem assinatura digital: clique em "Mais informações" e "Executar assim mesmo".
- **TudoMKV-…-win-x64.zip** — versão portátil: extraia a pasta e abra `TudoMKV.exe`.

Como usar
---------

Funciona como o MakeMKV: em Arquivo > Abrir arquivo, escolha o `VIDEO_TS.IFO` (DVD), o `index.bdmv` ou o `MovieObject.bdmv` (Blu-ray) ou vídeos comuns. Marque títulos e faixas na árvore e clique em Make MKV.

O MKV sai marcado como gravado pelo TudoMKV, com a data, os nomes das faixas de áudio (`English DTS-HD MA 5.1`, `Portuguese AC3 2.0`…), os nomes das legendas, o idioma em três letras e o ID de cada faixa no disco de origem, como o MediaInfo mostra nos arquivos do MakeMKV.

Novidades desta versão (desde a v0.5.10)
----------------------------------------

**Nomes de faixa no padrão das releases**

- As faixas de **áudio** passam a ser nomeadas como nos lançamentos: idioma, codec e canais — `English DTS-HD MA 5.1`, `Chinese DTS-HD Master Audio 5.1`, `Portuguese AC3 2.0`. O nome já conta o que a gravação reencoda, então um PCM mono que sai em AC-3 aparece como `English AC3 1.0`.
- As faixas de **legenda** saem como `Portuguese (Subtitles)` e `English (Subtitles)`, em vez do nome que viesse da origem.
- O nome anterior, quando a origem trazia um, é sempre substituído por esse padrão; o idioma é gravado em **três letras** (`por`, `eng`), como o Matroska espera.
- Tudo o que é gravado nas faixas fica em **inglês**, como os capítulos.
- Ao terminar cada MKV, a console mostra o **resumo de cada faixa de áudio** — `Track 1: English / DTS-HD Master Audio / 5.1 / 48.0 kHz / 2178 kb/s / 16-bit` — lido do arquivo já gravado.

**Capturas de tela**

- Nova opção em **Preferências > Muxing**: **ScreenShots**. Ligada, cada MKV gravado ganha **15 PNGs** entre 5% e 95% do filme, na mesma pasta, no padrão `Nome_screenshot_001.png`. A imagem sai no tamanho de exibição — um DVD 16:9 de 720x480 vira 853x480, sem achatamento.

**Preferências e ficha do filme**

- O **TMDb** passa a ser o provedor padrão, e um `settings.json` antigo que ficou gravado como "Nenhum" é migrado no primeiro uso — antes, qualquer salvamento das Preferências acabava desligando a busca sem querer.
- O **código do IMDb agora funciona sem chave do OMDb**: sem ela, o TMDb traduz o código para o filme dele. O código também pode ser digitado sem o `tt` (o `0111161` de *Um Sonho de Liberdade*).
- A **assinatura do grupo** é limpa antes de entrar no nome do arquivo: espaços e caracteres inválidos no Windows não quebram mais a gravação.

Novidades da v0.5.10
--------------------

**O áudio do disco tratado como num remux**

- No **Blu-ray**, toda faixa **LPCM/PCM** passa a ser reencodada em **FLAC sem perda**, mantendo os canais, a taxa de amostragem e a profundidade — o PCM 7.1 do *Painted Skin* sai como um FLAC 7.1 idêntico, com cerca de um terço do tamanho.
- No **DVD**, toda faixa **PCM/LPCM** passa a sair em **AC-3 a 192 kbps**, mantendo os canais: o PCM original não entra mais no MKV carregando 1,5 Mb/s sem necessidade.
- Todo **áudio mono** de disco ganha uma faixa **estéreo 2.0 marcada como padrão**, para a reprodução não reservar surpresas: **FLAC sem perda** quando a origem é sem perda (LPCM, TrueHD, FLAC ou DTS-HD MA) e **AC-3 a 192 kbps** quando a origem tem perda. A faixa mono original continua no arquivo, desmarcada.
- O **DTS-HD MA** passa a ser reconhecido pelo perfil que o ffprobe devolve: antes ele era lido como DTS comum e o mono desse codec acabava virando AC-3 em vez de FLAC.
- **Arquivos comuns** (`.ts`, `.mkv`) recebem o mesmo tratamento dos discos: nomes de faixa e marcas de padrão, mais a conversão do PCM em FLAC.

**Nome do MKV no padrão das releases**

- Quando o filme é identificado no **TMDb ou no IMDb** e há **um único título marcado**, o MKV passa a ser gravado com o nome no formato das releases de cena:
  - DVD: `Bastard.Swordsman.1983.DVD.REMUX.DD2.0.MPEG2-HAY`
  - Blu-ray: `Painted.Skin.2008.1080p.BluRay.REMUX.AVC.DTS-HD.MA7.1-HAY`
- O nome sai da ficha (título e ano) e da origem (resolução, codec de vídeo e o codec da **faixa de áudio padrão**, já contando o que a gravação reencoda).
- Com **mais de um título marcado**, ou sem ficha verificada, cada arquivo continua com o nome do disco (`_t01`, `_t02`…), que é o que os distingue.

**Capítulos, preferências e console**

- Os capítulos saem em inglês, numerados `Chapter 1`, `Chapter 2`…, como nas releases.
- Opção em **Preferências > Muxing** para marcar os MKVs de vídeo **MPEG-2** como gravados pelo `libmakemkv`, o que evita a imagem verde do VLC 3 ao avançar o vídeo com aceleração por hardware.
- A assinatura do grupo (`-HAY`) é **configurável nas Preferências**, na aba Muxing; em branco, o MKV sai sem assinatura.
- A console é limpa automaticamente ao abrir uma nova origem, e o botão de limpar o log apaga também o link da pasta de destino.

O que já vinha desde a v0.5.2
-----------------------------

- Nome de release, capítulos em inglês e as primeiras opções de Muxing descritas acima, além dos ajustes de extração de DVD e Blu-ray das versões anteriores.

Discos com proteção contra cópia (CSS, AACS) não são suportados. Veja o LEIA-ME.txt dentro do pacote.

Contribuidores
--------------

- Clediomar do Carmo Santos
- Trae (assistente de IA), no código, nos testes e no empacotamento

Versão anterior: [v0.5.10](https://github.com/Hayllander/TudoMKV/releases/tag/v0.5.10)
